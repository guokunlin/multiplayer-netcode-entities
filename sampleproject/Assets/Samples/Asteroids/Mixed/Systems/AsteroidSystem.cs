using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct AsteroidSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AsteroidTagComponentData>()
                .WithNone<StaticAsteroid>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
        }
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {}
        [BurstCompile]
        [WithAll(typeof(Simulate), typeof(AsteroidTagComponentData))]
        [WithNone(typeof(StaticAsteroid))]
        partial struct AsteroidJob : IJobEntity
        {
            public float deltaTime;
            public void Execute(ref Translation position, ref Rotation rotation, in Velocity velocity)
            {
                position.Value.xy += velocity.Value * deltaTime;
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(100 * deltaTime)));
            }
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var asteroidJob = new AsteroidJob
            {
                deltaTime = SystemAPI.Time.DeltaTime
            };
            state.Dependency = asteroidJob.ScheduleParallel(state.Dependency);
        }
    }
}
