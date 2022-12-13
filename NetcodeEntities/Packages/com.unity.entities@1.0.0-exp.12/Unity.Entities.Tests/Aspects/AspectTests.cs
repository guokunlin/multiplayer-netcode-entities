using System;
using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Tests.Aspects.FunctionalTests;
using static Unity.Entities.SystemAPI;

namespace Unity.Entities.Tests
{
    readonly internal partial struct MyAspect : IAspect
    {
        public readonly RefRW<EcsTestData> _Data;
        [Optional] public readonly RefRW<EcsTestData2> _Data2;

        public void Read()
        {
            var res = _Data.ValueRO;
        }

        public void ReadAssertWrittenValue()
        {
            Assert.AreEqual(10, _Data.ValueRW.value);
        }

        public void Write()
        {
            _Data.ValueRW.value = 10;
        }

        public EcsTestData Data
        {
            get => _Data.ValueRO;
            set => _Data.ValueRW = value;
        }

        public static Entity CreateEntity(EntityManager manager, bool hasData2)
        {
            var e = manager.CreateEntity(typeof(EcsTestData));
            if (hasData2)
                manager.CreateEntity(typeof(EcsTestData2));
            return e;
        }
    }

    public struct MyBufferElement : IBufferElementData
    {
        public int Value;
    }


    /// <summary>
    /// This aspect uses many different features that triggers the source generator in multiple ways.
    /// It aims to test the aspect source generator.
    /// </summary>
    internal readonly partial struct MyAspectMiscTests : IAspect
    {
        public readonly Entities.Entity Entity;

        // Test: component type should be correctly without any qualifiers
        public readonly RefRW<EcsTestData> Data;

        // Test: component type should be correctly handled with qualifiers
        public readonly RefRW<Unity.Entities.Tests.EcsTestData2> Data2;

        // Test: component type should be correctly handled with the 'global' qualifier
        public readonly RefRW<global::Unity.Entities.Tests.EcsTestData3> Data3;

        // Test: field declared with the [Unity.Collections.ReadOnly] must be read-only in the constructed entity query
        public readonly RefRO<EcsTestData4> DataRO;

        // Test: field declared with the [Unity.Collections.ReadOnly] must be read-only in the constructed entity query
        [Optional]
        public readonly RefRW<EcsTestData5> DataOptional;

        public const int k_InitialValue = 123;
        public const int k_InitialValueSum = k_InitialValue * 4;
        public const int k_InitialValueSumWithOptional = k_InitialValue * 5;

        public const int k_WriteValue = 456;
        public const int k_WriteValueSum = k_WriteValue * 4;
        public const int k_WriteValueSumWithOptional = k_WriteValueSum + k_InitialValue;
        public static Entity CreateEntity(EntityManager manager, bool hasDataOptional)
        {
            var e = manager.CreateEntity();
            manager.AddComponentData(e, new EcsTestData(k_InitialValue));
            manager.AddComponentData(e, new EcsTestData2(k_InitialValue));
            manager.AddComponentData(e, new EcsTestData3(k_InitialValue));
            manager.AddComponentData(e, new EcsTestData4(k_InitialValue));
            if (hasDataOptional)
                manager.AddComponentData(e, new EcsTestData5(k_InitialValue));
            return e;
        }
    }

    // Test: the aspect generator must support multiple partial declaration of the same aspect.
    internal readonly partial struct MyAspectMiscTests : global::Unity.Entities.IAspect
    {
        public int ReadSum()
        {
            var v = Data.ValueRO.value +
                Data2.ValueRO.value0 +
                Data3.ValueRO.value0 +
                DataRO.ValueRO.value0;
            if (DataOptional.IsValid)
            {
                v += DataOptional.ValueRO.value0;
            }
            return v;
        }
        public void WriteAll(int v)
        {
            Data.ValueRW.value = v;
            Data2.ValueRW.value0 = v;
            Data3.ValueRW.value0 = v;
            if (DataOptional.IsValid)
            {
                DataOptional.ValueRW.value0 = v;
            }
        }
    }

    public readonly partial struct NestingAspectOnlyAspects : IAspect
    {
        [ReadOnly]
        readonly internal MyAspectMiscTests MiscTests;
        readonly internal MyAspect MyAspect;
    }

    public readonly partial struct NestingAspectMixed : IAspect
    {
        readonly internal MyAspectMiscTests MiscTests;
        readonly internal RefRW<EcsTestData> Data;
    }

    // Test: an aspect declared with the [DisableGeneration] attribute must not be generated.
    [DisableGeneration]
    internal readonly partial struct AspectDisableGeneration : IAspect, IAspectCreate<AspectDisableGeneration>
    {
        public AspectDisableGeneration CreateAspect(Entity entity, ref SystemState system, bool isReadOnly) => default;
        public readonly RefRW<EcsTestData> Data;
    }

    internal readonly partial struct AspectWithManualCreate : IAspect, IAspectCreate<AspectWithManualCreate>
    {
        public AspectWithManualCreate CreateAspect(Entity entity, ref SystemState system, bool isReadOnly) => default;
        public readonly RefRW<EcsTestData> Data;
    }

    internal readonly partial struct AspectWithoutManualCreate : IAspect
    {
        public readonly RefRW<EcsTestData> Data;
    }
#pragma warning disable

    public static class ArchetypeChunkExt
    {
        public static int VisitAllOfComponent<ComponentT>(this Unity.Entities.ArchetypeChunk chunk, Unity.Entities.EntityManager manager, Action<ComponentT> visitor)
            where ComponentT : unmanaged, IComponentData
        {
            var dataTypeHandle = manager.GetComponentTypeHandle<ComponentT>(true);
            var data = chunk.GetNativeArray(dataTypeHandle);
            for (int i = 0; i != data.Length; ++i)
                visitor(data[i]);
            return data.Length;
        }
    }

    namespace MyNamespace
    {
        // type shadowing test
        struct Entity { }
        struct SystemBase { }
        struct NativeArray<T> { }
        struct SystemState { }
        struct EntityQueryEnumerator { }
        internal readonly partial struct MyAspectTestTypeShadowing : IAspect
        {
            public readonly RefRW<EcsTestData> Data;
        }
    }
    partial class AspectTests : AspectFunctionalTest
    {
        public unsafe T GetAspect<T>(Entity entity) where T : struct, IAspect, IAspectCreate<T>
        {
            T aspect = default;
            return aspect.CreateAspect(entity, ref EmptySystem.CheckedStateRef, false);
        }
        partial class AspectEntityCountTestSystem : SystemBase
        {
            public int Count;
            protected override void OnUpdate()
            {
                int c = 0;
                Entities.ForEach((in MyAspect test) =>
                {
                    ++c;
                }).Run();
                Count = c;
            }
        }

        [Test]
        public void Query()
        {
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData2));
            m_Manager.CreateEntity(typeof(EcsTestData3));

            var test = World.GetOrCreateSystemManaged<AspectEntityCountTestSystem>();
            test.Update();

            // there should be exactly 4 entities with the aspect
            Assert.AreEqual(4, test.Count);
        }

        partial class NestedAspectEntityCountTestSystem : SystemBase
        {
            public int Count;
            protected override void OnUpdate()
            {
                int c = 0;
                int res = 0;
                Entities.ForEach((ref NestingAspectOnlyAspects only, in NestingAspectMixed test) =>
                {
                    only.MyAspect.Write();
                    res = test.MiscTests.ReadSum();
                    c++;
                }).Run();

                Assert.AreEqual(10, res);
                Count = c;
            }
        }

        [Test]
        public void NestedAspectQuery()
        {
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestData4));
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData2));
            m_Manager.CreateEntity(typeof(EcsTestData3));

            var test = World.GetOrCreateSystemManaged<NestedAspectEntityCountTestSystem>();
            test.Update();

            Assert.AreEqual(1, test.Count);
        }

        [Test]
        public void NestedAspectTypesReturnsCorrectRequiredComponents()
        {
            var expected = new[]
            {
                ComponentType.ReadWrite<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>(),
                ComponentType.ReadOnly<EcsTestData3>(), ComponentType.ReadOnly<EcsTestData4>()
            };

            CollectionAsserts.CompareSorted(expected, NestingAspectOnlyAspects.RequiredComponents);

            var expectedRO = new[]
            {
                ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>(),
                ComponentType.ReadOnly<EcsTestData3>(), ComponentType.ReadOnly<EcsTestData4>()
            };
            CollectionAsserts.CompareSorted(expectedRO, NestingAspectOnlyAspects.RequiredComponentsRO);
        }

        [Test]
        public void StructuralChangeSafety()
        {
            var entity = MyAspect.CreateEntity(m_Manager, false);

            var aspect = GetAspect<MyAspect>(entity);

            // Structural change, invalidates pointer and thus you can't keep an aspect beyond structural changes
            m_Manager.AddComponentData(entity, new EcsTestData3());
            Assert.Throws<ObjectDisposedException>(() => Debug.Log(aspect.Data));

            // Fetching it again fixes it
            aspect = GetAspect<MyAspect>(entity);
            Assert.AreEqual(0, aspect.Data.value);
        }

        [Test]
        public void DisallowGettingInvalidAspect()
        {
            var entity = m_Manager.CreateEntity();
            Assert.Throws<ArgumentException>(() => GetAspect<MyAspect>(entity));
            Assert.Throws<ArgumentException>(() => GetAspect<MyAspect>(Entity.Null));
        }

        [Test]
        public unsafe void WithAndWithoutManualAspectCreate()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            var withManualCreate = GetAspect<AspectWithManualCreate>(entity);
            var withoutManualCreate = GetAspect<AspectWithoutManualCreate>(entity);

            var defaultWith = default(AspectWithManualCreate);
            var defaultWithout = default(AspectWithoutManualCreate);

            // Can't do Assert.That(with, Is.EqualTo(default(AspectWithManualCreate))); as coreclr (with .Net5 and under) returns false on pointer compare.
            Assert.True(0 == UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref withManualCreate), UnsafeUtility.AddressOf(ref defaultWith), UnsafeUtility.SizeOf<AspectWithManualCreate>()));
            Assert.True(0 != UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref withoutManualCreate), UnsafeUtility.AddressOf(ref defaultWithout), UnsafeUtility.SizeOf<AspectWithoutManualCreate>()));
        }

        partial class TestSystem : SystemBase
        {
            public AccessKind AccessKind;
            public ContextKind ContextKind;
            protected override void OnUpdate()
            {
                switch (AccessKind)
                {
                    case AccessKind.ReadOnlyAccess:
                        switch (ContextKind)
                        {
                            case ContextKind.GetAspect:
                                Entities.ForEach((Entity entity) => GetAspectRO<MyAspect>(entity).Read()).Run();
                                break;
                        }
                        break;
                    case AccessKind.ReadWriteAccess:
                        switch (ContextKind)
                        {
                            case ContextKind.GetAspect:
                                Entities.ForEach((Entity entity) => { GetAspectRW<MyAspect>(entity).Write(); }).Run();
                                break;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// AccessKind | ContextKind | Test Result
        /// -------------------------------------------------------------------------------------------------
        /// ReadOnly   | InEFEParam  | oldVersion == newVersion
        /// ReadOnly   | GetAspect   | oldVersion == newVersion
        /// ReadWrite  | InEFEParam  | oldVersion != newVersion
        /// ReadWrite  | GetAspect   | oldVersion != newVersion
        /// </summary>
        /// <param name="accessKind"></param>
        /// <param name="contextKind"></param>
        [Test]
        public void ReadOnlyChangeVersion([Values] AccessKind accessKind, [Values] ContextKind contextKind)
        {
            // check if the current permutation is supported. This test should be converted to an AspectFeatureTest.
            if (!MakeUseCase(default, SystemKind.SystemBase, contextKind, accessKind).IsSupported)
                Assert.Ignore();

            var entity = MyAspect.CreateEntity(m_Manager, false);
            var test = World.GetOrCreateSystemManaged<TestSystem>();

            var oldVersion = m_Manager.GetChunk(entity).GetChangeVersion(m_Manager.GetComponentTypeHandle<EcsTestData>(true));

            m_Manager.Debug.SetGlobalSystemVersion(oldVersion + 10);
            test.ContextKind = contextKind;
            test.AccessKind = accessKind;
            test.Update();

            var newVersion = m_Manager.GetChunk(entity).GetChangeVersion(m_Manager.GetComponentTypeHandle<EcsTestData>(true));
            switch (accessKind)
            {
                case AccessKind.ReadOnlyAccess:
                    Assert.AreEqual(oldVersion, newVersion);
                    break;
                case AccessKind.ReadWriteAccess:
                    Assert.AreNotEqual(oldVersion, newVersion);
                    break;
            }
        }

        partial class ReadWriteTestSystem : SystemBase
        {
            public AccessKind AccessKind;
            public ContextKind ContextKind;
            public OperationKind OperationKind;

            protected override void OnUpdate()
            {
                var operationKind = OperationKind;
                switch (operationKind)
                {
                    case OperationKind.Read:
                        switch (AccessKind)
                        {
                            case AccessKind.ReadOnlyAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRO<MyAspect>(entity).Read()).Run();

                                break;
                            case AccessKind.ReadWriteAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRW<MyAspect>(entity).Read()).Run();

                                break;
                        }
                        break;
                    case OperationKind.Write:
                        switch (AccessKind)
                        {
                            case AccessKind.ReadOnlyAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRO<MyAspect>(entity).Write()).Run();

                                break;
                            case AccessKind.ReadWriteAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRW<MyAspect>(entity).Write()).Run();

                                break;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// AccessKind | ContextKind | OperationKind | Test Result
        /// -----------------------------------------------------------------------
        /// ReadOnly   | GetAspect   | Read          | all components value == 0
        /// ReadOnly   | GetAspect   | Write         | Throw Exception
        /// ReadWrite  | GetAspect   | Read          | all components value == 0
        /// ReadWrite  | GetAspect   | Write         | all components value == 10
        /// </summary>
        /// <param name="accessKind"></param>
        /// <param name="contextKind"></param>
        /// <param name="operationKind"></param>
        [Test]
        public void ReadWriteAccess([Values] AccessKind accessKind, [Values] ContextKind contextKind, [Values] OperationKind operationKind)
        {
            // check if the current permutation is supported. This test should be converted to an AspectFeatureTest.
            if (!MakeUseCase(default, SystemKind.SystemBase, contextKind, accessKind).IsSupported)
                Assert.Ignore();

            // expect an exception if we try to write while being in read-only mode
            bool readOnlyException = accessKind == AccessKind.ReadOnlyAccess && operationKind == OperationKind.Write;

            var entity = MyAspect.CreateEntity(m_Manager, false);
            var test = World.GetOrCreateSystemManaged<ReadWriteTestSystem>();
            test.OperationKind = operationKind;
            test.AccessKind = accessKind;
            test.ContextKind = contextKind;

            if (readOnlyException)
                Assert.Throws<System.InvalidOperationException>(delegate { test.Update(); });
            else
                test.Update();

            var dataTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var data = m_Manager.GetChunk(entity).GetNativeArray(dataTypeHandle);
            for (int i = 0; i != data.Length; ++i)
            {
                Assert.AreEqual((operationKind == OperationKind.Read || accessKind == AccessKind.ReadOnlyAccess) ? 0 : 10, data[i].value);
            }

        }

        partial class MyAspectMiscTestsSystem : SystemBase
        {
            public AccessKind AccessKind;
            public ContextKind ContextKind;
            public OperationKind OperationKind;
            public int TestValue = 0;

            protected override void OnUpdate()
            {
                var operationKind = OperationKind;
                var value = TestValue;
                switch (operationKind)
                {
                    case OperationKind.Read:
                        switch (AccessKind)
                        {
                            case AccessKind.ReadOnlyAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => value = GetAspectRO<MyAspectMiscTests>(entity).ReadSum()).Run();

                                break;
                            case AccessKind.ReadWriteAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => value = GetAspectRW<MyAspectMiscTests>(entity).ReadSum()).Run();

                                break;
                        }
                        TestValue = value;
                        break;
                    case OperationKind.Write:
                        switch (AccessKind)
                        {
                            case AccessKind.ReadOnlyAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRO<MyAspectMiscTests>(entity).WriteAll(value)).Run();
                                break;
                            case AccessKind.ReadWriteAccess:
                                if (ContextKind == ContextKind.GetAspect)
                                    Entities.ForEach((Entity entity) => GetAspectRW<MyAspectMiscTests>(entity).WriteAll(value)).Run();

                                break;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// AccessKind | ContextKind | OperationKind | OptionalKind | Test Result
        /// --------------------------------------------------------------------------------------------------------------------------------
        /// ReadOnly   | GetAspect   | Read          | With         | TestValue == k_InitialValueSumWithOptional
        /// ReadOnly   | GetAspect   | Read          | No           | TestValue == k_InitialValueSum
        /// ReadOnly   | GetAspect   | Write         | With         | Throw Exception
        /// ReadOnly   | GetAspect   | Write         | No           | Throw Exception
        /// ReadWrite  | GetAspect   | Read          | With         | all components value == k_WriteValue, including optional components
        /// ReadWrite  | GetAspect   | Read          | No           | all components value == k_WriteValue
        /// ReadWrite  | GetAspect   | Write         | With         | all components value == k_WriteValue, including optional components
        /// ReadWrite  | GetAspect   | Write         | No           | all components value == k_WriteValue
        /// </summary>
        /// <param name="accessKind"></param>
        /// <param name="contextKind"></param>
        /// <param name="operationKind"></param>
        /// <param name="optionalKind"></param>
        [Test]
        public void MiscReadWriteTests([Values] AccessKind accessKind, [Values] ContextKind contextKind, [Values] OperationKind operationKind, [Values] OptionalKind optionalKind)
        {
            // check if the current permutation is supported. This test should be converted to an AspectFeatureTest.
            if (!MakeUseCase(default, SystemKind.SystemBase, contextKind, accessKind).IsSupported)
                Assert.Ignore();

            // expect an exception if we try to write while being in read-only mode
            bool readOnlyException = accessKind == AccessKind.ReadOnlyAccess && operationKind == OperationKind.Write;

            var entity = MyAspectMiscTests.CreateEntity(m_Manager, optionalKind == OptionalKind.WithOptionalComponent);
            var test = World.GetOrCreateSystemManaged<MyAspectMiscTestsSystem>();
            if (operationKind == OperationKind.Write)
            {
                test.TestValue = MyAspectMiscTests.k_WriteValue;
            }
            test.OperationKind = operationKind;
            test.AccessKind = accessKind;
            test.ContextKind = contextKind;

            if (readOnlyException)
                Assert.Throws<System.InvalidOperationException>(delegate { test.Update(); });
            else
                test.Update();

            if (operationKind == OperationKind.Read)
            {
                Assert.AreEqual(
                    optionalKind == OptionalKind.WithOptionalComponent
                        ? MyAspectMiscTests.k_InitialValueSumWithOptional
                        : MyAspectMiscTests.k_InitialValueSum,
                    test.TestValue, "the value read through the aspect should be the one the components value are set to");

            }
            else if (!readOnlyException)
            {
                var chunk = m_Manager.GetChunk(entity);
                chunk.VisitAllOfComponent<EcsTestData>(m_Manager, x => Assert.AreEqual(MyAspectMiscTests.k_WriteValue, x.value, "Component EcsTestData must be overwritten with the MyAspectMiscTests.k_WriteValue value"));
                chunk.VisitAllOfComponent<EcsTestData2>(m_Manager, x => Assert.AreEqual(MyAspectMiscTests.k_WriteValue, x.value0, "Component EcsTestData2 must be overwritten with the MyAspectMiscTests.k_WriteValue value"));
                chunk.VisitAllOfComponent<EcsTestData3>(m_Manager, x => Assert.AreEqual(MyAspectMiscTests.k_WriteValue, x.value0, "Component EcsTestData3 must be overwritten with the MyAspectMiscTests.k_WriteValue value"));
                chunk.VisitAllOfComponent<EcsTestData4>(m_Manager, x => Assert.AreEqual(MyAspectMiscTests.k_InitialValue, x.value0, "The component EcsTestData4 is read only and must be equal to initial value (should not be written over)"));
                if (optionalKind == OptionalKind.WithOptionalComponent)
                    chunk.VisitAllOfComponent<EcsTestData5>(m_Manager, x => Assert.AreEqual(MyAspectMiscTests.k_WriteValue, x.value0, "Component EcsTestData5 must be overwritten with the MyAspectMiscTests.k_WriteValue value"));
            }
        }

        [Test]
        public void AspectEnumerationWithEnableBitFiltering()
        {
            var componentsWithEnabled = ComponentType.Combine(MyAspect.RequiredComponents, new[] { ComponentType.ReadOnly<EcsTestDataEnableable>() });
            var componentsWithoutEnabled = ComponentType.Combine(MyAspect.RequiredComponents, new[] { ComponentType.ReadOnly<EcsTestTag>() });

            // Create entities
            var allEntitiesArch = m_Manager.CreateArchetype(componentsWithoutEnabled);
            var enableEntitiesArch = m_Manager.CreateArchetype(componentsWithEnabled);
            var allEntities = m_Manager.CreateEntity(allEntitiesArch, 500, Allocator.Temp);
            var enableEntities = m_Manager.CreateEntity(enableEntitiesArch, 500, Allocator.Temp);

            // Disable some entities to enforce some filtering / chunking
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(enableEntities[10], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(enableEntities[63], false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(enableEntities[64], false);
            for (int i = 250; i < enableEntities.Length; i++)
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(enableEntities[i], false);

            var queryEnabled = EmptySystem.GetEntityQuery(
                new EntityQueryDesc
                {
                    All = componentsWithEnabled
                });
            var queryAll = EmptySystem.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = componentsWithoutEnabled
                });

            int expectedCounts = 500 + 250 - 3;
            Assert.AreEqual(expectedCounts, queryAll.CalculateEntityCount() + queryEnabled.CalculateEntityCount());

            var type = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);
            int counter = 0;

            foreach (var e in MyAspect.Query(queryEnabled, type))
            {
                e._Data.ValueRW.value++;
                counter++;
            }

            foreach (var e in MyAspect.Query(queryAll, type))
            {
                e._Data.ValueRW.value++;
                counter++;
            }

            Assert.AreEqual(expectedCounts, counter);
            for (int i = 0; i < allEntities.Length; ++i)
                Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(allEntities[i]).value);
            for (int i = 0; i < enableEntities.Length; ++i)
            {
                var expectEnabled = i != 10 && i != 63 && i != 64 && i < 250;
                Assert.AreEqual(expectEnabled ? 1 : 0, m_Manager.GetComponentData<EcsTestData>(enableEntities[i]).value, $"At index: {i}");
            }

        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG

        [Test]
        public void AspectSafety_TestForEachSets_IsInForEachDisallowStructuralChangeCounter()
        {
            m_Manager.CreateEntity(MyAspect.RequiredComponentsRO);

            var query = m_Manager.CreateEntityQuery(MyAspect.RequiredComponentsRO);
            var typeHandle = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);

            int counter = 0;
            Assert.AreEqual(0, m_Manager.Debug.IsInForEachDisallowStructuralChangeCounter);
            foreach (var rotationData in MyAspect.Query(query, typeHandle))
            {
                Assert.AreEqual(1, m_Manager.Debug.IsInForEachDisallowStructuralChangeCounter);
                counter++;
            }
            Assert.AreEqual(0, m_Manager.Debug.IsInForEachDisallowStructuralChangeCounter);
            Assert.AreEqual(1, counter);
        }
        [Test]
        public void AspectSafety_MoveNextTooFar()
        {
            var e = m_Manager.CreateEntity(MyAspect.RequiredComponentsRO);

            var query = m_Manager.CreateEntityQuery(MyAspect.RequiredComponentsRO);
            var typeHandle = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);

            using (var iterator = MyAspect.Query(query, typeHandle))
            {
                Assert.IsTrue(iterator.MoveNext());
                Assert.IsFalse(iterator.MoveNext());
                Assert.Throws<ArgumentOutOfRangeException>(() => { var value = iterator.Current; });
            }
        }

        [Test]
        public void AspectSafety_ResetThrows()
        {
            var e = m_Manager.CreateEntity(MyAspect.RequiredComponentsRO);

            var query = m_Manager.CreateEntityQuery(MyAspect.RequiredComponentsRO);
            var typeHandle = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);
            using (var iterator = MyAspect.Query(query, typeHandle))
            {
                Assert.Throws<NotImplementedException>(() => { ((IEnumerator)iterator).Reset(); });
            }
        }

        [Test]
        public void AspectSafety_AccessingDisposedQueryEnumeratorThrows()
        {
            var e = m_Manager.CreateEntity(MyAspect.RequiredComponentsRO);

            var query = m_Manager.CreateEntityQuery(MyAspect.RequiredComponentsRO);
            var typeHandle = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);
            var iterator = MyAspect.Query(query, typeHandle);
            query.Dispose();
            Assert.Throws<ObjectDisposedException>(() => iterator.MoveNext());
            Assert.Throws<ObjectDisposedException>(() => iterator.Dispose());

            m_Manager.Debug.SetIsInForEachDisallowStructuralChangeCounter(0);
        }
        [Test]
        public void AspectSafety_AbuseDisposedEnumeratorThrows([Values(0, 1, 2)] int disposeScenario)
        {
            var e = m_Manager.CreateEntity(MyAspect.RequiredComponentsRO);

            var query = m_Manager.CreateEntityQuery(MyAspect.RequiredComponentsRO);
            var typeHandle = new MyAspect.TypeHandle(ref EmptySystem.CheckedStateRef, false);

            var iterator = MyAspect.Query(query, typeHandle);

            var iteratorFirstElement = iterator;
            Assert.IsTrue(iteratorFirstElement.MoveNext());
            if (disposeScenario == 0)
                iterator.Dispose();
            else if (disposeScenario == 1)
                iterator.Dispose();
            else
                m_Manager.Debug.SetIsInForEachDisallowStructuralChangeCounter(0);
            m_Manager.DestroyEntity(e);
            if (disposeScenario == 1)
                m_Manager.Debug.SetIsInForEachDisallowStructuralChangeCounter(1);

            Assert.Throws<ObjectDisposedException>(() => { var value = iterator.Current; });
            Assert.Throws<ObjectDisposedException>(() => iterator.MoveNext());
            Assert.Throws<ObjectDisposedException>(() => { var value = iterator.Current; });

            //Assert.Throws<ObjectDisposedException>(() => { var value = iteratorFirstElement.Current; }); // NativeArray enumarator no longer throws for use after free
            Assert.Throws<ObjectDisposedException>(() => { iteratorFirstElement.MoveNext(); });
            //Assert.Throws<ObjectDisposedException>(() => { var value = iteratorFirstElement.Current; }); // NativeArray enumarator no longer throws for use after fr

            // Clean up

            if (disposeScenario == 1)
                m_Manager.Debug.SetIsInForEachDisallowStructuralChangeCounter(0);

            unsafe
            {
                if (disposeScenario == 2)
                {
                    iterator.Dispose();
                    var access = m_Manager.GetCheckedEntityDataAccess();
                    access->DependencyManager->ForEachStructuralChange.Depth = 0;
                }
            }
        }
#endif

        [Test]
        public void Aspect_With_Same_Name_But_Different_Namespace_Works()
        {
            var archetype01 = m_Manager.CreateArchetype(typeof(Data01));
            var archetype02 = m_Manager.CreateArchetype(typeof(Data02));
            m_Manager.CreateEntity(archetype01, 4);
            m_Manager.CreateEntity(archetype02, 2);

            var query01 = EmptySystem.GetEntityQuery(AnAspect.RequiredComponents);
            var query02 = EmptySystem.GetEntityQuery(ParentAspectNamespace02.AnAspect.RequiredComponents);

            Assert.AreEqual(4, query01.CalculateEntityCount());
            Assert.AreEqual(2, query02.CalculateEntityCount());
        }

        [Test]
        public void Aspect_With_Same_Name_But_Two_Different_Namespaces_Works()
        {
            var archetype01 = m_Manager.CreateArchetype(typeof(Data01));
            var archetype02 = m_Manager.CreateArchetype(typeof(Data02));
            m_Manager.CreateEntity(archetype01, 4);
            m_Manager.CreateEntity(archetype02, 2);

            var query01 = EmptySystem.GetEntityQuery(ParentAspectNamespace01.AnAspect.RequiredComponents);
            var query02 = EmptySystem.GetEntityQuery(ParentAspectNamespace02.AnAspect.RequiredComponents);

            Assert.AreEqual(4, query01.CalculateEntityCount());
            Assert.AreEqual(2, query02.CalculateEntityCount());
        }
    }

    struct Data01 : IComponentData
    {
        public int Value;
    }

    struct Data02 : IComponentData
    {
        public int Value;
    }

    readonly partial struct AnAspect : IAspect
    {
        public readonly RefRW<Data01> Value;
    }

    namespace ParentAspectNamespace01
    {
        readonly partial struct AnAspect : IAspect
        {
            public readonly RefRW<Data01> Value;
        }
    }

    namespace ParentAspectNamespace02
    {
        readonly partial struct AnAspect : IAspect
        {
            public readonly RefRW<Data02> Value;
        }
    }
}
