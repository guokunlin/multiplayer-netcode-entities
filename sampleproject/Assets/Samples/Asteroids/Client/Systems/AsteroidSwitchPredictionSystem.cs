using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Collections;
using Unity.Rendering;

namespace Asteroids.Client
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct AsteroidSwitchPredictionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ClientSettings>();
            state.RequireForUpdate<ShipCommandData>();
            state.RequireForUpdate<AsteroidTagComponentData>();
            state.RequireForUpdate<GhostPredictionSwitchingQueues>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<ClientSettings>();
            if (settings.predictionRadius <= 0)
                return;

            var stateEntityManager = state.EntityManager;
            if (!SystemAPI.TryGetSingletonEntity<ShipCommandData>(out var playerEnt) || !stateEntityManager.HasComponent<Translation>(playerEnt))
                return;

            var playerPos = stateEntityManager.GetComponentData<Translation>(playerEnt).Value;
            var parallelEcb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
            var ghostPredictionSwitchingQueues = SystemAPI.GetSingletonRW<GhostPredictionSwitchingQueues>().ValueRW;

            new SwitchToPredictedGhostViaRange
            {
                playerPos = playerPos,
                parallelEcb = parallelEcb,
                predictedQueue = ghostPredictionSwitchingQueues.ConvertToPredictedQueue,
                enterRadiusSq = settings.predictionRadius*settings.predictionRadius,
            }.ScheduleParallel();

            var radiusPlusMargin = settings.predictionRadius + settings.predictionRadiusMargin;
            new SwitchToInterpolatedGhostViaRange
            {
                playerPos = playerPos,
                parallelEcb = parallelEcb,
                interpolatedQueue = ghostPredictionSwitchingQueues.ConvertToInterpolatedQueue,
                exitRadiusSq = radiusPlusMargin * radiusPlusMargin,
            }.ScheduleParallel();
        }

        [BurstCompile]
        [WithNone(typeof(PredictedGhostComponent), typeof(SwitchPredictionSmoothing))]
        [WithAll(typeof(AsteroidTagComponentData))]
        partial struct SwitchToPredictedGhostViaRange : IJobEntity
        {
            public float3 playerPos;
            public float enterRadiusSq;
            public NativeQueue<ConvertPredictionEntry>.ParallelWriter predictedQueue;
            public EntityCommandBuffer.ParallelWriter parallelEcb;

            void Execute(Entity ent, [EntityInQueryIndex] int entityInQueryIndex, in Translation position)
            {
                if (math.distancesq(playerPos, position.Value) < enterRadiusSq)
                {
                    predictedQueue.Enqueue(new ConvertPredictionEntry
                    {
                        TargetEntity = ent,
                        TransitionDurationSeconds = 1.0f,
                    });
                    parallelEcb.AddComponent(entityInQueryIndex, ent, new URPMaterialPropertyBaseColor { Value = new float4(0, 1, 0, 1) });
                }
            }
        }

        [BurstCompile]
        [WithNone(typeof(SwitchPredictionSmoothing))]
        [WithAll(typeof(PredictedGhostComponent), typeof(AsteroidTagComponentData))]
        partial struct SwitchToInterpolatedGhostViaRange : IJobEntity
        {
            public float3 playerPos;
            public float exitRadiusSq;

            public NativeQueue<ConvertPredictionEntry>.ParallelWriter interpolatedQueue;
            public EntityCommandBuffer.ParallelWriter parallelEcb;

            void Execute(Entity ent, [EntityInQueryIndex] int entityInQueryIndex, in Translation position)
            {
                if (math.distancesq(playerPos, position.Value) > exitRadiusSq)
                {
                    interpolatedQueue.Enqueue(new ConvertPredictionEntry
                    {
                        TargetEntity = ent,
                        TransitionDurationSeconds = 1.0f,
                    });
                    parallelEcb.RemoveComponent<URPMaterialPropertyBaseColor>(entityInQueryIndex, ent);
                }
            }
        }
    }
}
