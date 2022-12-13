using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// [GenerateAuthoringComponent]
public struct CubeComponent : IComponentData
{
    [GhostField]
    public int ExampleValue;
}

[DisallowMultipleComponent]
public class CubeComponentAuthoring : MonoBehaviour
{
    class MovableCubeComponentBaker : Baker<CubeComponentAuthoring>
    {
        public override void Bake(CubeComponentAuthoring authoring)
        {
            CubeComponent component = default(CubeComponent);
            AddComponent(component);
        }
    }
}