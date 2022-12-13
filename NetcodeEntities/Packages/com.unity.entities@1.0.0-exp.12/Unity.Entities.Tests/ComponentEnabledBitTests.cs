using System;
using System.Collections.Generic;
#if !NET_DOTS && !UNITY_DOTSRUNTIME // DOTS Runtimes does not support regex
using System.Text.RegularExpressions;
#endif
using NUnit.Framework;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    partial class ComponentEnabledBitTests : ECSTestsFixture
    {
        [Test]
        public unsafe void IsComponentEnabled_NewEntities_IsTrue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType variant
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    foreach (var ent in entities)
                    {
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");
                    }
                }
                // Test generic interface
                foreach (var ent in entities)
                {
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent), $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be enabled, but isn't");
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(ent), $"Component {nameof(EcsTestDataEnableable2)} in Entity {ent} is should be enabled, but isn't");
                }

            }
        }

        [Test]
        public unsafe void IsComponentEnabled_ImmediatelyAfterSet_HasCorrectValue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType interface
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled(ent, t, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be disabled, but isn't");

                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled(ent, t, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");

                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }

                // Test generic interface
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype,
                        TypeManager.GetTypeIndex<EcsTestDataEnableable>());
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent),
                            $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be disabled, but isn't");
                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent),
                            $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be enabled, but isn't");
                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }
            }
        }

        [Test]
        public void RegressionTest6416()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),
                typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4));
            var ent = m_Manager.CreateEntity(archetype);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(ent, false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(ent, false);
            // This should not hit an assert while cloning the enabled bits
            Assert.DoesNotThrow(() => m_Manager.RemoveComponent(ent,
                new ComponentTypeSet(typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4))));
            // The bits should clone successfully
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent));
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(ent));
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public unsafe void IsComponentEnabled_ManagedComponent_NewEntities_IsTrue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponentEnableable),
                typeof(EcsTestManagedComponentEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType variant
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    foreach (var ent in entities)
                    {
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");
                    }
                }
                // Test generic interface
                foreach (var ent in entities)
                {
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestManagedComponentEnableable>(ent), $"Component {nameof(EcsTestManagedComponentEnableable)} in Entity {ent} is should be enabled, but isn't");
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestManagedComponentEnableable2>(ent), $"Component {nameof(EcsTestManagedComponentEnableable2)} in Entity {ent} is should be enabled, but isn't");
                }

            }
        }

        [Test]
        public unsafe void IsComponentEnabled_ManagedComponent_ImmediatelyAfterSet_HasCorrectValue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponentEnableable),
                typeof(EcsTestManagedComponentEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType interface
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled(ent, t, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be disabled, but isn't");

                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled(ent, t, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");

                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }

                // Test generic interface
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype,
                        TypeManager.GetTypeIndex<EcsTestManagedComponentEnableable>());
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled<EcsTestManagedComponentEnableable>(ent, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestManagedComponentEnableable>(ent),
                            $"Component {nameof(EcsTestManagedComponentEnableable)} in Entity {ent} is should be disabled, but isn't");
                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled<EcsTestManagedComponentEnableable>(ent, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestManagedComponentEnableable>(ent),
                            $"Component {nameof(EcsTestManagedComponentEnableable)} in Entity {ent} is should be enabled, but isn't");
                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }
            }
        }
#endif

        partial class DummySystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((in EcsTestDataEnableable2 testData2) => { }).Run();
            }
        }

        [Test]
        public unsafe void SetComponentEnabled_ChunkChangeVersion_IsChanged()
        {
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ent = m_Manager.CreateEntity(archetype);
            var typeIndex = TypeManager.GetTypeIndex(typeof(EcsTestDataEnableable));
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, typeIndex);
            var versionBefore = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            // Force a system update on unrelated entities to bump the global system version
            var ent2 = m_Manager.CreateEntity(typeof(EcsTestDataEnableable2));
            var sys = World.CreateSystemManaged<DummySystem>();
            sys.Update();
            var versionAfterUpdate = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            Assert.AreEqual(versionBefore, versionAfterUpdate, "Chunk's change version should be the same after unrelated system update");
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, false);
            var versionAfterSet = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            Assert.AreNotEqual(versionBefore, versionAfterSet, "Chunk's change version should be different after SetComponentEnabled()");
        }

        [Test]
        public unsafe void SetEnabled_ThrowsWithNonIEnableableComponent()
        {
            var type = ComponentType.ReadOnly<EcsTestData>();
            var archetype = m_Manager.CreateArchetype(type);
            var entity = m_Manager.CreateEntity(archetype);
            Assert.Throws<ArgumentException>(() =>
            {
                m_Manager.SetComponentEnabled(entity, type, false);
            });
        }

        static bool GetTestEntityShouldBeEnabled(int entityIndex, int chunkIndex)
        {
            return entityIndex % (2 + chunkIndex) == 0;
        }

        static unsafe void SetupChunkWithEnabledBits(ref EntityManager manager, ComponentType enableableType, Allocator allocator, out NativeArray<Entity> outEntities, out UnsafeParallelHashMap<Entity, bool> outMap, out EntityArchetype outArchetype, int chunkCount = 1, params ComponentType[] additionalTypes)
        {
            var types = new List<ComponentType>();
            types.Add(enableableType);
            types.Add(ComponentType.ReadOnly<EcsTestData>());
            types.AddRange(additionalTypes);

            outArchetype = manager.CreateArchetype(types.ToArray());

            outMap = new UnsafeParallelHashMap<Entity, bool>(outArchetype.ChunkCapacity * chunkCount, allocator);
            outEntities = manager.CreateEntity(outArchetype, outArchetype.ChunkCapacity * chunkCount, allocator);

            for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
            {
                for (int i = 0; i < outArchetype.ChunkCapacity; ++i)
                {
                    var entityIndex = chunkIndex * outArchetype.ChunkCapacity + i;
                    var value = GetTestEntityShouldBeEnabled(entityIndex, chunkIndex);
                    manager.SetComponentEnabled(outEntities[entityIndex], enableableType, value);
                    outMap.Add(outEntities[entityIndex], value);
                }
            }
        }

        static unsafe void CheckChunkDataAndMapConsistency(EntityManager manager, ComponentType enableableType, NativeArray<Entity> entities, UnsafeParallelHashMap<Entity, bool> map, int skipStartIndex = -1, int skipCount = 0)
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                if(skipStartIndex != -1 && i >= skipStartIndex && i < skipStartIndex + skipCount)
                    continue;

                var ecsIsComponentEnabled = manager.IsComponentEnabled(entities[i], enableableType);
                var mapIsComponentEnabled = map[entities[i]];

                Assert.AreEqual(mapIsComponentEnabled, ecsIsComponentEnabled);
            }
        }

        static unsafe void CheckChunkDataAndMapConsistency_WithRemapping(EntityManager dstManager, ComponentType enableableType, NativeArray<Entity> srcEntities, NativeArray<Entity> dstEntities, UnsafeParallelHashMap<Entity, bool> map, int skipIndex = -1)
        {
            for (int i = 0; i < srcEntities.Length; ++i)
            {
                if(skipIndex != -1 && i == skipIndex)
                    continue;

                var ecsIsComponentEnabled = dstManager.IsComponentEnabled(dstEntities[i], enableableType);
                var mapIsComponentEnabled = map[srcEntities[i]];

                Assert.AreEqual(mapIsComponentEnabled, ecsIsComponentEnabled);
            }
        }

        [Test]
        public void AddDataComponent_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddComponent<EcsTestData>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddTagComponent_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddComponent<EcsTestTag>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddTagComponent_Query_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());
            m_Manager.AddComponent<EcsTestTag>(query);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();

            query.Dispose();
        }

        [Test]
        public void AddBuffer_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddBuffer<EcsIntElement>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddSharedComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddSharedComponentManaged<EcsTestSharedComp>(entities[7], new EcsTestSharedComp(0));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void SetSharedComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount, typeof(EcsTestSharedComp));

            m_Manager.AddSharedComponentManaged<EcsTestSharedComp>(entities[7], new EcsTestSharedComp(10));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddChunkComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddChunkComponentData<EcsTestData2>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddChunkComponentData_Query_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddChunkComponentData<EcsTestData2>(m_Manager.UniversalQuery, new EcsTestData2(7));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void RemoveComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.RemoveComponent<EcsTestData>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void RemoveComponentData_Batched_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.RemoveComponent<EcsTestData>(entities);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void CreateEntity_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.CreateEntity(m_Manager.CreateArchetype(enableableType));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public unsafe void InstantiatePrefab_One_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var archetype = m_Manager.CreateArchetype(enableableType, ComponentType.ReadOnly<Prefab>());
            var prefabEntity = m_Manager.CreateEntity(archetype);

            m_Manager.SetComponentEnabled(prefabEntity, enableableType, false);
            var entity = m_Manager.Instantiate(prefabEntity);
            Assert.AreEqual(false, m_Manager.IsComponentEnabled(entity, enableableType));

            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public unsafe void InstantiatePrefab_Many_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var archetype = m_Manager.CreateArchetype(enableableType, ComponentType.ReadOnly<Prefab>());
            var prefabEntity = m_Manager.CreateEntity(archetype);

            m_Manager.SetComponentEnabled(prefabEntity, enableableType, false);
            using (var entities = m_Manager.Instantiate(prefabEntity, archetype.ChunkCapacity * chunkCount, m_Manager.World.UpdateAllocator.ToAllocator))
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual(false, m_Manager.IsComponentEnabled(entities[i], enableableType));
                }
            }

            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void DestroyEntityFromMiddleOfChunk_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var destroyIndex = 10;
            m_Manager.DestroyEntity(entities[destroyIndex]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyIndex, 1);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void DestroyEntityFromMiddleOfChunk_MultipleEntities_PreservesBitValues()
        {
            // Regression test for DOTS-4672
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using var entities = m_Manager.CreateEntity(archetype, 12, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[5], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[7], false);
            m_Manager.DestroyEntity(entities.GetSubArray(5, 4));
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void DestroyEntityFromEndOfChunk_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var destroyIndex = entities.Length - 1;
            m_Manager.DestroyEntity(entities[destroyIndex]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyIndex, 1);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void MoveEntitiesFrom_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var dstWorld = new World("CopyWorld");
            var dstManager = dstWorld.EntityManager;
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            dstManager.MoveEntitiesFrom(out var copyWorldEntities, m_Manager);
            CheckChunkDataAndMapConsistency_WithRemapping(dstManager, enableableType, entities, copyWorldEntities, map);

            m_Manager.Debug.CheckInternalConsistency();
            dstManager.Debug.CheckInternalConsistency();

            copyWorldEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public void CopyEntitiesFrom_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var dstWorld = new World("CopyWorld");
            var dstManager = dstWorld.EntityManager;
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var copyWorldEntities = new NativeArray<Entity>(entities.Length, Allocator.Persistent);
            dstManager.CopyEntitiesFrom(m_Manager, entities, copyWorldEntities);
            CheckChunkDataAndMapConsistency_WithRemapping(dstManager, enableableType, entities, copyWorldEntities, map);

            m_Manager.Debug.CheckInternalConsistency();
            dstManager.Debug.CheckInternalConsistency();

            copyWorldEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public void AddComponent_NewComponentIsEnableable_EnabledByDefault()
        {
            var ent = m_Manager.CreateEntity();

            // There have been enough bugs where only the first component gets their bits handled properly,
            // so this test now adds two components of each type.

            // basic components
            m_Manager.AddComponent<EcsTestDataEnableable2>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(ent));
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(ent));

            m_Manager.AddComponent<EcsTestDataEnableable>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent));
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent));

            // tag components
            m_Manager.AddComponent<EcsTestTagEnableable2>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestTagEnableable2>(ent));
            m_Manager.SetComponentEnabled<EcsTestTagEnableable2>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestTagEnableable2>(ent));

            m_Manager.AddComponent<EcsTestTagEnableable>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestTagEnableable>(ent));
            m_Manager.SetComponentEnabled<EcsTestTagEnableable>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestTagEnableable>(ent));

            // buffer components
            m_Manager.AddComponent<EcsIntElementEnableable2>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsIntElementEnableable2>(ent));
            m_Manager.SetComponentEnabled<EcsIntElementEnableable2>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsIntElementEnableable2>(ent));

            m_Manager.AddComponent<EcsIntElementEnableable>(ent);
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsIntElementEnableable>(ent));
            m_Manager.SetComponentEnabled<EcsIntElementEnableable>(ent, false);
            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsIntElementEnableable>(ent));
        }

        [Test]
        public unsafe void Serialization_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadOnly<EcsTestDataEnableable>(), ComponentType.ReadOnly<EcsTestData>());
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, World.UpdateAllocator.ToAllocator))
            {
                for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                {
                    for (int i = 0; i < archetype.ChunkCapacity; ++i)
                    {
                        var entityIndex = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndex, chunkIndex);
                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[entityIndex], value);
                    }
                }
            }

            // disposed via reader
            var writer = new TestBinaryWriter(World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            using (var deserializeWorld = new World("DeserializeWorld"))
            using (var deserializeQuery = deserializeWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>()))
            using (var reader = new TestBinaryReader(writer))
            {
                var deserializeManager = deserializeWorld.EntityManager;
                SerializeUtility.DeserializeWorld(deserializeWorld.EntityManager.BeginExclusiveEntityTransaction(), reader);
                deserializeManager.EndExclusiveEntityTransaction();

                var queryData = deserializeQuery._GetImpl()->_QueryData;
                Assert.AreEqual(1, queryData->MatchingArchetypes.Length);
                var newArchetype = queryData->MatchingArchetypes.Ptr[0]->Archetype;
                var chunks = newArchetype->Chunks;
                Assert.AreEqual(chunkCount, chunks.Count);

                for (int chunkIndex = 0; chunkIndex < chunks.Count; ++chunkIndex)
                {
                    var chunk = chunks[chunkIndex];
                    for (int i = 0; i < chunk->Count; ++i)
                    {
                        var entityIndexInQuery = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndexInQuery, chunkIndex);

                        var entityArray = (Entity*) chunk->Buffer;
                        Assert.AreEqual(value, deserializeManager.IsComponentEnabled<EcsTestDataEnableable>(entityArray[i]));
                    }

                }
                deserializeManager.Debug.CheckInternalConsistency();
            }
        }

        // Regression test for DOTS-5716
        [InternalBufferCapacity(512)]
        public struct EcsLargeBufferElement : IBufferElementData
        {
            public byte Value;
        }
        [Test]
        public unsafe void Serialization_LargeEntity_PreservesBitValues([Values(4)] int chunkCount)
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadOnly<EcsTestDataEnableable>(), ComponentType.ReadOnly<EcsLargeBufferElement>());
            Assert.LessOrEqual(archetype.ChunkCapacity, 64);
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, World.UpdateAllocator.ToAllocator))
            {
                for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                {
                    for (int i = 0; i < archetype.ChunkCapacity; ++i)
                    {
                        var entityIndex = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndex, chunkIndex);
                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[entityIndex], value);
                    }
                }
            }

            // disposed via reader
            var writer = new TestBinaryWriter(World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            using (var deserializeWorld = new World("DeserializeWorld"))
            using (var deserializeQuery = deserializeWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>()))
            using (var reader = new TestBinaryReader(writer))
            {
                var deserializeManager = deserializeWorld.EntityManager;
                SerializeUtility.DeserializeWorld(deserializeWorld.EntityManager.BeginExclusiveEntityTransaction(), reader);
                deserializeManager.EndExclusiveEntityTransaction();

                var queryData = deserializeQuery._GetImpl()->_QueryData;
                Assert.AreEqual(1, queryData->MatchingArchetypes.Length);
                var newArchetype = queryData->MatchingArchetypes.Ptr[0]->Archetype;
                var chunks = newArchetype->Chunks;
                Assert.AreEqual(chunkCount, chunks.Count);

                for (int chunkIndex = 0; chunkIndex < chunks.Count; ++chunkIndex)
                {
                    var chunk = chunks[chunkIndex];
                    for (int i = 0; i < chunk->Count; ++i)
                    {
                        var entityIndexInQuery = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndexInQuery, chunkIndex);

                        var entityArray = (Entity*) chunk->Buffer;
                        Assert.AreEqual(value, deserializeManager.IsComponentEnabled<EcsTestDataEnableable>(entityArray[i]));
                    }

                }
                deserializeManager.Debug.CheckInternalConsistency();
            }
        }

        [Test]
        public unsafe void ArchetypeStoresEnableableTypes()
        {
            var enableableTypeA = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var enableableTypeB = ComponentType.ReadOnly<EcsTestDataEnableable2>();
            var archetype = m_Manager.CreateArchetype(enableableTypeA, typeof(EcsTestData), typeof(EcsTestData2), enableableTypeB);

            Assert.AreEqual(6, archetype.Archetype->TypesCount); // listed types + Entity + Simulate
            Assert.AreEqual(3, archetype.Archetype->EnableableTypesCount); // Simulate + EcsTestDataEnableable + EcsTestDataEnableable2

            var types = archetype.Archetype->Types;
            Assert.AreEqual(enableableTypeA.TypeIndex, types[archetype.Archetype->EnableableTypeIndexInArchetype[0]].TypeIndex);
            Assert.AreEqual(enableableTypeB.TypeIndex, types[archetype.Archetype->EnableableTypeIndexInArchetype[1]].TypeIndex);
        }

        [Test]
        public void MoveChunkWithinArchetype_PreservesEnabledBits()
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, 4);

            using(var query = m_Manager.CreateEntityQuery(enableableType))
            using (var chunks = query.ToArchetypeChunkArray(World.UpdateAllocator.ToAllocator))
            {
                var entityType = m_Manager.GetEntityTypeHandle();
                var destroyChunkIndex = 1;
                m_Manager.DestroyEntity(chunks[destroyChunkIndex].GetNativeArray(entityType));

                var destroyStartIndex = archetype.ChunkCapacity * destroyChunkIndex;
                var destroyCount = archetype.ChunkCapacity;
                CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyStartIndex, destroyCount);

                m_Manager.Debug.CheckInternalConsistency();
            }
        }


        struct WriteBitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [NativeDisableContainerSafetyRestriction]public ComponentLookup<EcsTestDataEnableable> EnableableTypeRW;

            public void Execute(int index)
            {
                var entity = Entities[index];
                var setValue = index % 2 == 0;
                EnableableTypeRW.SetComponentEnabled(entity, setValue);
            }
        }

        [Test]
        public void ParallelWrites_PreservesMetadataCount([Values(1, 4)] int chunkCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, m_Manager.World.UpdateAllocator.ToAllocator))
            {
                new WriteBitsJob
                {
                    Entities = entities,
                    EnableableTypeRW = m_Manager.GetComponentLookup<EcsTestDataEnableable>(false)
                }.Schedule(entities.Length, 1, default).Complete();

                m_Manager.Debug.CheckInternalConsistency();
            }
        }

        [Test]
        public unsafe void EntityQuery_WithEnableableComponents_CreatesCorrectMatchingArchetypes()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var archetypeD = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4));

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(),},
                None = new ComponentType[0],
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(4, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(0, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);
                Assert.AreEqual(0, a->EnableableComponentsCount_Any);
                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);
                Assert.AreEqual(0, b->EnableableComponentsCount_Any);
                var c = matchingArchetypes.Ptr[2];
                Assert.AreEqual(0, c->EnableableComponentsCount_All);
                Assert.AreEqual(0, c->EnableableComponentsCount_None);
                Assert.AreEqual(0, c->EnableableComponentsCount_Any);
                var d = matchingArchetypes.Ptr[3];
                Assert.AreEqual(0, d->EnableableComponentsCount_All);
                Assert.AreEqual(0, d->EnableableComponentsCount_None);
                Assert.AreEqual(0, d->EnableableComponentsCount_Any);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestDataEnableable>(), },
                None = new ComponentType[0],
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(2, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(1, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);
                Assert.AreEqual(0, a->EnableableComponentsCount_Any);
                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(1, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);
                Assert.AreEqual(0, b->EnableableComponentsCount_Any);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(), },
                None = new [] {ComponentType.ReadOnly<EcsTestDataEnableable>()},
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(4, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(0, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);
                Assert.AreEqual(0, a->EnableableComponentsCount_Any);
                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(1, b->EnableableComponentsCount_None);
                Assert.AreEqual(0, b->EnableableComponentsCount_Any);
                var c = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, c->EnableableComponentsCount_All);
                Assert.AreEqual(1, c->EnableableComponentsCount_None);
                Assert.AreEqual(0, c->EnableableComponentsCount_Any);
                var d = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, d->EnableableComponentsCount_All);
                Assert.AreEqual(1, d->EnableableComponentsCount_None);
                Assert.AreEqual(0, d->EnableableComponentsCount_Any);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new ComponentType[0],
                       None = new ComponentType[0],
                       Any = new [] {ComponentType.ReadOnly<EcsTestDataEnableable2>(),
                           ComponentType.ReadOnly<EcsTestDataEnableable3>(),
                           ComponentType.ReadOnly<EcsTestDataEnableable4>(),}
                   }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(2, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(0, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);
                Assert.AreEqual(1, a->EnableableComponentsCount_Any);
                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);
                Assert.AreEqual(2, b->EnableableComponentsCount_Any);
            }

            // None on non-enableable types does not result in extra archetypes added to MatchingArchetypes
            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>(), },
                None = new [] {ComponentType.ReadOnly<EcsTestData>()},
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(0, matchingArchetypes.Length);
            }
        }

        private unsafe NativeArray<ArchetypeChunk> CreateChunks(ref EntityManager manager, EntityArchetype archetype, int chunkCount, Allocator allocator)
        {
            manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount);

            var ret = CollectionHelper.CreateNativeArray<ArchetypeChunk>(chunkCount, allocator);
            for (int i = 0; i < chunkCount; ++i)
            {
                ret[i] = new ArchetypeChunk(archetype.Archetype->Chunks[i], manager.GetCheckedEntityDataAccess()->EntityComponentStore);
            }

            return ret;
        }

        [Test]
        public unsafe void GetEnabledMask_OneEntityDisabled_HasExpectedMask()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(128, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, World.UpdateAllocator.ToAllocator))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 10, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                ChunkIterationUtility.GetEnabledMask(firstChunk.m_Chunk, matchingArchetype, out var chunkEnabledMask);
                Assert.AreEqual(0xFFFFFFFFFFFFFBFF, chunkEnabledMask.ULong0);
                Assert.AreEqual(0xFFFFFFFFFFFFFFFF, chunkEnabledMask.ULong1);
            }
        }

        static void SetBit(ref v128 mask, int index, bool value)
        {
            ulong bit = 1ul << (index % 64);
            int ulongIdx = index / 64;
            if (value)
            {
                if (ulongIdx == 0)
                    mask.ULong0 |= bit;
                else
                    mask.ULong1 |= bit;
            }
            else
            {
                if (ulongIdx == 0)
                    mask.ULong0 &= ~bit;
                else
                    mask.ULong1 &= ~bit;
            }

        }
        static bool GetBit(in v128 mask, int index)
        {
            ulong bit = 1ul << (index % 64);
            int ulongIdx = index / 64;
            ulong result = ulongIdx == 0 ? (mask.ULong0 & bit) : (mask.ULong1 & bit);
            return result != 0;
        }


        [BurstCompile(CompileSynchronously = true)]
        static void TestNextRange(int expectedBegin, int expectedCount, ref v128 mask, ref int2 state)
        {
            var res = EnabledBitUtility.GetNextRange(ref mask, ref state.x, ref state.y);

            Assert.AreEqual(expectedCount != 0, res);
            Assert.AreEqual(expectedBegin, state.x);
            Assert.AreEqual(expectedCount, state.y - state.x);
        }

        [BurstCompile(CompileSynchronously = true)]
        static void TestNextRangeDone(ref v128 mask, ref int2 state)
        {
            var res = EnabledBitUtility.GetNextRange(ref mask, ref state.x, ref state.y);

            Assert.AreEqual(state.y, state.x);
            Assert.IsFalse(res);
        }

        [Test]
        public unsafe void TzcntU128_Works()
        {
            v128 mask = default;

            for (int i = 0; i < 128; i++)
            {
                mask = default;
                SetBit(ref mask, i, true);
                Assert.AreEqual(i, EnabledBitUtility.tzcnt_u128(mask));
            }
            // test empty mask
            mask = default;
            Assert.AreEqual(128, EnabledBitUtility.tzcnt_u128(mask));
        }

        [Test]
        public unsafe void ShiftRight128_Works()
        {
            v128 mask;
            for (int i = 1; i < 128; i++)
            {
                mask = new v128(-1, -1);
                mask = EnabledBitUtility.ShiftRight(mask, i);
                for (int j = 0; j < 128; ++j)
                {
                    FastAssert.AreEqual((j < 128 - i), GetBit(mask, j));
                }
            }
            // test shift by 0
            mask = new v128(-1, -1, -1, 0x7FFFFFFF);
            mask = EnabledBitUtility.ShiftRight(mask, 0);
            FastAssert.AreEqual(-1, mask.SLong0);
            FastAssert.AreEqual(0x7FFFFFFFFFFFFFFF, mask.SLong1);
            // test shift by 128
            mask = new v128(-1, -1, -1, -1);
            mask = EnabledBitUtility.ShiftRight(mask, 128);
            FastAssert.AreEqual(0, mask.ULong0);
            FastAssert.AreEqual(0, mask.ULong1);
        }

        [Test]
        public unsafe void TestGetNextRange128()
        {
            v128 mask;
            int2 state;

            for (int i = 0; i < 128; i++)
            {
                state = default;
                mask = default;
                SetBit(ref mask, i, true);
                TestNextRange(i, 1, ref mask, ref state);
                TestNextRangeDone(ref mask, ref state);
            }

            state = default;
            mask = default;
            TestNextRangeDone(ref mask, ref state);

            state = default;
            mask = new v128(-1, -1);
            TestNextRange(0, 128, ref mask, ref state);
            TestNextRangeDone(ref mask, ref state);

            state = default;
            mask = default;
            SetBit(ref mask, 3, true);
            SetBit(ref mask, 4, true);
            SetBit(ref mask, 5, true);
            SetBit(ref mask, 6, true);
            SetBit(ref mask, 74, true);
            SetBit(ref mask, 75, true);
            SetBit(ref mask, 76, true);
            TestNextRange(3, 4, ref mask, ref state);
            TestNextRange(74, 3, ref mask, ref state);
            TestNextRangeDone(ref mask, ref state);

            state = default;
            mask = default;
            SetBit(ref mask, 3, true);
            SetBit(ref mask, 62, true);
            SetBit(ref mask, 63, true);
            SetBit(ref mask, 64, true);
            SetBit(ref mask, 65, true);
            SetBit(ref mask, 127, true);
            TestNextRange(3, 1, ref mask, ref state);
            TestNextRange(62, 4, ref mask, ref state);
            TestNextRange(127, 1, ref mask, ref state);
            TestNextRangeDone(ref mask, ref state);
        }

        [Test]
        public unsafe void GetEnabledMask_NoEnableableTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create less than a full chunk, to make sure that the final bits of the mask isn't set (there's no valid entities there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 4, World.UpdateAllocator.ToAllocator);

            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]{ typeof(EcsTestData),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // All valid entities should have their bits set
            Assert.AreEqual(0xFFFFFFFFFFFFFFFF, mask.ULong0);
            Assert.AreEqual(0x0FFFFFFFFFFFFFFF, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_OnlyAllTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create one less entity than a full chunk, to make sure that the final bit of the mask isn't set (there's no valid entity there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 1, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[0], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[126], false);

            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]{ typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // Any entity with 1+ components disabled should have its bit clear in the mask
            Assert.AreEqual(0xFFFFFFFFFFFFFFFC, mask.ULong0);
            Assert.AreEqual(0x3FFFFFFFFFFFFFFF, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_OnlyNoneTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create one less entity than a full chunk, to make sure that the final bit of the mask isn't set (there's no valid entity there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 1, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[0], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[126], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[126], false);

            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                None = new ComponentType[]{ typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // Any entity with both component disabled should have its bit set in the mask; all other bits should be off.
            Assert.AreEqual(0x0000000000000002, mask.ULong0);
            Assert.AreEqual(0x4000000000000000, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_OnlyAnyTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create one less entity than a full chunk, to make sure that the final bit of the mask isn't set (there's no valid entity there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 1, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[0], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[1], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[65], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[126], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[126], false);

            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new ComponentType[]{ typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // Any entity with either component enabled should have its bit set
            Assert.AreEqual(0xFFFFFFFFFFFFFFFD, mask.ULong0);
            Assert.AreEqual(0x3FFFFFFFFFFFFFFF, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_AllAndNoneTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),
                typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create less than a full chunk, to make sure that the final bits of the mask isn't set (there's no valid entities there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 4, World.UpdateAllocator.ToAllocator);
            // Only entities with 1 AND 2 enabled, and 3 and 4 disabled (variety = 0xC) should have their bits set in the final mask.
            for (int i = 0; i < entities.Length; ++i)
            {
                int variety = i % 16;
                if ((variety & 0x1) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
                if ((variety & 0x2) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[i], false);
                if ((variety & 0x4) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable3>(entities[i], false);
                if ((variety & 0x8) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(entities[i], false);
            }
            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]{ typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),},
                None = new ComponentType[]{ typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // Bits should be set for entities with components 1 and 2 enabled, and 3 and 4 disabled.
            // In every group of 16 bits, the 12th bit should be set. The high 4 bits should be clear, since there's no
            // entities there.
            Assert.AreEqual(0x1000100010001000, mask.ULong0);
            Assert.AreEqual(0x0000100010001000, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_AllNoneAnyTypes_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2),
                typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            // Create less than a full chunk, to make sure that the final bits of the mask isn't set (there's no valid entities there)
            using var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity - 32, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entities.Length; ++i)
            {
                int variety = i % 16;
                if ((variety & 0x1) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
                if ((variety & 0x2) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(entities[i], false);
                if ((variety & 0x4) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable3>(entities[i], false);
                if ((variety & 0x8) != 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(entities[i], false);
            }
            using var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]{ typeof(EcsTestDataEnableable),},
                None = new ComponentType[]{ typeof(EcsTestDataEnableable2),},
                Any = new ComponentType[]{ typeof(EcsTestDataEnableable3), typeof(EcsTestDataEnableable4),},
            });
            var chunk = archetype.Archetype->Chunks[0];
            var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
            ChunkIterationUtility.GetEnabledMask(chunk, matchingArchetype, out var mask);
            // Bits should be set for entities with components 1 enabled, 2 disabled, and either 3 or 4 enabled.
            // In every group of 16 bits, the 12th bit should be set.
            // The high 32 bits should be clear, since there's no entities there.
            Assert.AreEqual(0x0444044404440444, mask.ULong0);
            Assert.AreEqual(0x0000000004440444, mask.ULong1);
        }

        [Test]
        public unsafe void GetEnabledMask_QueryNone_HasExpectedMask()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(128, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>()},
                None = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>()}
            }))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, World.UpdateAllocator.ToAllocator))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);
                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 0, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 32, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 33, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 34, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                ChunkIterationUtility.GetEnabledMask(firstChunk.m_Chunk, matchingArchetype, out var mask);

                Assert.AreEqual(0x0000000700000001, mask.ULong0);
                Assert.AreEqual(0x0000000000000000, mask.ULong1);
            }
        }

        [Test]
        public void ReleaseChunk_PreservesEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestDataEnableable));
            int entityCount = 2;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            m_Manager.SetSharedComponentManaged(entities[0], new EcsTestSharedComp { value = 17 });
            Assert.DoesNotThrow(() => m_Manager.Debug.CheckInternalConsistency());
            // Setting the shared component value of the remaining entity leaves its current chunk empty.
            // If the subsequent ReleaseChunk() operation doesn't correctly preserve enabled bits state, this will cause
            // an internal consistency error.
            m_Manager.SetSharedComponentManaged(entities[1], new EcsTestSharedComp { value = 17 });
            Assert.DoesNotThrow(() => m_Manager.Debug.CheckInternalConsistency());
        }

        [Test]
        public void EntityManager_SetSharedComponentData_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                m_Manager.SetSharedComponentManaged(entities[i], new EcsTestSharedComp { value = 17 });
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.SetSharedComponentManaged(query, new EcsTestSharedComp { value = 23 });

            for (int i = 0; i < entityCount; ++i)
            {
                int expectedValue = (i % 100) == 0 ? 17 : 23;
                Assert.AreEqual(expectedValue, m_Manager.GetSharedComponentManaged<EcsTestSharedComp>(entities[i]).value);
            }
        }

        [Test]
        public void EntityManager_AddComponent_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.AddComponent(query, ComponentType.ReadWrite<EcsTestData>());

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? false : true;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData>(entities[i]));
            }
        }

        [Test]
        public void EntityManager_AddComponents_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.AddComponent(query, new ComponentTypeSet(typeof(EcsTestData), typeof(EcsTestData2)));

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? false : true;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData>(entities[i]));
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData2>(entities[i]));
            }
        }

        [Test]
        public void EntityManager_AddComponentDataWithValues_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            var valueList = new NativeList<EcsTestData>(entityCount, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
                else
                    valueList.Add(new EcsTestData { value = i });
            }

            m_Manager.AddComponentData(query, valueList.AsArray());
            valueList.Dispose();

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? false : true;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData>(entities[i]));
                if (shouldHaveComponent)
                    Assertions.Assert.AreEqual(i, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
            }
        }

        [Test]
        public void EntityManager_RemoveComponent_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.RemoveComponent(query, ComponentType.ReadWrite<EcsTestData>());

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? true : false;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData>(entities[i]));
            }
        }

        [Test]
        public void EntityManager_RemoveComponents_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.RemoveComponent(query, new ComponentTypeSet(typeof(EcsTestData), typeof(EcsTestData2)));

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? true : false;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData>(entities[i]));
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestData2>(entities[i]));
            }
        }

        [Test]
        public void EntityManager_AddSharedComponentData_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.AddSharedComponentManaged(query, new EcsTestSharedComp { value = 23 });

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldHaveComponent = (i % 100) == 0 ? false : true;
                Assert.AreEqual(shouldHaveComponent, m_Manager.HasComponent<EcsTestSharedComp>(entities[i]));
                if (shouldHaveComponent)
                    Assert.AreEqual(23, m_Manager.GetSharedComponentManaged<EcsTestSharedComp>(entities[i]).value);
            }
        }

        [Test]
        public void EntityManager_DestroyEntity_EntityQuery_RespectsEnabledBits()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            int entityCount = 1000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable));
            for (int i = 0; i < entityCount; ++i)
            {
                if ((i % 100) == 0)
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
            }

            m_Manager.DestroyEntity(query);

            for (int i = 0; i < entityCount; ++i)
            {
                bool shouldExist = (i % 100) == 0 ? true : false;
                Assert.AreEqual(shouldExist, m_Manager.Exists(entities[i]));
            }
        }

        [Test]
        public unsafe void EntityQuery_MultipleArchetypeQueriesWithEnableableTypes_StoresAllUnique()
        {
            using var query = m_Manager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new ComponentType[]{typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2)},
                    None = new ComponentType[]{typeof(EcsTestDataEnableable4)},
                },
                new EntityQueryDesc{
                    All = new ComponentType[]{typeof(EcsTestDataEnableable3)},
                    None = new ComponentType[]{typeof(EcsTestDataEnableable2), typeof(EcsTestDataEnableable4)},
                });
            var queryData = query._GetImpl()->_QueryData;
            var expectedTypeIndices = new TypeIndex[] {
                        TypeManager.GetTypeIndex<EcsTestDataEnableable>(),
                        TypeManager.GetTypeIndex<EcsTestDataEnableable2>(),
                        TypeManager.GetTypeIndex<EcsTestDataEnableable3>(),
                        TypeManager.GetTypeIndex<EcsTestDataEnableable4>(),
                    };
            var actualTypeIndices =
                CollectionHelper.CreateNativeArray<TypeIndex>(queryData->EnableableComponentTypeIndexCount, Allocator.Temp);
            for (int i = 0; i < queryData->EnableableComponentTypeIndexCount; ++i)
            {
                actualTypeIndices[i] = queryData->EnableableComponentTypeIndices[i];
            }

            CollectionAssert.AreEquivalent(expectedTypeIndices, actualTypeIndices.ToArray());
        }

        [Test]
        public void EntityQuery_MultipleArchetypeQueries_ThrowIfDifferentIgnoreEnabledBitsFlags()
        {
            Assert.Throws<ArgumentException>(() => m_Manager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new ComponentType[]{typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2)},
                    None = new ComponentType[]{typeof(EcsTestDataEnableable4)},
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                },
                new EntityQueryDesc{
                    All = new ComponentType[]{typeof(EcsTestDataEnableable3)},
                    None = new ComponentType[]{typeof(EcsTestDataEnableable2), typeof(EcsTestDataEnableable4)},
                    Options = default,
                }));
        }

        [Test]
        public void EntityQuery_IgnoreComponentEnabledState_Works()
        {
            using var queryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EcsTestDataEnableable>()
                .WithAny<EcsTestDataEnableable2,EcsTestDataEnableable3>()
                .WithNone<EcsTestDataEnableable4>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
            using var query = m_Manager.CreateEntityQuery(queryBuilder);
            var archetypeMissingExcluded = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var archetypeWithAll = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2), typeof(EcsTestDataEnableable4));
            var archetypeMissingRequired = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable2), typeof(EcsTestDataEnableable4));
            var archetypeMissingOptional = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable2), typeof(EcsTestDataEnableable4));
            // These entities match the required/excluded components as-is
            using var entities1 = m_Manager.CreateEntity(archetypeMissingExcluded, 2, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities1[1], false);
            // These entities would match the query if their EcsTestDataEnableable4 were disabled
            using var entities2 = m_Manager.CreateEntity(archetypeWithAll, 3, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities2[2], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(entities2[1], false);
            // These entities can never match the query; they're missing the required component EcsTestDataEnableable
            using var entities3 = m_Manager.CreateEntity(archetypeMissingRequired, 5, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(entities3[1], false);
            // These entities can never match the query; they're missing the optional components EcsTestDataEnableable2 and 3
            using var entities4 = m_Manager.CreateEntity(archetypeMissingOptional, 7, World.UpdateAllocator.ToAllocator);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable4>(entities3[1], false);

            var expectedMatches = new[] { entities1[0], entities1[1], entities2[0], entities2[1], entities2[2] };
            CollectionAssert.AreEquivalent(expectedMatches, query.ToEntityArray(Allocator.Temp).ToArray());
        }

#if !NET_DOTS && !UNITY_DOTSRUNTIME // DOTS Runtimes does not support regex
        struct DataJob_WriteBits_ComponentLookup : IJobChunk
        {
            [ReadOnly]public ComponentLookup<EcsTestDataEnableable> EnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                EnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct BufferJob_WriteBits_BufferDataFromEntity : IJobChunk
        {
            [ReadOnly]public BufferLookup<EcsIntElementEnableable> EnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                EnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct DataJob_WritesBitsToNonEnableable : IJobChunk
        {
            public ComponentLookup<EcsTestData> NonEnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                NonEnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct BufferJob_WritesBitsToNonEnableable : IJobChunk
        {
            public BufferLookup<EcsIntElement> NonEnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                NonEnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        [Test]
        public void WritingBitsToReadOnlyData_TriggersSafetySystem_ComponentLookup()
        {
            m_Manager.CreateEntity(typeof(EcsTestDataEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new DataJob_WriteBits_ComponentLookup{EntityType = m_Manager.GetEntityTypeHandle(), EnableableType = m_Manager.GetComponentLookup<EcsTestDataEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsToReadOnlyBuffer_TriggersSafetySystem_ComponentLookup()
        {
            m_Manager.CreateEntity(typeof(EcsIntElementEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElementEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new BufferJob_WriteBits_BufferDataFromEntity(){EntityType = m_Manager.GetEntityTypeHandle(), EnableableType = m_Manager.GetBufferLookup<EcsIntElementEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsForNonEnableableDataType_Throws()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestData>());

            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"));
            new DataJob_WritesBitsToNonEnableable{EntityType = m_Manager.GetEntityTypeHandle(), NonEnableableType = m_Manager.GetComponentLookup<EcsTestData>(false)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsForNonEnableableBufferType_Throws()
        {
            m_Manager.CreateEntity(typeof(EcsIntElement));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElement>());

            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"));
            new BufferJob_WritesBitsToNonEnableable{EntityType = m_Manager.GetEntityTypeHandle(), NonEnableableType = m_Manager.GetBufferLookup<EcsIntElement>(false)}.Run(queryRO);
        }

        struct DataJob_WritesBits_ArchetypeChunk : IJobChunk
        {
            [ReadOnly]public ComponentTypeHandle<EcsTestDataEnableable> EnableableType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                chunk.SetComponentEnabled(EnableableType, 0, true);
            }
        }

        struct BufferJob_WritesBits_ArchetypeChunk : IJobChunk
        {
            [ReadOnly]public BufferTypeHandle<EcsIntElementEnableable> EnableableType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                chunk.SetComponentEnabled(EnableableType, 0, true);
            }
        }

        [Test]
        public void WritingBitsToReadOnlyData_TriggersSafetySystem_ArchetypeChunk()
        {
            m_Manager.CreateEntity(typeof(EcsTestDataEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new DataJob_WritesBits_ArchetypeChunk(){ EnableableType = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsToReadOnlyBuffer_TriggersSafetySystem_ArchetypeChunk()
        {
            m_Manager.CreateEntity(typeof(EcsIntElementEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElementEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new BufferJob_WritesBits_ArchetypeChunk(){ EnableableType = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(true)}.Run(queryRO);
        }
#endif
    }
}
