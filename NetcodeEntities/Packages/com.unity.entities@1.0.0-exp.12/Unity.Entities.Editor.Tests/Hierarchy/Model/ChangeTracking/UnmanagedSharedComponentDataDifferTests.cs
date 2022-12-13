using NUnit.Framework;
using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Unity.Entities.Editor.Tests
{
    [TestFixture]
    class UnmanagedSharedComponentDataDifferTests
    {
        World m_World;
        UnmanagedSharedComponentDataDiffer m_Differ;

        protected World World => m_World;

        [SetUp]
        public void Setup()
        {
            m_World = new World("TestWorld");
            m_Differ = new UnmanagedSharedComponentDataDiffer(typeof(EcsTestSharedComp));
        }

        [TearDown]
        public void TearDown()
        {
            m_World.Dispose();
            m_Differ.Dispose();
        }

        [Test]
        public void UnmanagedSharedComponentDataDiffer_Simple()
        {
            using var result = m_Differ.GatherComponentChangesAsync(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator, out var handle);
            handle.Complete();
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(0));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(0));
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectMissingEntityWhenDestroyed()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 1 });
            m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator).Dispose();

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.DestroyEntity(entityB);

            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(0));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(1));
            Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 1 })));
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectReplacedChunk()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            using (var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator))
            {
                Assert.That(result.AddedSharedComponentCount, Is.EqualTo(1));
                Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(0));
                Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 1 })));
            }

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.SetSharedComponentManaged(entityA, new EcsTestSharedComp { value = 2 });
            using (var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator))
            {
                Assert.That(result.AddedSharedComponentCount, Is.EqualTo(1));
                Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(1));
                Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 2 })));
                Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 1 })));
            }
        }

        [Test]
        public void UnmanagedSharedComponentDataDiffer_DetectNewEntity()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 2 });

            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(2));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(0));
            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 1 })));
            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(1), Is.EqualTo((entityB, new EcsTestSharedComp { value = 2 })));
        }

        [Test]
        public void UnmanagedSharedComponentDataDiffer_DetectEntityOnDefaultComponentValue()
        {
            var archetype = m_World.EntityManager.CreateArchetype(typeof(EcsTestSharedComp));
            var entityA = m_World.EntityManager.CreateEntity(archetype);

            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(1));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(0));
            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 0 })));
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectNewAndMissingEntityInExistingChunk()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 1 });

            m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator).Dispose();

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.RemoveComponent<EcsTestSharedComp>(entityB);
            using (var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator))
            {
                Assert.That(result.AddedSharedComponentCount, Is.EqualTo(0));
                Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(1));
                Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 1 })));
            }

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            var entityC = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityC, new EcsTestSharedComp { value = 1 });
            using (var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator))
            {
                Assert.That(result.AddedSharedComponentCount, Is.EqualTo(1));
                Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(0));
                Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityC, new EcsTestSharedComp { value = 1 })));
            }
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectMovedEntitiesAsNewAndRemoved()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 1 });

            m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator).Dispose();

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.RemoveComponent<EcsTestSharedComp>(entityA);
            var entityC = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityC, new EcsTestSharedComp { value = 1 });

            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(2));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(2));

            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 1 })));
            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(1), Is.EqualTo((entityC, new EcsTestSharedComp { value = 1 })));

            Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityA, new EcsTestSharedComp { value = 1 })));
            Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(1), Is.EqualTo((entityB, new EcsTestSharedComp { value = 1 })));
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectMissingChunk()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 2 });

            m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator).Dispose();

            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.DestroyEntity(entityB);
            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(0));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(1));
            Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 2 })));
        }

        [Test]
        public unsafe void UnmanagedSharedComponentDataDiffer_DetectEntityMovingFromOneChunkToAnother()
        {
            var entityA = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityA, new EcsTestSharedComp { value = 1 });
            var entityB = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddSharedComponentManaged(entityB, new EcsTestSharedComp { value = 2 });

            m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator).Dispose();
            m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            m_World.EntityManager.SetSharedComponentManaged(entityB, new EcsTestSharedComp { value = 1 });
            using var result = m_Differ.GatherComponentChanges(m_World.EntityManager, m_World.EntityManager.UniversalQuery, World.UpdateAllocator.ToAllocator);
            Assert.That(result.AddedSharedComponentCount, Is.EqualTo(1));
            Assert.That(result.RemovedSharedComponentCount, Is.EqualTo(1));
            Assert.That(result.GetAddedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 1 })));
            Assert.That(result.GetRemovedSharedComponent<EcsTestSharedComp>(0), Is.EqualTo((entityB, new EcsTestSharedComp { value = 2 })));
        }

        [Test]
        public void UnmanagedSharedComponentDataDiffer_ResultShouldThrowIfQueryWrongType()
        {
            var result = new SharedComponentDataDiffer.ComponentChanges(TypeManager.GetTypeIndex(typeof(EcsTestSharedComp)), default, default, default, default, default);

            var ex = Assert.Throws<InvalidOperationException>(() => result.GetAddedComponent<OtherSharedComponent>(0));
            Assert.That(ex.Message, Is.EqualTo($"Unable to retrieve data for component type {typeof(OtherSharedComponent)} (type index {TypeManager.GetTypeIndex<OtherSharedComponent>()}), this container only holds data for the type with type index {TypeManager.GetTypeIndex(typeof(EcsTestSharedComp))}."));
        }

        [Test]
        public void UnmanagedSharedComponentDataDiffer_CheckIfDifferCanWatchType()
        {
            Assert.That(SharedComponentDataDiffer.CanWatch(typeof(EcsTestData)), Is.False);
            Assert.That(SharedComponentDataDiffer.CanWatch(typeof(EcsTestSharedComp)), Is.True);
            Assert.That(SharedComponentDataDiffer.CanWatch(typeof(Entity)), Is.False);
        }

        struct OtherSharedComponent : ISharedComponentData
        {
#pragma warning disable 649
            public int SomethingElse;
#pragma warning restore 649
        }
    }
}
