using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.Entities
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    // A bundle of component safety handles implicitly referenced by an EntityQuery. This allows a query to
    // register its component dependencies when the query is used by a job.
    [NativeContainer]
    internal struct EntityQuerySafetyHandles
    {
        // All IJobEntityBatchWithIndex jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
        // jobs without any other safety handles are still running (haven't been synced).
        internal AtomicSafetyHandle m_Safety0;
        // TODO(DOTS-6573): Enable this path in DOTSRT once it supports AtomicSafetyHandle.SetExclusiveWeak()
#if !UNITY_DOTSRUNTIME
        // Enableable components from query used to schedule job. To add more handles here, you must also increase
        // EntityQueryManager.MAX_ENABLEABLE_COMPONENTS_PER_QUERY.
        internal AtomicSafetyHandle m_SafetyEnableable1;
        internal AtomicSafetyHandle m_SafetyEnableable2;
        internal AtomicSafetyHandle m_SafetyEnableable3;
        internal AtomicSafetyHandle m_SafetyEnableable4;
        internal AtomicSafetyHandle m_SafetyEnableable5;
        internal AtomicSafetyHandle m_SafetyEnableable6;
        internal AtomicSafetyHandle m_SafetyEnableable7;
        internal AtomicSafetyHandle m_SafetyEnableable8;
#endif
        internal int m_SafetyReadOnlyCount;
        internal int m_SafetyReadWriteCount;

        internal unsafe EntityQuerySafetyHandles(EntityQueryImpl* queryImpl)
        {
            this = default; // workaround for CS0171 error (all fields must be fully assigned before control is returned)
            var queryData = queryImpl->_QueryData;
            m_Safety0 = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle();
#if UNITY_DOTSRUNTIME
            // TODO(DOTS-6573): DOTSRT can use the main code path once it supports AtomicSafetyHandle.SetExclusiveWeak()
            m_SafetyReadOnlyCount = 1; // for EntityManager handle
            m_SafetyReadWriteCount = 0;
#else
            m_SafetyReadOnlyCount = 1 + queryData->EnableableComponentTypeIndexCount; // +1 for EntityManager handle
            m_SafetyReadWriteCount = 0;
            fixed (AtomicSafetyHandle* pEnableableHandles = &m_SafetyEnableable1)
            {
                for (int i = 0; i < EntityQueryManager.MAX_ENABLEABLE_COMPONENTS_PER_QUERY; ++i)
                {
                    if (i < queryData->EnableableComponentTypeIndexCount)
                    {
                        pEnableableHandles[i] =
                            queryImpl->SafetyHandles->GetSafetyHandle(queryData->EnableableComponentTypeIndices[i],
                                true);
                        AtomicSafetyHandle.SetExclusiveWeak(ref pEnableableHandles[i], true);
                    }
                }
            }
#endif
        }
    }
#endif

    [GenerateTestsForBurstCompatibility]
    internal unsafe struct EntityQueryManager
    {
        private ComponentDependencyManager*    m_DependencyManager;
        private BlockAllocator                 m_EntityQueryDataChunkAllocator;
        private UnsafePtrList<EntityQueryData> m_EntityQueryDatas;
        internal const int MAX_ENABLEABLE_COMPONENTS_PER_QUERY = 8;

        private UntypedUnsafeParallelHashMap           m_EntityQueryDataCacheUntyped;
        internal int                           m_EntityQueryMasksAllocated;

        public static void Create(EntityQueryManager* queryManager, ComponentDependencyManager* dependencyManager)
        {
            queryManager->m_DependencyManager = dependencyManager;
            queryManager->m_EntityQueryDataChunkAllocator = new BlockAllocator(AllocatorManager.Persistent, 16 * 1024 * 1024); // 16MB should be enough
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeMultiHashMap<int, int>>(ref queryManager->m_EntityQueryDataCacheUntyped);
            entityQueryCache = new UnsafeMultiHashMap<int, int>(1024, Allocator.Persistent);
            queryManager->m_EntityQueryDatas = new UnsafePtrList<EntityQueryData>(0, Allocator.Persistent);
            queryManager->m_EntityQueryMasksAllocated = 0;
        }

        public static void Destroy(EntityQueryManager* manager)
        {
            manager->Dispose();
        }

        void Dispose()
        {
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeMultiHashMap<int, int>>(ref m_EntityQueryDataCacheUntyped);
            entityQueryCache.Dispose();
            for (var g = 0; g < m_EntityQueryDatas.Length; ++g)
            {
                m_EntityQueryDatas.Ptr[g]->Dispose();
            }
            m_EntityQueryDatas.Dispose();
            //@TODO: Need to wait for all job handles to be completed..
            m_EntityQueryDataChunkAllocator.Dispose();
        }

        ArchetypeQuery* CreateArchetypeQueries(ref UnsafeScratchAllocator unsafeScratchAllocator, ComponentType* requiredTypes, int count)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp);

            for (int i = 0; i != count; i++)
            {
                if (requiredTypes[i].AccessModeType == ComponentType.AccessMode.Exclude)
                {
                    var noneType = ComponentType.ReadOnly(requiredTypes[i].TypeIndex);
                    builder.WithNone(&noneType, 1);
                }
                else
                {
                    builder.WithAll(&requiredTypes[i], 1);
                }
            }

            builder.FinalizeQueryInternal();

            var result = CreateArchetypeQueries(ref unsafeScratchAllocator, builder);

            builder.Dispose();

            return result;
        }

        internal void ConstructTypeArray(ref UnsafeScratchAllocator unsafeScratchAllocator, UnsafeList<ComponentType> types, out TypeIndex* outTypes, out byte* outAccessModes, out int outLength)
        {
            if (types.Length == 0)
            {
                outTypes = null;
                outAccessModes = null;
                outLength = 0;
            }
            else
            {
                outLength = types.Length;
                outTypes = (TypeIndex*)unsafeScratchAllocator.Allocate<TypeIndex>(types.Length);
                outAccessModes = (byte*)unsafeScratchAllocator.Allocate<byte>(types.Length);

                var sortedTypes = stackalloc ComponentType[types.Length];
                for (var i = 0; i < types.Length; ++i)
                    SortingUtilities.InsertSorted(sortedTypes, i, types[i]);

                for (int i = 0; i != types.Length; i++)
                {
                    outTypes[i] = sortedTypes[i].TypeIndex;
                    outAccessModes[i] = (byte)sortedTypes[i].AccessModeType;
                }
            }
        }

        void IncludeDependentWriteGroups(ComponentType type, ref UnsafeList<ComponentType> explicitList)
        {
            if (type.AccessModeType != ComponentType.AccessMode.ReadOnly)
                return;

            var typeInfo = TypeManager.GetTypeInfo(type.TypeIndex);
            var writeGroups = TypeManager.GetWriteGroups(typeInfo);
            var writeGroupCount = typeInfo.WriteGroupCount;
            for (int i = 0; i < writeGroupCount; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroups, i);
                if (explicitList.Contains(excludedComponentType))
                    continue;

                explicitList.Add(excludedComponentType);
                IncludeDependentWriteGroups(excludedComponentType, ref explicitList);
            }
        }

        private static ComponentType GetWriteGroupReadOnlyComponentType(TypeIndex* writeGroupTypes, int i)
        {
            // Need to get "Clean" TypeIndex from Type. Since TypeInfo.TypeIndex is not actually the index of the
            // type. (It includes other flags.) What is stored in WriteGroups is the actual index of the type.
            ref readonly var excludedType = ref TypeManager.GetTypeInfo(writeGroupTypes[i]);
            var excludedComponentType = ComponentType.ReadOnly(excludedType.TypeIndex);
            return excludedComponentType;
        }

        void ExcludeWriteGroups(ComponentType type, ref UnsafeList<ComponentType> noneList, UnsafeList<ComponentType> explicitList)
        {
            if (type.AccessModeType == ComponentType.AccessMode.ReadOnly)
                return;

            var typeInfo = TypeManager.GetTypeInfo(type.TypeIndex);
            var writeGroups = TypeManager.GetWriteGroups(typeInfo);
            var writeGroupCount = typeInfo.WriteGroupCount;
            for (int i = 0; i < writeGroupCount; i++)
            {
                var excludedComponentType = GetWriteGroupReadOnlyComponentType(writeGroups, i);
                if (noneList.Contains(excludedComponentType))
                    continue;
                if (explicitList.Contains(excludedComponentType))
                    continue;

                noneList.Add(excludedComponentType);
            }
        }

        // Plan to unmanaged EntityQueryManager
        // [X] Introduce EntityQueryBuilder and test it
        // [X] Change internals to take an EntityQueryBuilder as an in parameter
        // [X] Validate queryData in CreateArchetypeQueries
        // [x] Everyone calling this needs to convert managed stuff into unmanaged EQDBuilder
        // [x] Public overloads of APIs to offer an EQDBuilder option
        // [ ] Deprecate EntityQueryDesc
        ArchetypeQuery* CreateArchetypeQueries(ref UnsafeScratchAllocator unsafeScratchAllocator, in EntityQueryBuilder queryBuilder)
        {
            var types = queryBuilder._builderDataPtr->_typeData;
            var queryData = queryBuilder._builderDataPtr->_indexData;
            var outQuery = (ArchetypeQuery*)unsafeScratchAllocator.Allocate(sizeof(ArchetypeQuery) * queryData.Length, UnsafeUtility.AlignOf<ArchetypeQuery>());

            // we need to build out new lists of component types to mutate them for WriteGroups
            var allTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var anyTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
            var noneTypes = new UnsafeList<ComponentType>(32, Allocator.Temp);
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var entityTypeIndex = TypeManager.GetTypeIndex<Entity>();
#endif

            for (int q = 0; q != queryData.Length; q++)
            {
                {
                    var typesAll = queryData[q].All;
                    for (int i = typesAll.Index; i < typesAll.Index + typesAll.Count; i++)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        if (types[i].TypeIndex == entityTypeIndex)
                        {
                            throw new ArgumentException("Entity is not allowed in list of component types for EntityQuery");
                        }
#endif
                        allTypes.Add(types[i]);
                    }
                }

                {
                    var typesAny = queryData[q].Any;
                    for (int i = typesAny.Index; i < typesAny.Index + typesAny.Count; i++)
                    {
                        anyTypes.Add(types[i]);
                    }
                }

                {
                    var typesNone = queryData[q].None;
                    for (int i = typesNone.Index; i < typesNone.Index + typesNone.Count; i++)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        var type = types[i];
                        // Can not use Assert.AreEqual here because it uses the (object, object) overload which
                        // boxes the enums being compared, and that can not be burst compiled.
                        Assert.IsTrue(ComponentType.AccessMode.ReadOnly == type.AccessModeType, "EntityQueryBuilder.None must convert ComponentType.AccessMode to ReadOnly");
#endif
                        noneTypes.Add(types[i]);
                    }
                }

                // Validate the queryBuilder has components declared in a consistent way
                EntityQueryBuilder.Validate(allTypes, anyTypes, noneTypes);

                var isFilterWriteGroup = (queryData[q].Options & EntityQueryOptions.FilterWriteGroup) != 0;
                if (isFilterWriteGroup)
                {
                    // Each ReadOnly<type> in any or all
                    //   if has WriteGroup types,
                    //   - Recursively add to any (if not explictly mentioned)

                    var explicitList = new UnsafeList<ComponentType>(allTypes.Length + anyTypes.Length + noneTypes.Length + 16, Allocator.Temp);
                    explicitList.AddRange(allTypes);
                    explicitList.AddRange(anyTypes);
                    explicitList.AddRange(noneTypes);

                    for (int i = 0; i < anyTypes.Length; i++)
                        IncludeDependentWriteGroups(anyTypes[i], ref explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        IncludeDependentWriteGroups(allTypes[i], ref explicitList);

                    // Each ReadWrite<type> in any or all
                    //   if has WriteGroup types,
                    //     Add to none (if not exist in any or all or none)

                    for (int i = 0; i < anyTypes.Length; i++)
                        ExcludeWriteGroups(anyTypes[i], ref noneTypes, explicitList);
                    for (int i = 0; i < allTypes.Length; i++)
                        ExcludeWriteGroups(allTypes[i], ref noneTypes, explicitList);
                    explicitList.Dispose();
                }

                ConstructTypeArray(ref unsafeScratchAllocator, noneTypes, out outQuery[q].None,
                    out outQuery[q].NoneAccessMode, out outQuery[q].NoneCount);

                ConstructTypeArray(ref unsafeScratchAllocator, allTypes, out outQuery[q].All,
                    out outQuery[q].AllAccessMode, out outQuery[q].AllCount);

                ConstructTypeArray(ref unsafeScratchAllocator, anyTypes, out outQuery[q].Any,
                    out outQuery[q].AnyAccessMode, out outQuery[q].AnyCount);

                allTypes.Clear();
                anyTypes.Clear();
                noneTypes.Clear();
                outQuery[q].Options = queryData[q].Options;
            }

            allTypes.Dispose();
            anyTypes.Dispose();
            noneTypes.Dispose();
            return outQuery;
        }

        internal static bool CompareQueryArray(in EntityQueryBuilder builder, EntityQueryBuilder.ComponentIndexArray arr, TypeIndex* typeArray, byte* accessModeArray, int typeArrayCount)
        {
            int arrCount = arr.Count;
            if (typeArrayCount != arrCount)
                return false;

            var sortedTypes = stackalloc ComponentType[arrCount];
            var types = builder._builderDataPtr->_typeData;
            for (var i = 0; i < arrCount; ++i)
            {
                SortingUtilities.InsertSorted(sortedTypes, i, types[arr.Index + i]);
            }

            for (var i = 0; i < arrCount; ++i)
            {
                if (typeArray[i] != sortedTypes[i].TypeIndex || accessModeArray[i] != (byte)sortedTypes[i].AccessModeType)
                    return false;
            }

            return true;
        }


        public static bool CompareQuery(in EntityQueryBuilder queryBuilder, EntityQueryData* queryData)
        {
            queryBuilder.FinalizeQueryInternal();
            var indexData = queryBuilder._builderDataPtr->_indexData;
            int count = indexData.Length;
            if (queryData->ArchetypeQueryCount != count)
                return false;

            for (int i = 0; i != count; i++)
            {
                ref var archetypeQuery = ref queryData->ArchetypeQueries[i];
                var q = indexData[i];

                if (!CompareQueryArray(queryBuilder, q.All, archetypeQuery.All, archetypeQuery.AllAccessMode, archetypeQuery.AllCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.None, archetypeQuery.None, archetypeQuery.NoneAccessMode, archetypeQuery.NoneCount))
                    return false;
                if (!CompareQueryArray(queryBuilder, q.Any, archetypeQuery.Any, archetypeQuery.AnyAccessMode, archetypeQuery.AnyCount))
                    return false;
                if (q.Options != archetypeQuery.Options)
                    return false;
            }

            return true;
        }

        private int IntersectSortedComponentIndexArrays(TypeIndex* arrayA, byte* accessArrayA, int arrayACount, TypeIndex* arrayB, byte* accessArrayB, int arrayBCount, TypeIndex* outArray, byte* outAccessArray)
        {
            var intersectionCount = 0;

            var i = 0;
            var j = 0;
            while (i < arrayACount && j < arrayBCount)
            {
                if (arrayA[i] < arrayB[j])
                    i++;
                else if (arrayB[j] < arrayA[i])
                    j++;
                else
                {
                    outArray[intersectionCount] = arrayB[j];
                    outAccessArray[intersectionCount] = accessArrayB[j];
                    intersectionCount++;
                    i++;
                    j++;
                }
            }

            return intersectionCount;
        }

        // Calculates the intersection of "All" queriesDesc
        private ComponentType* CalculateRequiredComponentsFromQuery(ref UnsafeScratchAllocator allocator, ArchetypeQuery* queries, int queryCount, out int outRequiredComponentsCount)
        {
            var maxIntersectionCount = 0;
            for (int queryIndex = 0; queryIndex < queryCount; ++queryIndex)
                maxIntersectionCount = math.max(maxIntersectionCount, queries[queryIndex].AllCount);

            // allocate index array and r/w permissions array
            var intersection = (TypeIndex*)allocator.Allocate<TypeIndex>(maxIntersectionCount);
            UnsafeUtility.MemCpy(intersection, queries[0].All, sizeof(TypeIndex) * queries[0].AllCount);

            var access = (byte*)allocator.Allocate<byte>(maxIntersectionCount);
            UnsafeUtility.MemCpy(access, queries[0].AllAccessMode, sizeof(byte) * queries[0].AllCount);

            var intersectionCount = maxIntersectionCount;
            for (int i = 1; i < queryCount; ++i)
            {
                intersectionCount = IntersectSortedComponentIndexArrays(intersection, access, intersectionCount,
                    queries[i].All, queries[i].AllAccessMode, queries[i].AllCount, intersection, access);
            }

            var outRequiredComponents = (ComponentType*)allocator.Allocate<ComponentType>(intersectionCount + 1);
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            for (int i = 0; i < intersectionCount; ++i)
            {
                outRequiredComponents[i + 1] = ComponentType.FromTypeIndex(intersection[i]);
                outRequiredComponents[i + 1].AccessModeType = (ComponentType.AccessMode)access[i];
            }

            outRequiredComponentsCount = intersectionCount + 1;
            return outRequiredComponents;
        }

        [ExcludeFromBurstCompatTesting("Takes managed array")]
        internal static void ConvertToEntityQueryBuilder(ref EntityQueryBuilder builder, EntityQueryDesc[] queryDesc)
        {
            for (int q = 0; q != queryDesc.Length; q++)
            {
                ref var desc = ref queryDesc[q];
                fixed (ComponentType* allTypes = desc.All)
                {
                    builder.WithAll(allTypes, desc.All.Length);
                }
                fixed (ComponentType* anyTypes = desc.Any)
                {
                    builder.WithAny(anyTypes, desc.Any.Length);
                }
                fixed (ComponentType* noneTypes = desc.None)
                {
                    builder.WithNone(noneTypes, desc.None.Length);
                }

                builder.WithOptions(desc.Options);
                builder.FinalizeQueryInternal();
            }
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, EntityQueryBuilder query)
        {
            query.FinalizeQueryInternal();

            var buffer = stackalloc byte[1024];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 1024);
            var archetypeQuery = CreateArchetypeQueries(ref scratchAllocator, query);

            var indexData = query._builderDataPtr->_indexData;
            var outRequiredComponents = CalculateRequiredComponentsFromQuery(ref scratchAllocator, archetypeQuery, indexData.Length, out var outRequiredComponentsCount);
            return CreateEntityQuery(access, archetypeQuery, indexData.Length, outRequiredComponents, outRequiredComponentsCount);
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access, ComponentType* inRequiredComponents, int inRequiredComponentsCount)
        {
            var buffer = stackalloc byte[1024];
            var scratchAllocator = new UnsafeScratchAllocator(buffer, 1024);
            var archetypeQueries = CreateArchetypeQueries(ref scratchAllocator, inRequiredComponents, inRequiredComponentsCount);
            var outRequiredComponents = (ComponentType*)scratchAllocator.Allocate<ComponentType>(inRequiredComponentsCount + 1);
            outRequiredComponents[0] = ComponentType.ReadWrite<Entity>();
            for (int i = 0; i != inRequiredComponentsCount; i++)
                SortingUtilities.InsertSorted(outRequiredComponents + 1, i, inRequiredComponents[i]);
            var outRequiredComponentsCount = inRequiredComponentsCount + 1;
            return CreateEntityQuery(access, archetypeQueries, 1, outRequiredComponents, outRequiredComponentsCount);
        }

        bool Matches(EntityQueryData* grp, ArchetypeQuery* archetypeQueries, int archetypeQueryCount,
            ComponentType* requiredComponents, int requiredComponentsCount)
        {
            if (requiredComponentsCount != grp->RequiredComponentsCount)
                return false;
            if (archetypeQueryCount != grp->ArchetypeQueryCount)
                return false;
            if (requiredComponentsCount > 0 && UnsafeUtility.MemCmp(requiredComponents, grp->RequiredComponents, sizeof(ComponentType) * requiredComponentsCount) != 0)
                return false;
            for (var i = 0; i < archetypeQueryCount; ++i)
                if (!archetypeQueries[i].Equals(grp->ArchetypeQueries[i]))
                    return false;
            return true;
        }

        void* ChunkAllocate<T>(int count = 1, void *source = null) where T : struct
        {
            var bytes = count * UnsafeUtility.SizeOf<T>();
            if (bytes == 0)
                return null;
            var pointer = m_EntityQueryDataChunkAllocator.Allocate(bytes, UnsafeUtility.AlignOf<T>());
            if (source != null)
                UnsafeUtility.MemCpy(pointer, source, bytes);
            return pointer;
        }

        public EntityQuery CreateEntityQuery(EntityDataAccess* access,
            ArchetypeQuery* archetypeQueries,
            int archetypeQueryCount,
            ComponentType* requiredComponents,
            int requiredComponentCount)
        {
            //@TODO: Validate that required types is subset of archetype filters all...

            int hash = (int)math.hash(requiredComponents, requiredComponentCount * sizeof(ComponentType));
            for (var i = 0; i < archetypeQueryCount; ++i)
                hash = hash * 397 ^ archetypeQueries[i].GetHashCode();
            EntityQueryData* queryData = null;
            ref var entityQueryCache = ref UnsafeUtility.As<UntypedUnsafeParallelHashMap, UnsafeMultiHashMap<int, int>>(ref m_EntityQueryDataCacheUntyped);

            if (entityQueryCache.TryGetFirstValue(hash, out var entityQueryIndex, out var iterator))
            {
                do
                {
                    var possibleMatch = m_EntityQueryDatas.Ptr[entityQueryIndex];
                    if (Matches(possibleMatch, archetypeQueries, archetypeQueryCount, requiredComponents, requiredComponentCount))
                    {
                        queryData = possibleMatch;
                        break;
                    }
                }
                while (entityQueryCache.TryGetNextValue(out entityQueryIndex, ref iterator));
            }

            if (queryData == null)
            {
                // Validate input archetype queries
                bool queryIgnoresEnabledBits =
                    (archetypeQueries[0].Options & EntityQueryOptions.IgnoreComponentEnabledState) != 0;
                for (int iAQ = 1; iAQ < archetypeQueryCount; ++iAQ)
                {
                    bool hasIgnoreEnabledBitsFlag = (archetypeQueries[iAQ].Options & EntityQueryOptions.IgnoreComponentEnabledState) != 0;
                    if (hasIgnoreEnabledBitsFlag != queryIgnoresEnabledBits)
                        throw new ArgumentException(
                            $"All EntityQueryOptions passed to CreateEntityQuery() must have the same value for the IgnoreComponentEnabledState flag");
                }

                // count & identify enableable component types
                int totalComponentCount = 0;
                for (int iAQ = 0; iAQ < archetypeQueryCount; ++iAQ)
                {
                    totalComponentCount += archetypeQueries[iAQ].AllCount + archetypeQueries[iAQ].NoneCount +
                                           archetypeQueries[iAQ].NoneCount;
                }

                var allEnableableTypeIndices = new NativeList<TypeIndex>(totalComponentCount, Allocator.Temp);
                for (int iAQ = 0; iAQ < archetypeQueryCount; ++iAQ)
                {
                    ref ArchetypeQuery aq = ref archetypeQueries[iAQ];
                    for (int i = 0; i < aq.AllCount; ++i)
                    {
                        if (aq.All[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.All[i]);
                    }

                    for (int i = 0; i < aq.NoneCount; ++i)
                    {
                        if (aq.None[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.None[i]);
                    }

                    for (int i = 0; i < aq.AnyCount; ++i)
                    {
                        if (aq.Any[i].IsEnableable)
                            allEnableableTypeIndices.Add(aq.Any[i]);
                    }
                }
                // eliminate duplicate type indices
                if (allEnableableTypeIndices.Length > 0)
                {
                    allEnableableTypeIndices.Sort();
                    int lastUniqueIndex = 0;
                    for (int i = 1; i < allEnableableTypeIndices.Length; ++i)
                    {
                        if (allEnableableTypeIndices[i] != allEnableableTypeIndices[lastUniqueIndex])
                        {
                            allEnableableTypeIndices[++lastUniqueIndex] = allEnableableTypeIndices[i];
                        }
                    }
                    allEnableableTypeIndices.Length = lastUniqueIndex + 1;
                    // This limit matches the number of safety handles for enableable types we store in job structs.
                    if (allEnableableTypeIndices.Length > MAX_ENABLEABLE_COMPONENTS_PER_QUERY)
                        throw new ArgumentException(
                            $"EntityQuery objects may not reference more than {MAX_ENABLEABLE_COMPONENTS_PER_QUERY} enableable components");
                }
                // Allocate and populate query data
                queryData = (EntityQueryData*)ChunkAllocate<EntityQueryData>();
                queryData->RequiredComponentsCount = requiredComponentCount;
                queryData->RequiredComponents = (ComponentType*)ChunkAllocate<ComponentType>(requiredComponentCount, requiredComponents);
                queryData->EnableableComponentTypeIndexCount = queryIgnoresEnabledBits ? 0 : allEnableableTypeIndices.Length;
                queryData->EnableableComponentTypeIndices = (TypeIndex*)ChunkAllocate<TypeIndex>(allEnableableTypeIndices.Length, allEnableableTypeIndices.GetUnsafeReadOnlyPtr());
                queryData->DoesQueryRequireBatching = (allEnableableTypeIndices.Length > 0 && !queryIgnoresEnabledBits) ? (byte)1 : (byte)0;

                InitializeReaderWriter(queryData, requiredComponents, requiredComponentCount);
                queryData->ArchetypeQueryCount = archetypeQueryCount;
                queryData->ArchetypeQueries = (ArchetypeQuery*)ChunkAllocate<ArchetypeQuery>(archetypeQueryCount, archetypeQueries);
                for (var i = 0; i < archetypeQueryCount; ++i)
                {
                    queryData->ArchetypeQueries[i].All = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].AllCount, archetypeQueries[i].All);
                    queryData->ArchetypeQueries[i].Any = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].AnyCount, archetypeQueries[i].Any);
                    queryData->ArchetypeQueries[i].None = (TypeIndex*)ChunkAllocate<TypeIndex>(queryData->ArchetypeQueries[i].NoneCount, archetypeQueries[i].None);
                    queryData->ArchetypeQueries[i].AllAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].AllCount, archetypeQueries[i].AllAccessMode);
                    queryData->ArchetypeQueries[i].AnyAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].AnyCount, archetypeQueries[i].AnyAccessMode);
                    queryData->ArchetypeQueries[i].NoneAccessMode = (byte*)ChunkAllocate<byte>(queryData->ArchetypeQueries[i].NoneCount, archetypeQueries[i].NoneAccessMode);
                }

                var ecs = access->EntityComponentStore;

                queryData->MatchingArchetypes = new UnsafeMatchingArchetypePtrList(access->EntityComponentStore);
                queryData->MatchingChunkCache = new UnsafeCachedChunkList(access->EntityComponentStore);

                queryData->EntityQueryMask = new EntityQueryMask();

                for (var i = 0; i < ecs->m_Archetypes.Length; ++i)
                {
                    var archetype = ecs->m_Archetypes.Ptr[i];
                    AddArchetypeIfMatching(archetype, queryData);
                }

                entityQueryCache.Add(hash, m_EntityQueryDatas.Length);
                m_EntityQueryDatas.Add(queryData);
                queryData->MatchingChunkCache.Invalidate();
            }

            return EntityQuery.Construct(queryData, access);
        }

        void InitializeReaderWriter(EntityQueryData* grp, ComponentType* requiredTypes, int requiredCount)
        {
            Assert.IsTrue(requiredCount > 0);
            Assert.IsTrue(requiredTypes[0] == ComponentType.ReadWrite<Entity>());

            grp->ReaderTypesCount = 0;
            grp->WriterTypesCount = 0;

            for (var i = 1; i != requiredCount; i++)
            {
                if (requiredTypes[i].IsZeroSized && !requiredTypes[i].IsEnableable)
                    continue;

                switch (requiredTypes[i].AccessModeType)
                {
                    case ComponentType.AccessMode.ReadOnly:
                        grp->ReaderTypesCount++;
                        break;
                    default:
                        grp->WriterTypesCount++;
                        break;
                }
            }

            grp->ReaderTypes = (TypeIndex*)m_EntityQueryDataChunkAllocator.Allocate(sizeof(TypeIndex) * grp->ReaderTypesCount, 4);
            grp->WriterTypes = (TypeIndex*)m_EntityQueryDataChunkAllocator.Allocate(sizeof(TypeIndex) * grp->WriterTypesCount, 4);

            var curReader = 0;
            var curWriter = 0;
            for (var i = 1; i != requiredCount; i++)
            {
                if (requiredTypes[i].IsZeroSized && !requiredTypes[i].IsEnableable)
                    continue;

                switch (requiredTypes[i].AccessModeType)
                {
                    case ComponentType.AccessMode.ReadOnly:
                        grp->ReaderTypes[curReader++] = requiredTypes[i].TypeIndex;
                        break;
                    default:
                        grp->WriterTypes[curWriter++] = requiredTypes[i].TypeIndex;
                        break;
                }
            }
        }

        public void AddAdditionalArchetypes(UnsafePtrList<Archetype> archetypeList)
        {
            for (int i = 0; i < archetypeList.Length; i++)
            {
                for (var g = 0; g < m_EntityQueryDatas.Length; ++g)
                {
                    var grp = m_EntityQueryDatas.Ptr[g];
                    AddArchetypeIfMatching(archetypeList.Ptr[i], grp);
                }
            }
        }

        void AddArchetypeIfMatching(Archetype* archetype, EntityQueryData* query)
        {
            if (!IsMatchingArchetype(archetype, query))
                return;

            var match = MatchingArchetype.Create(ref m_EntityQueryDataChunkAllocator, archetype, query);
            match->Archetype = archetype;
            var typeIndexInArchetypeArray = match->IndexInArchetype;

            match->Archetype->SetMask(query->EntityQueryMask);

            query->MatchingArchetypes.Add(match);

            // Add back pointer from archetype to query data
            archetype->MatchingQueryData.Add((IntPtr)query);

            var typeComponentIndex = 0;
            for (var component = 0; component < query->RequiredComponentsCount; ++component)
            {
                if (query->RequiredComponents[component].AccessModeType != ComponentType.AccessMode.Exclude)
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, query->RequiredComponents[component].TypeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    typeIndexInArchetypeArray[component] = typeComponentIndex;
                }
                else
                {
                    typeIndexInArchetypeArray[component] = -1;
                }
            }

            // TODO(DOTS-5638): this assumes that query only contains one ArchetypeQuery
            var enableableAllCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->AllCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->All[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    typeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    Assert.AreNotEqual(-1, typeComponentIndex);
                    match->EnableableIndexInArchetype_All[enableableAllCount++] = typeComponentIndex;
                }
            }

            var enableableNoneCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->NoneCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->None[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    if (currentTypeComponentIndex != -1) // we skip storing "None" component types for matching archetypes that do not contain the "None" type (there are no bits to check)
                    {
                        match->EnableableIndexInArchetype_None[enableableNoneCount++] = currentTypeComponentIndex;
                        typeComponentIndex = currentTypeComponentIndex;
                    }
                }
            }

            var enableableAnyCount = 0;
            typeComponentIndex = 0;
            for (var i = 0; i < query->ArchetypeQueries->AnyCount; ++i)
            {
                var typeIndex = query->ArchetypeQueries->Any[i];
                if (TypeManager.IsEnableable(typeIndex))
                {
                    var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                    // The archetype may not contain all the Any types (by definition; this is the whole point of Any).
                    // Skip storing the missing types.
                    if (currentTypeComponentIndex != -1)
                    {
                        match->EnableableIndexInArchetype_Any[enableableAnyCount++] = currentTypeComponentIndex;
                        typeComponentIndex = currentTypeComponentIndex;
                    }
                }
            }
        }

        //@TODO: All this could be much faster by having all ComponentType pre-sorted to perform a single search loop instead two nested for loops...
        static bool IsMatchingArchetype(Archetype* archetype, EntityQueryData* query)
        {
            for (int i = 0; i != query->ArchetypeQueryCount; i++)
            {
                if (IsMatchingArchetype(archetype, query->ArchetypeQueries + i))
                    return true;
            }

            return false;
        }

        static bool IsMatchingArchetype(Archetype* archetype, ArchetypeQuery* query)
        {
            if (!TestMatchingArchetypeAll(archetype, query->All, query->AllCount, query->Options))
                return false;
            if (!TestMatchingArchetypeNone(archetype, query->None, query->NoneCount))
                return false;
            if (!TestMatchingArchetypeAny(archetype, query->Any, query->AnyCount))
                return false;

            return true;
        }

        static bool TestMatchingArchetypeAny(Archetype* archetype, TypeIndex* anyTypes, int anyCount)
        {
            if (anyCount == 0) return true;

            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < anyCount; j++)
                {
                    var anyTypeIndex = anyTypes[j];
                    if (componentTypeIndex == anyTypeIndex)
                        return true;
                }
            }

            return false;
        }

        static bool TestMatchingArchetypeNone(Archetype* archetype, TypeIndex* noneTypes, int noneCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < noneCount; j++)
                {
                    var noneTypeIndex = noneTypes[j];
                    if (componentTypeIndex == noneTypeIndex && !TypeManager.IsEnableable(componentTypeIndex)) return false;
                }
            }

            return true;
        }

        static bool TestMatchingArchetypeAll(Archetype* archetype, TypeIndex* allTypes, int allCount, EntityQueryOptions options)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            var foundCount = 0;
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex = TypeManager.GetTypeIndex<Prefab>();
            var systemInstanceTypeIndex = TypeManager.GetTypeIndex<SystemInstance>();
            var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var includeInactive = (options & EntityQueryOptions.IncludeDisabledEntities) != 0;
            var includePrefab = (options & EntityQueryOptions.IncludePrefab) != 0;
            var includeSystems = (options & EntityQueryOptions.IncludeSystems) != 0;
            var includeChunkHeader = false;

            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < allCount; j++)
                {
                    var allTypeIndex = allTypes[j];
                    if (allTypeIndex == disabledTypeIndex)
                        includeInactive = true;
                    if (allTypeIndex == prefabTypeIndex)
                        includePrefab = true;
                    if (allTypeIndex == chunkHeaderTypeIndex)
                        includeChunkHeader = true;
                    if (allTypeIndex == systemInstanceTypeIndex)
                        includeSystems = true;

                    if (componentTypeIndex == allTypeIndex) foundCount++;
                }
            }

            if (archetype->Disabled && (!includeInactive))
                return false;
            if (archetype->Prefab && (!includePrefab))
                return false;
            if (archetype->HasSystemInstanceComponents && (!includeSystems))
                return false;
            if (archetype->HasChunkHeader && (!includeChunkHeader))
                return false;

            return foundCount == allCount;
        }

        public static int FindMatchingArchetypeIndexForArchetype(ref UnsafeMatchingArchetypePtrList matchingArchetypes,
            Archetype* archetype)
        {
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            for (int i = 0; i < archetypeCount; ++i)
            {
                if (archetype == ptrs[i]->Archetype)
                    return i;
            }

            return -1;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void ThrowIfEntityQueryMasksIsGreaterThanLimit(int entityQueryMasksAllocated)
        {
            if (entityQueryMasksAllocated >= 1024)
                throw new Exception("You have reached the limit of 1024 unique EntityQueryMasks, and cannot generate any more.");
        }

        public EntityQueryMask GetEntityQueryMask(EntityQueryData* query, EntityComponentStore* ecStore)
        {
            if (query->EntityQueryMask.IsCreated())
                return query->EntityQueryMask;

            ThrowIfEntityQueryMasksIsGreaterThanLimit(m_EntityQueryMasksAllocated);

            var mask = new EntityQueryMask(
                (byte)(m_EntityQueryMasksAllocated / 8),
                (byte)(1 << (m_EntityQueryMasksAllocated % 8)),
                ecStore);

            m_EntityQueryMasksAllocated++;

            int archetypeCount = query->MatchingArchetypes.Length;
            var ptrs = query->MatchingArchetypes.Ptr;
            for (var i = 0; i < archetypeCount; ++i)
            {
                ptrs[i]->Archetype->QueryMaskArray[mask.Index] |= mask.Mask;
            }

            query->EntityQueryMask = mask;

            return mask;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct MatchingArchetype
    {
        public Archetype* Archetype;
        public int RequiredComponentCount;
        public int EnableableComponentsCount_All;
        public int EnableableComponentsCount_None;
        public int EnableableComponentsCount_Any;

        public fixed int IndexInArchetype[1];

        public int* EnableableIndexInArchetype_All
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount;
                }
            }
        }
        public int* EnableableIndexInArchetype_None
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All;
                }
            }
        }
        public int* EnableableIndexInArchetype_Any
        {
            get
            {
                fixed (int* basePtr = IndexInArchetype)
                {
                    return basePtr + RequiredComponentCount + EnableableComponentsCount_All + EnableableComponentsCount_None;
                }
            }
        }

        public static int CalculateMatchingArchetypeEnableableTypeIntersectionCount(Archetype* archetype, TypeIndex* queryComponents, int queryComponentCount)
        {
            var intersectionCount = 0;
            var typeComponentIndex = 0;
            for (int i = 0; i < queryComponentCount; ++i)
            {
                var typeIndex = queryComponents[i];
                if (!TypeManager.IsEnableable(typeIndex))
                    continue;
                var currentTypeComponentIndex = ChunkDataUtility.GetNextIndexInTypeArray(archetype, typeIndex, typeComponentIndex);
                if (currentTypeComponentIndex >= 0)
                {
                    typeComponentIndex = currentTypeComponentIndex;
                    intersectionCount++;
                }
            }

            return intersectionCount;
        }

        public static MatchingArchetype* Create(ref BlockAllocator allocator, Archetype* archetype, EntityQueryData* query)
        {
            // TODO(DOTS-5638): this assumes that query only contains one ArchetypeQuery
            var enableableAllCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->All, query->ArchetypeQueries->AllCount);
            var enableableNoneCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->None, query->ArchetypeQueries->NoneCount);
            var enableableAnyCount = CalculateMatchingArchetypeEnableableTypeIntersectionCount(archetype, query->ArchetypeQueries->Any, query->ArchetypeQueries->AnyCount);
            var totalEnableableTypeCount = enableableAllCount + enableableNoneCount + enableableAnyCount;
            var match = (MatchingArchetype*)allocator.Allocate(GetAllocationSize(query->RequiredComponentsCount, totalEnableableTypeCount), 8);
            match->Archetype = archetype;

            match->RequiredComponentCount = query->RequiredComponentsCount;
            match->EnableableComponentsCount_All = enableableAllCount;
            match->EnableableComponentsCount_None = enableableNoneCount;
            match->EnableableComponentsCount_Any = enableableAnyCount;

            return match;
        }

        private static int GetAllocationSize(int requiredComponentsCount, int enableableComponentCount)
        {
            return sizeof(MatchingArchetype) +
                   sizeof(int) * (requiredComponentsCount-1) +
                   sizeof(int) * enableableComponentCount;
        }

        public bool ChunkMatchesFilter(int chunkIndex, ref EntityQueryFilter filter)
        {
            var chunks = Archetype->Chunks;

            // Must match ALL shared component data
            for (int i = 0; i < filter.Shared.Count; ++i)
            {
                var indexInEntityQuery = filter.Shared.IndexInEntityQuery[i];
                var sharedComponentIndex = filter.Shared.SharedComponentIndex[i];
                var componentIndexInChunk = IndexInArchetype[indexInEntityQuery] - Archetype->FirstSharedComponent;
                var sharedComponents = chunks.GetSharedComponentValueArrayForType(componentIndexInChunk);

                // if we don't have a match, we can early out
                if (sharedComponents[chunkIndex] != sharedComponentIndex)
                    return false;
            }

            if (filter.Changed.Count == 0 && !filter.UseOrderFiltering)
                return true;

            var orderVersionFilterPassed = filter.UseOrderFiltering && ChangeVersionUtility.DidChange(chunks.GetOrderVersion(chunkIndex), filter.RequiredChangeVersion);

            // Must have AT LEAST ONE type have changed
            var changedVersionFilterPassed = false;
            for (int i = 0; i < filter.Changed.Count; ++i)
            {
                var indexInEntityQuery = filter.Changed.IndexInEntityQuery[i];
                var componentIndexInChunk = IndexInArchetype[indexInEntityQuery];
                var changeVersions = chunks.GetChangeVersionArrayForType(componentIndexInChunk);

                var requiredVersion = filter.RequiredChangeVersion;

                changedVersionFilterPassed |= ChangeVersionUtility.DidChange(changeVersions[chunkIndex], requiredVersion);
            }

            return changedVersionFilterPassed || orderVersionFilterPassed;
        }
    }

    [DebuggerTypeProxy(typeof(UnsafeMatchingArchetypePtrListDebugView))]
    unsafe struct UnsafeMatchingArchetypePtrList
    {
        [NativeDisableUnsafePtrRestriction]
        private UnsafeList<IntPtr>* ListData;

        public MatchingArchetype** Ptr { get => (MatchingArchetype**)ListData->Ptr; }
        public int Length { get => ListData->Length; }

        public void Dispose() { UnsafeList<IntPtr>.Destroy(ListData); }
        public void Add(void* t) { ListData->Add((IntPtr)t); }

        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* entityComponentStore;

        public UnsafeMatchingArchetypePtrList(EntityComponentStore* entityComponentStore)
        {
            ListData = UnsafeList<IntPtr>.Create(0, Allocator.Persistent);
            this.entityComponentStore = entityComponentStore;
        }
    }

    [GenerateTestsForBurstCompatibility]
    unsafe struct ArchetypeQuery : IEquatable<ArchetypeQuery>
    {
        public TypeIndex*   Any;
        public byte*        AnyAccessMode;
        public int          AnyCount;

        public TypeIndex*   All;
        public byte*        AllAccessMode;
        public int          AllCount;

        public TypeIndex*   None;
        public byte*        NoneAccessMode;
        public int          NoneCount;

        public EntityQueryOptions  Options;

        public bool Equals(ArchetypeQuery other)
        {
            if (AnyCount != other.AnyCount)
                return false;
            if (AllCount != other.AllCount)
                return false;
            if (NoneCount != other.NoneCount)
                return false;
            if (AnyCount > 0 && UnsafeUtility.MemCmp(Any, other.Any, sizeof(int) * AnyCount) != 0 &&
                UnsafeUtility.MemCmp(AnyAccessMode, other.AnyAccessMode, sizeof(byte) * AnyCount) != 0)
                return false;
            if (AllCount > 0 && UnsafeUtility.MemCmp(All, other.All, sizeof(int) * AllCount) != 0 &&
                UnsafeUtility.MemCmp(AllAccessMode, other.AllAccessMode, sizeof(byte) * AllCount) != 0)
                return false;
            if (NoneCount > 0 && UnsafeUtility.MemCmp(None, other.None, sizeof(int) * NoneCount) != 0 &&
                UnsafeUtility.MemCmp(NoneAccessMode, other.NoneAccessMode, sizeof(byte) * NoneCount) != 0)
                return false;
            if (Options != other.Options)
                return false;

            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode =                  (AnyCount + 1);
                hashCode = 397 * hashCode ^ (AllCount + 1);
                hashCode = 397 * hashCode ^ (NoneCount + 1);
                hashCode = 397 * hashCode ^ (int)Options;
                hashCode = (int)math.hash(Any, sizeof(int) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(All, sizeof(int) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(None, sizeof(int) * NoneCount, (uint)hashCode);
                hashCode = (int)math.hash(AnyAccessMode, sizeof(byte) * AnyCount, (uint)hashCode);
                hashCode = (int)math.hash(AllAccessMode, sizeof(byte) * AllCount, (uint)hashCode);
                hashCode = (int)math.hash(NoneAccessMode, sizeof(byte) * NoneCount, (uint)hashCode);
                return hashCode;
            }
        }
    }

    [NoAlias]
    [BurstCompile]
    unsafe struct UnsafeCachedChunkList
    {
        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafePtrList<Chunk>* MatchingChunks;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafeList<int>* PerChunkMatchingArchetypeIndex;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal UnsafeList<int>* ChunkIndexInArchetype;

        [NoAlias,NativeDisableUnsafePtrRestriction]
        internal EntityComponentStore* EntityComponentStore;

        internal int CacheValid; // must not be a bool, for Burst compatibility

        internal Chunk** Ptr { get => (Chunk**)MatchingChunks->Ptr; }
        public int Length { get => MatchingChunks->Length; }
        public bool IsCacheValid { get => CacheValid != 0; }

        internal UnsafeCachedChunkList(EntityComponentStore* entityComponentStore)
        {
            EntityComponentStore = entityComponentStore;
            MatchingChunks = UnsafePtrList<Chunk>.Create(0, Allocator.Persistent);
            PerChunkMatchingArchetypeIndex = UnsafeList<int>.Create(0, Allocator.Persistent);
            ChunkIndexInArchetype = UnsafeList<int>.Create(0, Allocator.Persistent);
            CacheValid = 0;
        }

        internal void Append(Chunk** t, int addChunkCount, int matchingArchetypeIndex)
        {
            MatchingChunks->AddRange(new UnsafePtrList<Chunk>(t, addChunkCount));
            for (int i = 0; i < addChunkCount; ++i)
            {
                PerChunkMatchingArchetypeIndex->Add(matchingArchetypeIndex);
                ChunkIndexInArchetype->Add(i);
            }
        }

        internal void Dispose()
        {
            if (MatchingChunks != null)
                UnsafePtrList<Chunk>.Destroy(MatchingChunks);
            if (PerChunkMatchingArchetypeIndex != null)
                UnsafeList<int>.Destroy(PerChunkMatchingArchetypeIndex);
            if (ChunkIndexInArchetype != null)
                UnsafeList<int>.Destroy(ChunkIndexInArchetype);
        }

        internal void Invalidate()
        {
            CacheValid = 0;
        }

        [BurstCompile]
        internal static void Rebuild(ref UnsafeCachedChunkList cache, in EntityQueryData queryData)
        {
            Assert.AreEqual((ulong)queryData.MatchingChunkCache.MatchingChunks, (ulong)cache.MatchingChunks);

            cache.MatchingChunks->Clear();
            cache.PerChunkMatchingArchetypeIndex->Clear();
            cache.ChunkIndexInArchetype->Clear();

            int archetypeCount = queryData.MatchingArchetypes.Length;
            var ptrs = queryData.MatchingArchetypes.Ptr;
            for (int matchingArchetypeIndex = 0; matchingArchetypeIndex < archetypeCount; ++matchingArchetypeIndex)
            {
                var archetype = ptrs[matchingArchetypeIndex]->Archetype;
                if (archetype->EntityCount > 0)
                    archetype->Chunks.AddToCachedChunkList(ref cache, matchingArchetypeIndex);
            }

            cache.CacheValid = 1;
        }

        // Expensive debug validation, to make sure cached data is consistent with the associated query.
        internal static bool IsConsistent(in UnsafeCachedChunkList cache, in EntityQueryData queryData)
        {
            Assert.AreEqual((ulong)queryData.MatchingChunkCache.MatchingChunks, (ulong)cache.MatchingChunks);

            var chunkCounter = 0;
            int archetypeCount = queryData.MatchingArchetypes.Length;
            var ptrs = queryData.MatchingArchetypes.Ptr;
            for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
            {
                var archetype = ptrs[archetypeIndex]->Archetype;
                for (int chunkIndex = 0; chunkIndex < archetype->Chunks.Count; ++chunkIndex)
                {
                    if (chunkCounter >= cache.MatchingChunks->Length)
                        return false;
                    var chunk = cache.MatchingChunks->Ptr[chunkCounter];
                    if(chunk != archetype->Chunks[chunkIndex])
                        return false;
                    if (cache.ChunkIndexInArchetype->Ptr[chunkCounter] != chunk->ListIndex)
                        return false;
                    chunkCounter += 1;
                }
            }

            // All chunks in cache are accounted for
            if (chunkCounter != cache.Length)
                return false;

            return true;
        }
    }

    unsafe struct EntityQueryData : IDisposable
    {
        //@TODO: better name or remove entirely...
        public ComponentType*       RequiredComponents;
        public int                  RequiredComponentsCount;

        public TypeIndex*           ReaderTypes;
        public int                  ReaderTypesCount;

        public TypeIndex*           WriterTypes;
        public int                  WriterTypesCount;

        public TypeIndex*           EnableableComponentTypeIndices;
        public int                  EnableableComponentTypeIndexCount; // number of elements in EnableableComponentTypeIndices

        public ArchetypeQuery*      ArchetypeQueries;
        public int                  ArchetypeQueryCount;

        public EntityQueryMask      EntityQueryMask;

        public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        internal UnsafeCachedChunkList MatchingChunkCache;

        public byte DoesQueryRequireBatching; // 0 = no, 1 = yes

        public unsafe UnsafeCachedChunkList GetMatchingChunkCache()
        {
            // TODO(DOTS-5721): not thread safe, should only be called on the main thread.
            if (!MatchingChunkCache.IsCacheValid)
                UnsafeCachedChunkList.Rebuild(ref MatchingChunkCache, this);

            return MatchingChunkCache;
        }

        public void Dispose()
        {
            MatchingArchetypes.Dispose();
            MatchingChunkCache.Dispose();
        }
    }
}
