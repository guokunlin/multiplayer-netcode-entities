using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
#if !NET_DOTS
using System.Linq;
#endif
using UpdateOrderSystemSorter = Unity.Entities.ComponentSystemSorter<Unity.Entities.UpdateBeforeAttribute, Unity.Entities.UpdateAfterAttribute>;

namespace Unity.Entities
{
    internal struct UpdateIndex
    {
        private ushort Data;

        public bool IsManaged => (Data & 0x8000) != 0;
        public int Index => Data & 0x7fff;

        public UpdateIndex(int index, bool managed)
        {
            Data = (ushort) index;
            Data |= (ushort)((managed ? 1 : 0) << 15);
        }

        override public string ToString()
        {
            return IsManaged ? "Managed: Index " + Index : "UnManaged: Index " + Index;
        }
    }

    /// <summary>
    /// A special-case system that encapsulates an ordered list of other systems. When the group is updated, the group's
    /// member systems are updated in order.
    /// </summary>
    [DebuggerTypeProxy(typeof(ComponentSystemGroupDebugView))]
    public abstract unsafe partial class ComponentSystemGroup : SystemBase
    {
        private bool m_systemSortDirty = false;

        // Initial memory block size in bytes
        const int InitialSystemGroupAllocatorBlockSizeInBytes = 128 * 1024;    // 128k

        // If true (the default), calling SortSystems() will sort the system update list, respecting the constraints
        // imposed by [UpdateBefore] and [UpdateAfter] attributes. SortSystems() is called automatically during
        // DefaultWorldInitialization, as well as at the beginning of ComponentSystemGroup.OnUpdate(), but may also be
        // called manually.
        //
        // If false, calls to SortSystems() on this system group will have no effect on update order of systems in this
        // group (though SortSystems() will still be called recursively on any child system groups). The group's systems
        // will update in the order of the most recent sort operation, with any newly-added systems updating in
        // insertion order at the end of the list.
        //
        // Setting this value to false is not recommended unless you know exactly what you're doing, and you have full
        // control over the systems which will be updated in this group.
        private bool m_EnableSystemSorting = true;

        /// <summary>If true (the default), calling SortSystems() will sort the system update list, respecting the constraints
        /// imposed by [UpdateBefore] and [UpdateAfter] attributes.</summary>
        /// <remarks>SortSystems() is called automatically during
        /// DefaultWorldInitialization, as well as at the beginning of ComponentSystemGroup.OnUpdate(), but may also be
        /// called manually.
        ///
        /// If false, calls to SortSystems() on this system group will have no effect on update order of systems in this
        /// group (though SortSystems() will still be called recursively on any child system groups). The group's systems
        /// will update in the order of the most recent sort operation, with any newly-added systems updating in
        /// insertion order at the end of the list.
        ///
        /// Setting this value to false is not recommended unless you know exactly what you're doing, and you have full
        /// control over the systems which will be updated in this group.
        /// </remarks>
        public bool EnableSystemSorting
        {
            get => m_EnableSystemSorting;
            protected set
            {
                if (value && !m_EnableSystemSorting)
                    m_systemSortDirty = true; // force a sort after re-enabling sorting
                m_EnableSystemSorting = value;
            }
        }

        /// <summary>
        /// Checks if the system group is in a fully initialized and valid state
        /// </summary>
        public bool Created { get; private set; } = false;

        internal List<ComponentSystemBase> m_managedSystemsToUpdate = new List<ComponentSystemBase>();
        internal List<ComponentSystemBase> m_managedSystemsToRemove = new List<ComponentSystemBase>();

        internal UnsafeList<UpdateIndex> m_MasterUpdateList;
        internal UnsafeList<SystemHandle> m_UnmanagedSystemsToUpdate;
        internal UnsafeList<SystemHandle> m_UnmanagedSystemsToRemove;

        /// <summary>
        /// The ordered list of managed systems in this group.
        /// </summary>
        public virtual IReadOnlyList<ComponentSystemBase> Systems => m_managedSystemsToUpdate;
        internal UnsafeList<SystemHandle> UnmanagedSystems => m_UnmanagedSystemsToUpdate;

        internal DoubleRewindableAllocators* m_RateGroupAllocators = null;
        internal byte RateGroupAllocatorsCreated { get; set; } = 0;

        private IRateManager m_RateManager = null;

        /// <inheritdoc cref="SystemBase.OnCreate"/>
        protected override void OnCreate()
        {
            base.OnCreate();
            m_UnmanagedSystemsToUpdate = new UnsafeList<SystemHandle>(0, Allocator.Persistent);
            m_UnmanagedSystemsToRemove = new UnsafeList<SystemHandle>(0, Allocator.Persistent);
            m_MasterUpdateList = new UnsafeList<UpdateIndex>(0, Allocator.Persistent);
            Created = true;
        }

        /// <inheritdoc cref="SystemBase.OnDestroy"/>
        protected override void OnDestroy()
        {
            if (RateGroupAllocatorsCreated == 1)
            {
                DestroyRateGroupAllocators();
            }
            m_MasterUpdateList.Dispose();
            m_UnmanagedSystemsToRemove.Dispose();
            m_UnmanagedSystemsToUpdate.Dispose();
            base.OnDestroy();
            Created = false;
        }

        private void CheckCreated()
        {
            if (!Created)
                throw new InvalidOperationException($"Group of type {GetType()} has not been created, either the derived class forgot to call base.OnCreate(), or it has been destroyed");
        }

        /// <summary>
        /// Appends a managed system to the group's update list. The list will be sorted the next time the group is updated.
        /// </summary>
        /// <param name="sys">The system to add.</param>
        /// <exception cref="ArgumentException">Thrown if a group is added to itself.</exception>
        public void AddSystemToUpdateList(ComponentSystemBase sys)
        {
            CheckCreated();

            if (sys != null)
            {
                if (this == sys)
                    throw new ArgumentException($"Can't add {TypeManager.GetSystemName(GetType())} to its own update list");

                // Check for duplicate Systems. Also see issue #1792
                if (m_managedSystemsToUpdate.IndexOf(sys) >= 0)
                {
                    if (m_managedSystemsToRemove.Contains(sys))
                    {
                        m_managedSystemsToRemove.Remove(sys);
                    }
                    return;
                }

                m_MasterUpdateList.Add(new UpdateIndex(m_managedSystemsToUpdate.Count, true));
                m_managedSystemsToUpdate.Add(sys);
                m_systemSortDirty = true;
            }
        }

        private int UnmanagedSystemIndex(SystemHandle sysHandle)
        {
            int len = m_UnmanagedSystemsToUpdate.Length;
            var ptr = m_UnmanagedSystemsToUpdate.Ptr;
            for (int i = 0; i < len; ++i)
            {
                if (ptr[i] == sysHandle)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Appends an unmanaged system to the group's update list. The list will be sorted the next time the group is updated.
        /// </summary>
        /// <param name="sysHandle">The system to add</param>
        public void AddSystemToUpdateList(SystemHandle sysHandle)
        {
            CheckCreated();

            if (sysHandle == default)
                return;
            var state = World.Unmanaged.ResolveSystemState(sysHandle);

            bool isManaged = state->m_ManagedSystem.IsAllocated;
            if (isManaged)
            {
                var sys = (ComponentSystemBase)state->m_ManagedSystem.Target;

                this.AddSystemToUpdateList(sys);
            }
            else
            {
                if (-1 != UnmanagedSystemIndex(sysHandle))
                {
                    int index = m_UnmanagedSystemsToRemove.IndexOf(sysHandle);
                    if (-1 != index)
                        m_UnmanagedSystemsToRemove.RemoveAt(index);
                    return;
                }

                m_MasterUpdateList.Add(new UpdateIndex(m_UnmanagedSystemsToUpdate.Length, false));
                m_UnmanagedSystemsToUpdate.Add(sysHandle);
            }

            m_systemSortDirty = true;
        }

        /// <summary>
        /// Requests that a managed system be removed from the group's update list. The system will be removed the next time the group is sorted.
        /// </summary>
        /// <param name="sys">The system to remove</param>
        public void RemoveSystemFromUpdateList(ComponentSystemBase sys)
        {
            CheckCreated();

            if (m_managedSystemsToUpdate.Contains(sys) && !m_managedSystemsToRemove.Contains(sys))
            {
                m_systemSortDirty = true;
                m_managedSystemsToRemove.Add(sys);
            }
        }

        /// <summary>
        /// Requests that an unmanaged system be removed from the group's update list. The system will be removed the next time the group is sorted.
        /// </summary>
        /// <param name="sys">The system to remove</param>
        public void RemoveSystemFromUpdateList(SystemHandle sys)
        {
            CheckCreated();

            if (m_UnmanagedSystemsToUpdate.Contains(sys) && !m_UnmanagedSystemsToRemove.Contains(sys))
            {
                m_systemSortDirty = true;
                m_UnmanagedSystemsToRemove.Add(sys);
            }
        }

        private void RemovePending()
        {
            if (m_managedSystemsToRemove.Count > 0)
            {
                foreach (var sys in m_managedSystemsToRemove)
                {
                    m_managedSystemsToUpdate.Remove(sys);
                }

                m_managedSystemsToRemove.Clear();
            }

            for(int i=0; i<m_UnmanagedSystemsToRemove.Length; ++i)
            {
                var sysHandle = m_UnmanagedSystemsToRemove[i];
                m_UnmanagedSystemsToUpdate.RemoveAt(m_UnmanagedSystemsToUpdate.IndexOf(sysHandle));
            }

            m_UnmanagedSystemsToRemove.Clear();
        }


        private void RemoveSystemsFromUnsortedUpdateList()
        {
            if (m_managedSystemsToRemove.Count <= 0 && m_UnmanagedSystemsToRemove.Length <= 0)
                return;

            var world = World.Unmanaged;
            int largestID = 0;

            //determine the size of the lookup table used for looking up system information; whether a system is due to be removed
            //and/or the new update index of the system
            foreach (var managedSystem in m_managedSystemsToUpdate)
            {
                largestID = math.max(largestID, managedSystem.CheckedState()->m_SystemID);
            }
            foreach (var unmanagedSystem in m_UnmanagedSystemsToUpdate)
            {
                largestID = math.max(largestID, world.ResolveSystemState(unmanagedSystem)->m_SystemID);
            }

            var newListIndices = new NativeArray<int>(largestID + 1, Allocator.Temp);
            var systemIsRemoved = new NativeArray<byte>(largestID + 1, Allocator.Temp,NativeArrayOptions.ClearMemory);

            //update removed system lookup table
            foreach (var managedSystem in m_managedSystemsToRemove)
            {
                systemIsRemoved[managedSystem.CheckedState()->m_SystemID] = 1;
            }

            foreach (var unmanagedSystem in m_UnmanagedSystemsToRemove)
            {
                systemIsRemoved[world.ResolveSystemState(unmanagedSystem)->m_SystemID] = 1;
            }

            var newManagedUpdateList = new List<ComponentSystemBase>(m_managedSystemsToUpdate.Count);
            var newUnmanagedUpdateList = new UnsafeList<SystemHandle>(m_UnmanagedSystemsToUpdate.Length,Allocator.Persistent);

            //use removed lookup table to determine which systems will be in the new update
            foreach (var managedSystem in m_managedSystemsToUpdate)
            {
                var systemID = managedSystem.CheckedState()->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                {
                    //the new update index will be based on the position in the systems list
                    newListIndices[systemID] = newManagedUpdateList.Count;
                    newManagedUpdateList.Add(managedSystem);
                }
            }

            foreach (var unmanagedSystem in m_UnmanagedSystemsToUpdate)
            {
                var systemID = world.ResolveSystemState(unmanagedSystem)->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                {
                    newListIndices[systemID] = newUnmanagedUpdateList.Length;
                    newUnmanagedUpdateList.Add(unmanagedSystem);
                }
            }

            var newMasterUpdateList = new UnsafeList<UpdateIndex>(newManagedUpdateList.Count + newUnmanagedUpdateList.Length,Allocator.Persistent);

            foreach (var updateIndex in m_MasterUpdateList)
            {
                if (updateIndex.IsManaged)
                {
                    var system = m_managedSystemsToUpdate[updateIndex.Index];
                    var systemID = system.CheckedState()->m_SystemID;
                    //use the two lookup tables to determine if and where the new master update list entries go
                    if (systemIsRemoved[systemID] == 0)
                    {
                        newMasterUpdateList.Add(new UpdateIndex(newListIndices[systemID], true));
                    }
                }
                else
                {
                    var system = m_UnmanagedSystemsToUpdate[updateIndex.Index];
                    var systemID = world.ResolveSystemState(system)->m_SystemID;
                    if (systemIsRemoved[systemID] == 0)
                    {
                        newMasterUpdateList.Add(new UpdateIndex(newListIndices[systemID], false));
                    }
                }
            }

            newListIndices.Dispose();
            systemIsRemoved.Dispose();

            m_managedSystemsToUpdate = newManagedUpdateList;
            m_managedSystemsToRemove.Clear();

            m_UnmanagedSystemsToUpdate.Dispose();
            m_UnmanagedSystemsToUpdate = newUnmanagedUpdateList;
            m_UnmanagedSystemsToRemove.Clear();

            m_MasterUpdateList.Dispose();
            m_MasterUpdateList = newMasterUpdateList;
        }

        private void RecurseUpdate()
        {
            if (!EnableSystemSorting)
            {
                RemoveSystemsFromUnsortedUpdateList();
            }
            else if (m_systemSortDirty)
            {
                GenerateMasterUpdateList();
            }
            m_systemSortDirty = false;

            foreach (var sys in m_managedSystemsToUpdate)
            {
                if (TypeManager.IsSystemAGroup(sys.GetType()))
                {
                    var childGroup = sys as ComponentSystemGroup;
                    childGroup.RecurseUpdate();
                }
            }
        }

        private void GenerateMasterUpdateList()
        {
            RemovePending();

            var groupType = GetType();
            var allElems = new UpdateOrderSystemSorter.SystemElement[m_managedSystemsToUpdate.Count + m_UnmanagedSystemsToUpdate.Length];
            var systemsPerBucket = new int[3];
            for (int i = 0; i < m_managedSystemsToUpdate.Count; ++i)
            {
                var system = m_managedSystemsToUpdate[i];
                var sysType = system.GetType();
                int orderingBucket = ComputeSystemOrdering(sysType, groupType);
                allElems[i] = new UpdateOrderSystemSorter.SystemElement
                {
                    Type = sysType,
                    Index = new UpdateIndex(i, true),
                    OrderingBucket = orderingBucket,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
                systemsPerBucket[orderingBucket]++;
            }
            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sysType = World.Unmanaged.GetTypeOfSystem(m_UnmanagedSystemsToUpdate[i]);
                int orderingBucket = ComputeSystemOrdering(sysType, groupType);
                allElems[m_managedSystemsToUpdate.Count + i] = new UpdateOrderSystemSorter.SystemElement
                {
                    Type = sysType,
                    Index = new UpdateIndex(i, false),
                    OrderingBucket = orderingBucket,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
                systemsPerBucket[orderingBucket]++;
            }

            // Find & validate constraints between systems in the group
            UpdateOrderSystemSorter.FindConstraints(groupType, allElems);

            // Build three lists of systems
            var elemBuckets = new []
            {
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[0]],
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[1]],
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[2]],
            };
            var nextBucketIndex = new int[3];

            for(int i=0; i<allElems.Length; ++i)
            {
                int bucket = allElems[i].OrderingBucket;
                int index = nextBucketIndex[bucket]++;
                elemBuckets[bucket][index] = allElems[i];
            }
            // Perform the sort for each bucket.
            for (int i = 0; i < 3; ++i)
            {
                if (elemBuckets[i].Length > 0)
                {
                    UpdateOrderSystemSorter.Sort(elemBuckets[i]);
                }
            }

            // Because people can freely look at the list of managed systems, we need to put that part of list in order.
            var oldSystems = m_managedSystemsToUpdate;
            m_managedSystemsToUpdate = new List<ComponentSystemBase>(oldSystems.Count);
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elemBuckets[i])
                {
                    var index = e.Index;
                    if (index.IsManaged)
                    {
                        m_managedSystemsToUpdate.Add(oldSystems[index.Index]);
                    }
                }
            }

            // Commit results to master update list
            m_MasterUpdateList.Clear();
            m_MasterUpdateList.SetCapacity(allElems.Length);

            // Append buckets in order, but replace managed indices with incrementing indices
            // into the newly sorted m_systemsToUpdate list
            int managedIndex = 0;
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elemBuckets[i])
                {
                    if (e.Index.IsManaged)
                    {
                        m_MasterUpdateList.Add(new UpdateIndex(managedIndex++, true));
                    }
                    else
                    {
                        m_MasterUpdateList.Add(e.Index);
                    }
                }
            }
        }

        internal static int ComputeSystemOrdering(Type sysType, Type ourType)
        {
            foreach (var uga in TypeManager.GetSystemAttributes(sysType, typeof(UpdateInGroupAttribute)))
            {
                var updateInGroupAttribute = (UpdateInGroupAttribute) uga;

                if (updateInGroupAttribute.GroupType.IsAssignableFrom(ourType))
                {
                    if (updateInGroupAttribute.OrderFirst)
                    {
                        return 0;
                    }

                    if (updateInGroupAttribute.OrderLast)
                    {
                        return 2;
                    }
                }
            }

            return 1;
        }

        /// <summary>
        /// Update the component system's sort order.
        /// </summary>
        public void SortSystems()
        {
            CheckCreated();

            RecurseUpdate();
        }

#if UNITY_DOTSRUNTIME
        public void RecursiveLogToConsole()
        {
            foreach (var sys in m_managedSystemsToUpdate)
            {
                if (sys is ComponentSystemGroup)
                {
                    (sys as ComponentSystemGroup).RecursiveLogToConsole();
                }

                var name = TypeManager.GetSystemName(sys.GetType());
                Debug.Log(name);
            }
        }

#endif

        internal override void OnStopRunningInternal()
        {
            OnStopRunning();

            foreach (var sys in m_managedSystemsToUpdate)
            {
                if (sys == null)
                    continue;

                if (sys.m_StatePtr == null)
                    continue;

                if (!sys.m_StatePtr->PreviouslyEnabled)
                    continue;

                sys.m_StatePtr->PreviouslyEnabled = false;
                sys.OnStopRunningInternal();
            }

            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sys = World.Unmanaged.ResolveSystemState(m_UnmanagedSystemsToUpdate[i]);

                if (sys == null || !sys->PreviouslyEnabled)
                    continue;

                sys->PreviouslyEnabled = false;

                // Optional callback here
            }
        }

        /// <inheritdoc cref="RateManager"/>
        [Obsolete("This property has been renamed to RateManager. (RemovedAfter Entities 1.0) (UnityUpgradable) -> RateManager")]
        public IRateManager FixedRateManager
        {
            get => m_RateManager;
            set => m_RateManager = value;
        }

        /// <summary>
        /// Optional field to control the update rate of this system group.
        /// </summary>
        /// <remarks>
        /// No group allocator is created when setting rate manager.
        /// </remarks>
        public IRateManager RateManager
        {
            get
            {
                return m_RateManager;
            }
            set
            {
                m_RateManager = value;
            }
        }

        /// <summary>
        /// Set optional rate manager for the system group and create group allocator.
        /// </summary>
        /// <remarks>
        /// Create a group allocator for this system group if not created already.
        /// </remarks>
        /// <param name="rateManager">The <see cref="IRateManager"/> to set the allocator to</param>
        public void SetRateManagerCreateAllocator(IRateManager rateManager)
        {
            CreateRateGroupAllocators();
            m_RateManager = rateManager;
        }

        /// <summary>
        /// Retrieve double rewindable allocators of this rate system group.
        /// </summary>
        public DoubleRewindableAllocators* RateGroupAllocators => m_RateGroupAllocators;

        internal DoubleRewindableAllocators* CurrentGroupAllocators { get; set; } = null;

        /// <summary>
        /// Updates the group's systems
        /// </summary>
        protected override void OnUpdate()
        {
            CheckCreated();

            if (RateManager == null)
            {
                UpdateAllSystems();
            }
            else
            {
                while (RateManager.ShouldGroupUpdate(this))
                {
                    UpdateAllSystems();
                }
            }
        }

        void UpdateAllSystems()
        {
            if (m_systemSortDirty)
                SortSystems();

            // Update all unmanaged and managed systems together, in the correct sort order.
            // The master update list contains indices for both managed and unmanaged systems.
            // Negative values indicate an index in the unmanaged system list.
            // Positive values indicate an index in the managed system list.
            var world = World.Unmanaged;
            ref var worldImpl = ref world.GetImpl();

            // Cache the update list length before updating; any new systems added mid-loop will change the length and
            // should not be processed until the subsequent group update, to give SortSystems() a chance to run.
            int updateListLength = m_MasterUpdateList.Length;
            for (int i = 0; i < updateListLength; ++i)
            {
                try
                {
                    var index = m_MasterUpdateList[i];

                    if (!index.IsManaged)
                    {
                        // Update unmanaged (burstable) code.
                        var handle = m_UnmanagedSystemsToUpdate[index.Index];
                        worldImpl.UpdateSystem(handle);
                    }
                    else
                    {
                        // Update managed code.
                        var sys = m_managedSystemsToUpdate[index.Index];
                        sys.Update();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
#if UNITY_DOTSRUNTIME
                    // When in a DOTS Runtime build, throw this upstream -- continuing after silently eating an exception
                    // is not what you'll want, except maybe once we have LiveLink.  If you're looking at this code
                    // because your LiveLink dots runtime build is exiting when you don't want it to, feel free
                    // to remove this block, or guard it with something to indicate the player is not for live link.
                    throw;
#endif
                }

                if (World.QuitUpdate)
                    break;
            }
        }

        /// <summary>
        /// Create a double rewindable allocators for rate sytem groups.
        /// </summary>
        internal void CreateRateGroupAllocators()
        {
            if (RateGroupAllocatorsCreated == 0)
            {
                m_RateGroupAllocators = Memory.Unmanaged.Allocate<DoubleRewindableAllocators>(Allocator.Persistent);
                m_RateGroupAllocators->Initialize(Allocator.Persistent, InitialSystemGroupAllocatorBlockSizeInBytes);
                RateGroupAllocatorsCreated = 1;
            }
        }

        /// <summary>
        /// Destroy the double rewindable allocators for rate sytem groups.
        /// </summary>
        void DestroyRateGroupAllocators()
        {
            m_RateGroupAllocators->Dispose();
            Memory.Unmanaged.Free(m_RateGroupAllocators, Allocator.Persistent);
            m_RateGroupAllocators = null;
            RateGroupAllocatorsCreated = 0;
        }
    }

    /// <summary>
    /// This class only contains deprecated extension methods, and should not be used.
    /// </summary>
    [Obsolete("This class will soon be empty and will be removed. (RemovedAfter Entities 1.0)")]
    public static class ComponentSystemGroupExtensions
    {
        /// <inheritdoc cref="ComponentSystemGroup.RemoveSystemFromUpdateList(ComponentSystemBase)"/>
        [Obsolete("RemoveUnmanagedSystemFromUpdateList has been deprecated. Please use RemoveSystemFromUpdateList. (RemovedAfter Entities 1.0)")]
        public static void RemoveUnmanagedSystemFromUpdateList(this ComponentSystemGroup self, SystemHandle sysHandle)
        {
            self.RemoveSystemFromUpdateList(sysHandle);
        }

        /// <inheritdoc cref="ComponentSystemGroup.AddSystemToUpdateList(ComponentSystemBase)"/>
        [Obsolete("AddUnmanagedSystemToUpdateList has been deprecated. Please use AddSystemToUpdateList. (RemovedAfter Entities 1.0)")]
        public static void AddUnmanagedSystemToUpdateList(this ComponentSystemGroup self, SystemHandle sysHandle)
        {
            self.AddSystemToUpdateList(sysHandle);
        }
    }
}
