using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class JobEntityCalledWithIndirection : JobEntitySourceGenerationTests
    {
        private const string Code =
            @"
            using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;

            public partial struct MyJobEntity : IJobEntity
            {
                public float DeltaTime;

                public void Execute(ref Rotation rotation, in RotationSpeed_ForEach speed)
                {
                    rotation.Value =
                        math.mul(math.normalize(rotation.Value),
                            quaternion.AxisAngle(math.up(),
                                speed.RadiansPerSecond * DeltaTime));
                }
            }

            public struct Rotation : IComponentData
            {
                public quaternion Value;
            }

            public struct RotationSpeed_ForEach : IComponentData
            {
                public float RadiansPerSecond;
            }

            public partial class JobEntityCalledWithIndirectionSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Method1();
                }

                private void Method1()
                {
                    Method2();
                }

                private void Method2()
                {
                    var job = new MyJobEntity
                    {
                        DeltaTime = Time.DeltaTime
                    };
                    Dependency = job.ScheduleParallel(Dependency);
                }
            }";

        [Test]
        public void JobEntity_CalledAfterLayersOfIndirectionTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntityCalledWithIndirectionSystem"
                });
        }
    }
}
