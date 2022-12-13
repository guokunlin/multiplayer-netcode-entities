using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    class GenericComponentDataTest  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;

            public partial class GenericComponentDataSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    EntityManager.CreateEntity(typeof(GenericDataType<int>));
                    SetSingleton(new GenericDataType<int>() { value = 10 });
                }

                public struct GenericDataType<T> : IComponentData where T : unmanaged
                {
                    public T value;
                }
            }";

        [Test]
        public void GenericSingletonDoesNotTriggerEntityQueryGeneration_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "GenericComponentDataSystem"
                });
        }
    }
}
