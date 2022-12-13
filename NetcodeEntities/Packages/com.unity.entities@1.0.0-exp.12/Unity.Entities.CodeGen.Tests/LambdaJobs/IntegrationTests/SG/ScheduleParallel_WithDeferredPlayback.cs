﻿using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    class ScheduleParallel_WithDeferredPlayback : LambdaJobsSourceGenerationIntegrationTest
    {
        private const string Code =
            @"using Unity.Entities;
            using Unity.Mathematics;

            public struct WidgetSpawner : IComponentData
            {
                public Entity WidgetPrefabEntity;
            }

            public struct Translation : IComponentData
            {
                public float3 Value;
            }

            public struct Health : IComponentData
            {
                public float Score;
            }

            public partial class ScheduleParallel_WithDeferredPlayback : SystemBase
            {
                protected override void OnUpdate()
                {
                    var random = new Random();

                    Entities
                        .WithDeferredPlaybackSystem<EndSimulationEntityCommandBufferSystem>()
                        .ForEach(
                            (Entity entity, EntityCommandBuffer buffer, in WidgetSpawner spawner, in Translation translation) =>
                            {
                                var widget = buffer.Instantiate(spawner.WidgetPrefabEntity);
                                buffer.AddComponent(widget, new Health { Score = random.NextFloat() });
                                buffer.SetComponent(widget, new Translation { Value = translation.Value - random.NextFloat3() });
                                buffer.RemoveComponent<Health>(widget);
                                buffer.DestroyEntity(widget);
                            })
                        .ScheduleParallel();
                }
            }";

        [Test]
        public void ScheduleParallel_WithDeferredPlaybackTest()
        {
            RunTest(Code, new GeneratedType {Name = "ScheduleParallel_WithDeferredPlayback"});
        }
    }
}
