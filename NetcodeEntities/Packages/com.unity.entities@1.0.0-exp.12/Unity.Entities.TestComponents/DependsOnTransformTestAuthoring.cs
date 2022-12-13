using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class DependsOnTransformTestAuthoring : MonoBehaviour
    {
        public Transform Dependency;
        public bool SkipDependency;
        public struct Component : IComponentData
        {
            public Matrix4x4 LocalToWorld;
        }

        class Baker : Baker<DependsOnTransformTestAuthoring>
        {
            public override void Bake(DependsOnTransformTestAuthoring authoring)
            {
                if (!authoring.SkipDependency)
                {
                    DependsOn(authoring.Dependency);
                }

                if (authoring.Dependency == null)
                    return;
                AddComponent(new Component
                {
                    LocalToWorld = authoring.Dependency.localToWorldMatrix
                });
            }
        }
    }
}
