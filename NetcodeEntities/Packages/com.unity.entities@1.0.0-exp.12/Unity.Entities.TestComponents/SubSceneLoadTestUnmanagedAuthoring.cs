using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    public class SubSceneLoadTestUnmanagedAuthoring : MonoBehaviour
    {
        public GameObject Entity;
        public int Int;
    }

    public struct SubSceneLoadTestUnmanagedComponent : IComponentData
    {
        public int Int;
        public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
        public Entity Entity;
    }

    public class SubSceneLoadTestUnmanagedBaker : Baker<SubSceneLoadTestUnmanagedAuthoring>
    {
        public override void Bake(SubSceneLoadTestUnmanagedAuthoring authoring)
        {
            AddComponent(new SubSceneLoadTestUnmanagedComponent()
            {
                Entity = GetEntity(),
                Int = authoring.Int,
                BlobAsset = SubSceneLoadTestBlobAsset.Make(authoring.Int, authoring.Int + 1, authoring.gameObject.name, SubSceneLoadTestBlobAsset.MakeStrings(1))
            });
        }
    }
}
