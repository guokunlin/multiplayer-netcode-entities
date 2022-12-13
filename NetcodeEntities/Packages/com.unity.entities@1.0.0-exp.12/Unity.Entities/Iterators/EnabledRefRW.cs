using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    /// <summary>
    /// Stores a safe reference to a read-writable component enable bit.
    /// Also keeps a pointer to the chunk disabled count, which is updated when the enabled bit is written to.
    /// </summary>
    /// <remarks>Do not store outside of stack</remarks>
    /// <typeparam name="T">Type of enabled component</typeparam>
    [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleEnableableComponent) }, RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS")]
    public readonly struct EnabledRefRW<T> : IQueryTypeParameter where T : unmanaged, IEnableableComponent
    {
        readonly SafeBitRef m_Ptr;
        readonly unsafe int* m_PtrChunkDisabledCount;

        /// <summary>
        /// Constructor for writable enable reference to enableable component.
        /// This is typically used by generated code inside of Aspects.
        /// </summary>
        /// <param name="ptr">Pointer to single bit and safety handle</param>
        /// <param name="ptrChunkDisabledCount"></param>
        public unsafe EnabledRefRW(SafeBitRef ptr, int* ptrChunkDisabledCount)
        {
            m_Ptr = ptr;
            m_PtrChunkDisabledCount = ptrChunkDisabledCount;
        }

        /// <summary>
        /// Null value for this reference.
        /// </summary>
        public static unsafe EnabledRefRW<T> Null => new EnabledRefRW<T>(SafeBitRef.Null, null);

        /// <summary>
        /// Property that returns true if the reference is valid, false otherwise.
        /// </summary>
        public bool IsValid => m_Ptr.IsValid;

        /// <summary>
        /// Property to get enabled value of this reference (true if enabled, false otherwise).
        /// </summary>
        public bool ValueRO => m_Ptr.GetBit();

        /// <summary>
        /// Property to get or set enabled value of this reference  (true if enabled, false otherwise).
        /// </summary>
        public unsafe bool ValueRW
        {
            get => m_Ptr.GetBit();
            set
            {
                m_Ptr.SetBit(value);
                Interlocked.Add(ref UnsafeUtility.AsRef<int>(m_PtrChunkDisabledCount), value ? -1 : 1);
            }
        }
    }
}
