using Unity.Entities;
using UnityEngine;

namespace Unity.NetCode.Tests
{
    public class SomeDataAuthoring : MonoBehaviour
    {
    }

    class SomeDataAuthoringBaker : Baker<SomeDataAuthoring>
    {
        public override void Bake(SomeDataAuthoring authoring)
        {
            AddComponent(new SomeData {Value = Random.Range(1, 100)});
        }
    }
}
