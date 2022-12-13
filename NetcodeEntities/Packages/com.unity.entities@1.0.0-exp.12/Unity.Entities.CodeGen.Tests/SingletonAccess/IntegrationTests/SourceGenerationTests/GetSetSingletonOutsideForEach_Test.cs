using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class GetSetSingletonOutsideForEach_Test  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;

              partial class GetSetSingletonOutsideForEach : SystemBase
              {
                  public float SingletonValue
                  {
                      get
                      {
                          return GetSingleton<SingletonData>().Value;
                      }
                      set => SetSingleton(new SingletonData() { Value = value });
                  }

                  protected override void OnUpdate()
                  {
                      float singletonValue = GetSingleton<SingletonData>().Value;
                      singletonValue += 10.0f;
                      SetSingleton(new SingletonData() {Value = singletonValue});
                      GetSingletonEntity<SingletonData>();
                  }
              }

              public struct SingletonData : IComponentData
              {
                  public float Value;
              }";

        [Test]
        public void GetSetSingleton_OutsideEntitiesForEach_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "GetSetSingletonOutsideForEach"
                });
        }
    }
}
