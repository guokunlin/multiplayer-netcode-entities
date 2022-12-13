using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Hybrid.Tests
{
    public struct SubSceneSectionTestData : IComponentData
    {
        public SubSceneSectionTestData(int value)
        {
            Value = value;
        }

        public int Value;
    }

    public class SubSceneSectionTestDataAuthoringForBaking : MonoBehaviour
    {
        public int Value;
    }

    public class SubSceneSectionTestDataBaker : Baker<SubSceneSectionTestDataAuthoringForBaking>
    {
        public override void Bake(SubSceneSectionTestDataAuthoringForBaking authoring)
        {
            AddComponent(GetEntity(authoring), new SubSceneSectionTestData(){Value = authoring.Value});
        }
    }
}
