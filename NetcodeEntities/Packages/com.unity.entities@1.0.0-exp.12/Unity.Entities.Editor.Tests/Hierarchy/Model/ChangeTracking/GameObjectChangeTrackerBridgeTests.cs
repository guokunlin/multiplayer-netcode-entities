using System;
using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Editor.Bridge;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Editor.Tests
{
    sealed class GameObjectChangeTrackerBridgeTests
    {
#if UNITY_2022_1_OR_NEWER
        [UnityTest]
        public IEnumerator ShouldReceiveEvents()
        {
            var receivedEvents = new NativeList<GameObjectChangeTrackerEvent>(Allocator.TempJob);

            try
            {
                GameObjectChangeTrackerBridge.GameObjectsChanged += OnGameObjectsChanged;

                var go = new GameObject();
                yield return null;
                Assert.That(receivedEvents.ToArrayNBC(), Is.EquivalentTo(new[] { new GameObjectChangeTrackerEvent(go.GetInstanceID(), GameObjectChangeTrackerEventType.CreatedOrChanged) }));
            }
            finally
            {
                GameObjectChangeTrackerBridge.GameObjectsChanged -= OnGameObjectsChanged;
                receivedEvents.Dispose();
            }

            void OnGameObjectsChanged(in NativeArray<GameObjectChangeTrackerEvent> events)
            {
                receivedEvents.Clear();
                receivedEvents.Resize(events.Length, NativeArrayOptions.UninitializedMemory);
                events.CopyTo(receivedEvents.AsArray());
            }
        }
#endif
    }
}
