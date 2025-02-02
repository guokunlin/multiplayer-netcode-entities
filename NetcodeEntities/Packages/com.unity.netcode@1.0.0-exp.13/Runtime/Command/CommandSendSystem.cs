using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

namespace Unity.NetCode
{
    /// <summary>
    /// This singleton is used by code-gen. It stores a mapping of which ticks the client
    /// has changes to inputs so steps in the prediction loop can be batched when inputs
    /// are not changing.
    /// </summary>
    public struct UniqueInputTickMap : IComponentData
    {
        /// <summary>
        /// The set of ticks where inputs were changed compared to the frame before it. The value is not used but usually set to the same tick as the key.
        /// </summary>
        public NativeParallelHashMap<NetworkTick, NetworkTick>.ParallelWriter Value;
        internal NativeParallelHashMap<NetworkTick, NetworkTick> TickMap;
    }

    /// <summary>
    /// The parent group for all input gather systems. Only present in client worlds,
    /// it runs before the <see cref="CommandSendSystemGroup"/> in order to remove any latency in betwen the input gathering and
    /// the command submission.
    /// All the your systems that translate user input (ex: using the <see cref="UnityEngine.Input"/> into
    /// <see cref="ICommandData"/> command data must should update in this group.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation, WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostSimulationSystemGroup))]
    [UpdateBefore(typeof(CommandSendSystemGroup))]
    public partial class GhostInputSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// This group contains all core-generated system that are used to compare commands for sake of identifing the ticks the client
    /// has changed input (see <see cref="m_UniqueInputTicks"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation, WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostSimulationSystemGroup))]
    [UpdateAfter(typeof(GhostInputSystemGroup))]
    public partial class CompareCommandSystemGroup : ComponentSystemGroup
    {
        private NativeParallelHashMap<NetworkTick, NetworkTick> m_UniqueInputTicks;
        /// <summary>
        /// Create the <see cref="UniqueInputTickMap"/> singleton and store a reference to the
        /// UniqueInputTicks hash map
        /// </summary>
        protected override void OnCreate()
        {
            m_UniqueInputTicks = new NativeParallelHashMap<NetworkTick, NetworkTick>(CommandDataUtility.k_CommandDataMaxSize * 4, Allocator.Persistent);
            var singletonEntity = EntityManager.CreateEntity(ComponentType.ReadWrite<UniqueInputTickMap>());
            EntityManager.SetName(singletonEntity, "UniqueInputTickMap-Singleton");
            EntityManager.SetComponentData(singletonEntity, new UniqueInputTickMap{Value = m_UniqueInputTicks.AsParallelWriter(), TickMap = m_UniqueInputTicks});

            base.OnCreate();
        }
        /// <summary>
        /// Dispose all the allocated resources.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            m_UniqueInputTicks.Dispose();
        }
    }

    /// <summary>
    /// Parent group of all systems that serialize <see cref="ICommandData"/> structs into the
    /// <see cref="OutgoingCommandDataStreamBufferComponent"/> buffer.
    /// The serialized commands are then sent later by the <see cref="CommandSendPacketSystem"/>.
    /// Only present in client world.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation, WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(GhostSimulationSystemGroup))]
    // dependency just for acking
    [UpdateAfter(typeof(GhostReceiveSystem))]
    public partial class CommandSendSystemGroup : ComponentSystemGroup
    {
        private NetworkTick m_lastServerTick;
        protected override void OnCreate()
        {
            base.OnCreate();
        }
        protected override void OnUpdate()
        {
            var clientNetTime = GetSingleton<NetworkTime>();
            var targetTick = NetworkTimeHelper.LastFullServerTick(clientNetTime);
            // Make sure we only send a single ack per tick - only triggers when using dynamic timestep
            if (targetTick == m_lastServerTick)
                return;
            m_lastServerTick = targetTick;
            base.OnUpdate();
        }
    }

    /// <summary>
    /// <para>System responsible for building and sending the command packet to the server.
    /// As part of the command protocol:</para>
    /// <para>- Flushes all the serialized commands present in the <see cref="OutgoingCommandDataStreamBufferComponent"/>.</para>
    /// <para>- Acks the latest received snapshot to the server.</para>
    /// <para>- Sends the client local and remote time (used to calculate the Round Trip Time) back to the server.</para>
    /// <para>- Sends the loaded ghost prefabs to the server.</para>
    /// <para>- Calculates the current client interpolation delay (used for lag compensation).</para>
    /// </summary>
    [UpdateInGroup(typeof(CommandSendSystemGroup), OrderLast = true)]
    [BurstCompile]
    internal partial struct CommandSendPacketSystem : ISystem
    {
        private StreamCompressionModel m_CompressionModel;
        private EntityQuery m_connectionQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NetworkStreamConnection, NetworkStreamInGame, NetworkSnapshotAckComponent>()
                .WithAllRW<OutgoingCommandDataStreamBufferComponent>();
            m_connectionQuery = state.GetEntityQuery(builder);
            m_CompressionModel = StreamCompressionModel.Default;

            state.RequireForUpdate<GhostCollection>();
            state.RequireForUpdate(m_connectionQuery);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        [WithAll(typeof(NetworkStreamInGame))]
        partial struct CommandSendPacket : IJobEntity
        {
            public ConcurrentDriverStore concurrentDriverStore;
            public NetDebug netDebug;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public NativeArray<uint> netStats;
#endif
            public uint localTime;
            public int numLoadedPrefabs;
            public NetworkTick inputTargetTick;
            public uint interpolationDelay;
            public unsafe void Execute(DynamicBuffer<OutgoingCommandDataStreamBufferComponent> rpcData,
                    in NetworkStreamConnection connection, in NetworkSnapshotAckComponent ack)
            {
                var concurrentDriver = concurrentDriverStore.GetConcurrentDriver(connection.DriverId);
                // FIXME: support fragmented
                if (concurrentDriver.driver.BeginSend(concurrentDriver.unreliablePipeline, connection.Value, out var writer) != 0)
                {
                    rpcData.Clear();
                    return;
                }

                writer.WriteByte((byte)NetworkStreamProtocol.Command);
                writer.WriteUInt(ack.LastReceivedSnapshotByLocal.SerializedData);
                writer.WriteUInt(ack.ReceivedSnapshotByLocalMask);
                writer.WriteUInt(localTime);

                uint returnTime = ack.LastReceivedRemoteTime;
                if (returnTime != 0)
                    returnTime += (localTime - ack.LastReceiveTimestamp);

                writer.WriteUInt(returnTime);
                writer.WriteUInt(interpolationDelay);
                writer.WriteUInt((uint)numLoadedPrefabs);
                writer.WriteUInt(inputTargetTick.SerializedData);

                writer.WriteBytesUnsafe((byte*)rpcData.GetUnsafeReadOnlyPtr(), rpcData.Length);
                rpcData.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                netStats[0] = inputTargetTick.SerializedData;
                netStats[1] = (uint)writer.Length;
#endif

                var result = 0;
                if ((result = concurrentDriver.driver.EndSend(writer)) <= 0)
                    netDebug.LogError(FixedString.Format("An error occured during EndSend. ErrorCode: {0}", result));
            }
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clientNetTime = SystemAPI.GetSingleton<NetworkTime>();
            var targetTick = NetworkTimeHelper.LastFullServerTick(clientNetTime);
            // The time left util interpolation is at the given tick, the delta should be increased by this
            var subTickDeltaAdjust = 1 - clientNetTime.InterpolationTickFraction;
            // The time left util we are actually at the server tick, the delta should be reduced by this
            subTickDeltaAdjust -= 1 - clientNetTime.ServerTickFraction;
            var interpolationDelay = clientNetTime.ServerTick.TicksSince(clientNetTime.InterpolationTick);
            if (subTickDeltaAdjust >= 1)
                ++interpolationDelay;
            else if (subTickDeltaAdjust < 0)
                --interpolationDelay;

            ref var networkStreamDriver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            var sendJob = new CommandSendPacket
            {
                concurrentDriverStore = networkStreamDriver.ConcurrentDriverStore,
                netDebug = SystemAPI.GetSingleton<NetDebug>(),
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                netStats = SystemAPI.GetSingletonRW<GhostStatsCollectionCommand>().ValueRO.Value,
#endif
                localTime = NetworkTimeSystem.TimestampMS,
                numLoadedPrefabs = SystemAPI.GetSingleton<GhostCollection>().NumLoadedPrefabs,
                inputTargetTick = targetTick,
                interpolationDelay = (uint)interpolationDelay
            };
            state.Dependency = sendJob.Schedule(state.Dependency);
            state.Dependency = networkStreamDriver.DriverStore.ScheduleFlushSendAllDrivers(state.Dependency);
        }
    }

    /// <summary>
    /// Helper struct for implementing systems to send commands.
    /// This is generally used by code-gen and should only be used directly in special cases.
    /// </summary>
    /// <typeparam name="TCommandDataSerializer">Unmanaged CommandDataSerializer of type ICommandDataSerializer.</typeparam>
    /// <typeparam name="TCommandData">Unmanaged CommandData of type ICommandData.</typeparam>
    public struct CommandSendSystem<TCommandDataSerializer, TCommandData>
        where TCommandData : unmanaged, ICommandData
        where TCommandDataSerializer : unmanaged, ICommandDataSerializer<TCommandData>
    {
        /// <summary>
        /// The maximum number of inputs sent for every packet. Command packets add some redundancy by
        /// sending old commands to mimize the effect of packet loss.
        /// </summary>
        public const uint k_InputBufferSendSize = 4;

        /// <summary>
        /// Helper struct used by code-generated command job to serialize the <see cref="ICommandData"/> into the
        /// <see cref="OutgoingCommandDataStreamBufferComponent"/> for the client connection.
        /// </summary>
        public struct SendJobData
        {
            /// <summary>
            /// The readonly <see cref="CommandTargetComponent"/> type handle for accessing the chunk data.
            /// </summary>
            [ReadOnly] public ComponentTypeHandle<CommandTargetComponent> commmandTargetType;
            /// <summary>
            /// The readonly <see cref="networkIdType"/> type handle for accessing the chunk data.
            /// </summary>
            [ReadOnly] public ComponentTypeHandle<NetworkIdComponent> networkIdType;
            /// <summary>
            /// <see cref="OutgoingCommandDataStreamBufferComponent"/> buffer type handle for accessing the chunk data.
            /// This is the output of buffer for the job
            /// </summary>
            public BufferTypeHandle<OutgoingCommandDataStreamBufferComponent> outgoingCommandBufferType;
            /// <summary>
            /// Accessor for retrieving the input buffer from the target entity.
            /// </summary>
            [ReadOnly] public BufferLookup<TCommandData> inputFromEntity;
            /// <summary>
            /// Reaonly <see cref="GhostComponent"/> type handle for accessing the chunk data.
            /// </summary>
            [ReadOnly] public ComponentLookup<GhostComponent> ghostFromEntity;
            /// <summary>
            /// Readonly accessor to retrieve the <see cref="GhostOwnerComponent"/> from the target ghost entity.
            /// </summary>
            [ReadOnly] public ComponentLookup<GhostOwnerComponent> ghostOwnerFromEntity;
            /// <summary>
            /// Readonly accessor to retrieve the <see cref="AutoCommandTarget"/> from the target ghost entity.
            /// </summary>
            [ReadOnly] public ComponentLookup<AutoCommandTarget> autoCommandTargetFromEntity;
            /// <summary>
            /// The compression model used to delta encode the old inputs. The first input (the one for the current tick)
            /// is serialized as it is. The older ones, are serialized as delta in respect the first one to reduce the bandwidth.
            /// </summary>
            public StreamCompressionModel compressionModel;
            /// <summary>
            /// The server tick the command should be executed on the server.
            /// </summary>
            public NetworkTick inputTargetTick;
            /// <summary>
            /// The last server tick for which we send this command
            /// </summary>
            public NetworkTick prevInputTargetTick;
            /// <summary>
            /// The list of all ghost entities with a <see cref="AutoCommandTarget"/> component.
            /// </summary>
            [ReadOnly] public NativeList<Entity> autoCommandTargetEntities;
            /// <summary>
            /// The stable type hash for the command type. Serialized and used on the server side to match and verify the correctness
            /// of the input data sent.
            /// </summary>
            public ulong stableHash;

            void Serialize(DynamicBuffer<OutgoingCommandDataStreamBufferComponent> rpcData, Entity targetEntity, bool isAutoTarget)
            {
                var input = inputFromEntity[targetEntity];
                TCommandData baselineInputData;
                // Check if the buffer has any data for the ticks we are trying to send, first chck if it has data at all
                if (!input.GetDataAtTick(inputTargetTick, out baselineInputData))
                    return;
                // Next check if we have previously sent the latest input, and the latest data we have would not fit in the buffer
                // The check for previously sent is important to handle really bad client performance
                if (prevInputTargetTick.IsValid && !baselineInputData.Tick.IsNewerThan(prevInputTargetTick) && inputTargetTick.TicksSince(baselineInputData.Tick) >= CommandDataUtility.k_CommandDataMaxSize)
                    return;

                var oldLen = rpcData.Length;
                rpcData.ResizeUninitialized(oldLen + 1024);
                var writer = new DataStreamWriter(rpcData.Reinterpret<byte>().AsNativeArray().GetSubArray(oldLen, 1024));

                writer.WriteULong(stableHash);
                var lengthWriter = writer;
                writer.WriteUShort(0);
                var startLength = writer.Length;
                if (isAutoTarget)
                {
                    var ghostComponent = ghostFromEntity[targetEntity];
                    writer.WriteInt(ghostComponent.ghostId);
                    writer.WriteUInt(ghostComponent.spawnTick.SerializedData);
                }
                else
                {
                    writer.WriteInt(0);
                    writer.WriteUInt(0);
                }

                var serializerState = new RpcSerializerState {GhostFromEntity = ghostFromEntity};
                var serializer = default(TCommandDataSerializer);
                writer.WriteUInt(baselineInputData.Tick.SerializedData);
                serializer.Serialize(ref writer, serializerState, baselineInputData);
                // Target tick is the most recent tick which is older than the one we just sampled
                var targetTick = baselineInputData.Tick;
                if (targetTick.IsValid)
                {
                    targetTick.Decrement();
                }
                for (uint inputIndex = 1; inputIndex < k_InputBufferSendSize; ++inputIndex)
                {
                    TCommandData inputData = default;
                    if (targetTick.IsValid)
                        input.GetDataAtTick(targetTick, out inputData);
                    writer.WritePackedUIntDelta(inputData.Tick.SerializedData, baselineInputData.Tick.SerializedData, compressionModel);
                    serializer.Serialize(ref writer, serializerState, inputData, baselineInputData, compressionModel);

                    targetTick = inputData.Tick;
                    if (targetTick.IsValid)
                    {
                        targetTick.Decrement();
                    }
                }

                writer.Flush();
                lengthWriter.WriteUShort((ushort)(writer.Length - startLength));
                rpcData.ResizeUninitialized(oldLen + writer.Length);
            }

            /// <summary>
            /// Lookup all the ghost entities for which commands need to be serialized for the current
            /// tick and enqueue them into the <see cref="OutgoingCommandDataStreamBufferComponent"/>.
            /// Are considered as potential ghost targets:
            /// <para>- the entity referenced by the <see cref="CommandTargetComponent"/></para>
            /// <para>- All ghosts owned by the player (see <see cref="GhostOwnerComponent"/>) that present
            /// an enabled <see cref="AutoCommandTarget"/> components.</para>
            /// </summary>
            /// <param name="chunk">The chunk that contains the connection entities</param>
            /// <param name="orderIndex">unsed, the sorting index enequeing operation in the the entity command buffer</param>
            public void Execute(ArchetypeChunk chunk, int orderIndex)
            {
                var commandTargets = chunk.GetNativeArray(commmandTargetType);
                var networkIds = chunk.GetNativeArray(networkIdType);
                var rpcDatas = chunk.GetBufferAccessor(outgoingCommandBufferType);

                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                {
                    var targetEntity = commandTargets[i].targetEntity;
                    var owner = networkIds[i].Value;
                    bool sentTarget = false;
                    for (int ent = 0; ent < autoCommandTargetEntities.Length; ++ent)
                    {
                        var autoTarget = autoCommandTargetEntities[ent];
                        if (ghostOwnerFromEntity[autoTarget].NetworkId == owner &&
                            autoCommandTargetFromEntity[autoTarget].Enabled &&
                            inputFromEntity.HasBuffer(autoTarget))
                        {
                            Serialize(rpcDatas[i], autoTarget, true);
                            sentTarget |= (autoTarget == targetEntity);
                        }
                    }
                    if (!sentTarget && inputFromEntity.HasBuffer(targetEntity))
                        Serialize(rpcDatas[i], targetEntity, false);
                }
            }
        }

        /// <summary>
        /// The query to use when scheduling the processing job.
        /// </summary>
        public EntityQuery Query => m_connectionQuery;
        private EntityQuery m_connectionQuery;
        private EntityQuery m_autoTargetQuery;
        private EntityQuery m_networkTimeQuery;
        private StreamCompressionModel m_CompressionModel;
        private NetworkTick m_PrevInputTargetTick;

        private ComponentTypeHandle<CommandTargetComponent> m_CommandTargetComponentHandle;
        private ComponentTypeHandle<NetworkIdComponent> m_NetworkIdComponentHandle;
        private BufferTypeHandle<OutgoingCommandDataStreamBufferComponent> m_OutgoingCommandDataStreamBufferComponentHandle;
        private BufferLookup<TCommandData> m_TCommandDataFromEntity;
        private ComponentLookup<GhostComponent> m_GhostComponentFromEntity;
        private ComponentLookup<GhostOwnerComponent> m_GhostOwnerComponentFromEntity;
        private ComponentLookup<AutoCommandTarget> m_AutoCommandTargetFromEntity;
        /// <summary>
        /// Initialize the helper struct, should be called from OnCreate in an ISystem.
        /// </summary>
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NetworkStreamInGame, CommandTargetComponent>();
            m_connectionQuery = state.GetEntityQuery(builder);
            builder.Reset();
            builder.WithAll<GhostComponent, GhostOwnerComponent, PredictedGhostComponent, TCommandData, AutoCommandTarget>();
            m_autoTargetQuery = state.GetEntityQuery(builder);
            builder.Reset();
            builder.WithAll<NetworkTime>();
            m_networkTimeQuery = state.GetEntityQuery(builder);

            m_CompressionModel = StreamCompressionModel.Default;
            m_CommandTargetComponentHandle = state.GetComponentTypeHandle<CommandTargetComponent>(true);
            m_NetworkIdComponentHandle = state.GetComponentTypeHandle<NetworkIdComponent>(true);
            m_OutgoingCommandDataStreamBufferComponentHandle = state.GetBufferTypeHandle<OutgoingCommandDataStreamBufferComponent>();
            m_TCommandDataFromEntity = state.GetBufferLookup<TCommandData>(true);
            m_GhostComponentFromEntity = state.GetComponentLookup<GhostComponent>(true);
            m_GhostOwnerComponentFromEntity = state.GetComponentLookup<GhostOwnerComponent>(true);
            m_AutoCommandTargetFromEntity = state.GetComponentLookup<AutoCommandTarget>(true);

            state.RequireForUpdate(m_connectionQuery);
            state.RequireForUpdate(state.GetEntityQuery(builder));
            state.RequireForUpdate<GhostCollection>();
        }

        /// <summary>
        /// Initialize the internal state of a processing job, should be called from OnUpdate of an ISystem.
        /// </summary>
        /// <param name="state">Raw entity system state.</param>
        /// <returns>Constructed <see cref="SendJobData"/> with initialized state.</returns>
        public SendJobData InitJobData(ref SystemState state)
        {
            m_CommandTargetComponentHandle.Update(ref state);
            m_NetworkIdComponentHandle.Update(ref state);
            m_OutgoingCommandDataStreamBufferComponentHandle.Update(ref state);
            m_TCommandDataFromEntity.Update(ref state);
            m_GhostComponentFromEntity.Update(ref state);
            m_GhostOwnerComponentFromEntity.Update(ref state);
            m_AutoCommandTargetFromEntity.Update(ref state);

            var clientNetTime = m_networkTimeQuery.GetSingleton<NetworkTime>();
            var targetTick = NetworkTimeHelper.LastFullServerTick(clientNetTime);
            var targetEntities = m_autoTargetQuery.ToEntityListAsync(state.WorldUpdateAllocator, out var autoHandle);
            var sendJob = new SendJobData
            {
                commmandTargetType = m_CommandTargetComponentHandle,
                networkIdType = m_NetworkIdComponentHandle,
                outgoingCommandBufferType = m_OutgoingCommandDataStreamBufferComponentHandle,
                inputFromEntity = m_TCommandDataFromEntity,
                ghostFromEntity = m_GhostComponentFromEntity,
                ghostOwnerFromEntity = m_GhostOwnerComponentFromEntity,
                autoCommandTargetFromEntity = m_AutoCommandTargetFromEntity,
                compressionModel = m_CompressionModel,
                inputTargetTick = targetTick,
                prevInputTargetTick = m_PrevInputTargetTick,
                autoCommandTargetEntities = targetEntities,
                stableHash = TypeManager.GetTypeInfo<TCommandData>().StableTypeHash
            };
            m_PrevInputTargetTick = targetTick;
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, autoHandle);
            return sendJob;
        }

        /// <summary>
        /// Utility method to check if the processing job needs to run, used as an early-out in OnUpdate of an ISystem.
        /// </summary>
        /// <param name="state">Raw entity system state.</param>
        /// <returns>Whether the processing job needs to run.</returns>
        public bool ShouldRunCommandJob(ref SystemState state)
        {
            // If there are auto command target entities always run the job
            if (!m_autoTargetQuery.IsEmptyIgnoreFilter)
                return true;
            // Otherwise only run if CommandTarget exists and has this component type
            if (!m_connectionQuery.TryGetSingleton<CommandTargetComponent>(out var commandTarget))
                return false;
            if (!state.EntityManager.HasComponent<TCommandData>(commandTarget.targetEntity ))
                return false;

            return true;
        }
    }
}
