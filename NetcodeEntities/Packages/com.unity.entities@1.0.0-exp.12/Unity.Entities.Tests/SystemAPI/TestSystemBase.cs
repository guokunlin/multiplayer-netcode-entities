using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Mathematics;
using Unity.Transforms;
using static Unity.Entities.SystemAPI;
namespace Unity.Entities.Tests.TestSystemAPI
{
    /// <summary>
    /// Make sure this matches <see cref="TestISystem"/>.
    /// </summary>
    [TestFixture]
    public class TestSystemBase : ECSTestsFixture
    {
        [SetUp]
        public void SetUp() {
            World.GetOrCreateSystemManaged<TestSystemBaseSystem>();
            World.GetOrCreateSystemManaged<TestSystemBaseSystem.GenericSystem<EcsTestData>>();
        }

        #region Query Access
        [Test]
        public void Query([Values(1,2,3,4,5,6,7)] int queryArgumentCount) => World.GetExistingSystemManaged<TestSystemBaseSystem>().QuerySetup(queryArgumentCount);
        #endregion

        #region Time Access
        [Test]
        public void Time([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestTime(memberUnderneath);
        #endregion

        #region Component Access

        [Test]
        public void GetComponentLookup([Values] MemberUnderneath memberUnderneath, [Values] ReadAccess readAccess) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetComponentLookup(memberUnderneath, readAccess);

        [Test]
        public void GetComponent([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetComponent(memberUnderneath);

        [Test]
        public void SetComponent() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestSetComponent();

        [Test]
        public void HasComponent([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestHasComponent(memberUnderneath);

        [Test]
        public void GetComponentForSystem([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetComponentForSystem(memberUnderneath);

        [Test]
        public void GetComponentRWForSystem([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetComponentRWForSystem(memberUnderneath);

        [Test]
        public void SetComponentForSystem() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestSetComponentForSystem();

        [Test]
        public void HasComponentForSystem([Values] MemberUnderneath memberUnderneath) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestHasComponentForSystem(memberUnderneath);
        #endregion

        #region Buffer Access

        [Test]
        public void GetBufferDataFromEntity([Values] SystemAPIAccess access, [Values] MemberUnderneath memberUnderneath, [Values] ReadAccess readAccess) =>
            World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetBufferLookup(access, memberUnderneath, readAccess);

        [Test]
        public void GetBuffer([Values] SystemAPIAccess access, [Values] MemberUnderneath memberUnderneath, [Values] ReadAccess readAccess) =>
            World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetBuffer(access, memberUnderneath);

        [Test]
        public void HasBuffer([Values] SystemAPIAccess access, [Values] MemberUnderneath memberUnderneath, [Values] ReadAccess readAccess) =>
            World.GetExistingSystemManaged<TestSystemBaseSystem>().TestHasBuffer(access, memberUnderneath);

        #endregion

        #region StorageInfo Access

        [Test]
        public void GetEntityStorageInfoLookup([Values] SystemAPIAccess access) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetEntityStorageInfoLookup(access);

        [Test]
        public void Exists([Values] SystemAPIAccess access) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestExists(access);

        #endregion

        #region Singleton Access
        [Test]
        public void GetSingleton() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingleton();
        [Test]
        public void GetSingletonWithSystemEntity() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingletonWithSystemEntity();
        [Test]
        public void TryGetSingleton([Values] TypeArgumentExplicit typeArgumentExplicit) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestTryGetSingleton(typeArgumentExplicit);
        [Test]
        public void GetSingletonRW() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingletonRW();
        [Test]
        public void SetSingleton([Values] TypeArgumentExplicit typeArgumentExplicit) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestSetSingleton(typeArgumentExplicit);
        [Test]
        public void GetSingletonEntity() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingletonEntity();
        [Test]
        public void TryGetSingletonEntity() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestTryGetSingletonEntity();
        [Test]
        public void GetSingletonBuffer() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingletonBuffer();
        [Test]
        public void GetSingletonBufferWithSystemEntity() => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetSingletonBufferWithSystemEntity();
        [Test]
        public void TryGetSingletonBuffer([Values] TypeArgumentExplicit typeArgumentExplicit) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestTryGetSingletonBuffer(typeArgumentExplicit);
        [Test]
        public void HasSingleton([Values] SingletonVersion singletonVersion) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestHasSingleton(singletonVersion);
        #endregion

        #region Aspect

        [Test]
        public void GetAspectRW([Values] SystemAPIAccess access) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetAspectRW(access);

        [Test]
        public void GetAspectRO([Values] SystemAPIAccess access) => World.GetExistingSystemManaged<TestSystemBaseSystem>().TestGetAspectRO(access);

        #endregion

        #region NoError
        [Test]
        public void Nesting() =>  World.GetExistingSystemManaged<TestSystemBaseSystem>().TestNesting();
        [Test]
        public void StatementInsert() =>  World.GetExistingSystemManaged<TestSystemBaseSystem>().TestStatementInsert();
        [Test]
        public void GenericSystem() => World.GetExistingSystemManaged<TestSystemBaseSystem.GenericSystem<EcsTestData>>().TestGenericSystem();
        [Test]
        public void VariableInOnCreate() => World.CreateSystemManaged<TestSystemBaseSystem.VariableInOnCreateSystem>();
        #endregion
    }

    partial class TestSystemBaseSystem : SystemBase
    {
        protected override void OnCreate() {}
        protected override void OnDestroy() {}
        protected override void OnUpdate() {}

        #region Query Access
        public void QuerySetup(int queryArgumentCount)
        {
            for (var i = 0; i < 10; i++)
            {
                var e = EntityManager.CreateEntity();
#if !ENABLE_TRANSFORM_V1
                EntityManager.AddComponentData(e, new LocalToWorldTransform{Value=UniformScaleTransform.FromPosition(i)});
                EntityManager.AddComponentData(e, new LocalToWorld());
                EntityManager.AddComponentData(e, new LocalToParentTransform{Value=UniformScaleTransform.Identity});
#else
                EntityManager.AddComponentData(e, new Translation{Value=i});
                EntityManager.AddComponentData(e, new Rotation());
                EntityManager.AddComponentData(e, new LocalToWorld());
                EntityManager.AddComponentData(e, new LocalToParent());
#endif
                EntityManager.AddComponentData(e, new EcsTestData(i));
                EntityManager.AddComponentData(e, new EcsTestData2(i));
                EntityManager.AddComponentData(e, new EcsTestData3(i));
                EntityManager.AddComponentData(e, new EcsTestData4(i));
                EntityManager.AddComponentData(e, new EcsTestData5(i));
                EntityManager.AddComponentData(e, new EcsTestDataEnableable(i));
                EntityManager.AddComponentData(e, new EcsTestDataEnableable2(i));
            }

            Assert.AreEqual(45*queryArgumentCount, queryArgumentCount switch
            {
                1 => Query1(),
                2 => Query2(),
                3 => Query3(),
                4 => Query4(),
                5 => Query5(),
                6 => Query6(),
                7 => Query7(),
                _ => throw new ArgumentOutOfRangeException(nameof(queryArgumentCount), queryArgumentCount, null)
            });
        }

        int Query1()
        {
            var sum = 0;
            foreach (var transformAspect in SystemAPI.Query<TransformAspect>())
                sum += (int) transformAspect.LocalPosition.x;
            return sum;
        }

        int Query2()
        {
            var sum = 0;
            foreach (var (transformAspect, data1) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
            }
            return sum;
        }

        int Query3()
        {
            var sum = 0;
            foreach (var (transformAspect, data1, data2) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>, RefRW<EcsTestData2>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
                sum += data2.ValueRO.value0;
            }
            return sum;
        }

        int Query4()
        {
            var sum = 0;
            foreach (var (transformAspect, data1, data2, data3) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>, RefRW<EcsTestData2>, RefRW<EcsTestData3>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
                sum += data2.ValueRO.value0;
                sum += data3.ValueRO.value0;
            }
            return sum;
        }

        int Query5()
        {
            var sum = 0;
            foreach (var (transformAspect, data1, data2, data3, data4) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>, RefRW<EcsTestData2>, RefRW<EcsTestData3>, RefRW<EcsTestData4>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
                sum += data2.ValueRO.value0;
                sum += data3.ValueRO.value0;
                sum += data4.ValueRO.value0;
            }
            return sum;
        }

        int Query6()
        {
            var sum = 0;
            foreach (var (transformAspect, data1, data2, data3, data4, data5) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>, RefRW<EcsTestData2>, RefRW<EcsTestData3>, RefRW<EcsTestData4>, RefRW<EcsTestData5>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
                sum += data2.ValueRO.value0;
                sum += data3.ValueRO.value0;
                sum += data4.ValueRO.value0;
                sum += data5.ValueRO.value0;
            }
            return sum;
        }

        int Query7()
        {
            var sum = 0;
            foreach (var (transformAspect, data1, data2, data3, data4, data5, data6) in SystemAPI.Query<TransformAspect, RefRW<EcsTestData>, RefRW<EcsTestData2>, RefRW<EcsTestData3>, RefRW<EcsTestData4>, RefRW<EcsTestData5>, RefRW<EcsTestDataEnableable>>())
            {
                sum += (int)transformAspect.LocalPosition.x;
                sum += data1.ValueRO.value;
                sum += data2.ValueRO.value0;
                sum += data3.ValueRO.value0;
                sum += data4.ValueRO.value0;
                sum += data5.ValueRO.value0;
                sum += data6.ValueRO.value;
            }
            return sum;
        }

        #endregion

        #region Time Access

        public void TestTime(MemberUnderneath memberUnderneath)
        {
            var time = new TimeData(42, 0.5f);
            World.SetTime(time);

            if (memberUnderneath == MemberUnderneath.WithMemberUnderneath) {
                Assert.That(SystemAPI.Time.DeltaTime, Is.EqualTo(time.DeltaTime));
            } else if (memberUnderneath == MemberUnderneath.WithoutMemberUnderneath) {
                Assert.That(SystemAPI.Time, Is.EqualTo(time));
            }
        }
        #endregion

        #region Component Access

#if !ENABLE_TRANSFORM_V1
        public void TestGetComponentLookup(MemberUnderneath memberUnderneath, ReadAccess readAccess) {
            var e = EntityManager.CreateEntity();
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            switch (readAccess) {
                case ReadAccess.ReadOnly:
                    // Get works
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath: {
                            var tGet = SystemAPI.GetComponentLookup<LocalToWorldTransform>(true)[e];
                            Assert.That(tGet, Is.EqualTo(t));
                        } break;
                        case MemberUnderneath.WithoutMemberUnderneath: {
                            var lookup = SystemAPI.GetComponentLookup<LocalToWorldTransform>(true);
                            var tGet = lookup[e];
                            Assert.That(tGet, Is.EqualTo(t));
                        } break;
                    } break;

                // Set works
                case ReadAccess.ReadWrite: {
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath: {
                            t.Value.Position += 1;
                            var lookup = SystemAPI.GetComponentLookup<LocalToWorldTransform>();
                            lookup[e] = t;
                            var tSet = SystemAPI.GetComponentLookup<LocalToWorldTransform>(true)[e];
                            Assert.That(tSet, Is.EqualTo(t));
                        } break;
                        case MemberUnderneath.WithoutMemberUnderneath: {
                            t.Value.Position += 1;
                            var lookup = SystemAPI.GetComponentLookup<LocalToWorldTransform>();
                            lookup[e] = t;
                            Assert.That(lookup[e], Is.EqualTo(t));
                        } break;
                    }
                } break;
            }
        }
#else
        public void TestGetComponentLookup(MemberUnderneath memberUnderneath, ReadAccess readAccess) {
            var e = EntityManager.CreateEntity();
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            switch (readAccess) {
                case ReadAccess.ReadOnly:
                    // Get works
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath: {
                            var tGet = SystemAPI.GetComponentLookup<Translation>(true)[e];
                            Assert.That(tGet, Is.EqualTo(t));
                        } break;
                        case MemberUnderneath.WithoutMemberUnderneath: {
                            var lookup = SystemAPI.GetComponentLookup<Translation>(true);
                            var tGet = lookup[e];
                            Assert.That(tGet, Is.EqualTo(t));
                        } break;
                    } break;

                // Set works
                case ReadAccess.ReadWrite: {
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath: {
                            t.Value += 1;
                            var lookup = SystemAPI.GetComponentLookup<Translation>();
                            lookup[e] = t;
                            var tSet = SystemAPI.GetComponentLookup<Translation>(true)[e];
                            Assert.That(tSet, Is.EqualTo(t));
                        } break;
                        case MemberUnderneath.WithoutMemberUnderneath: {
                            t.Value += 1;
                            var lookup = SystemAPI.GetComponentLookup<Translation>();
                            lookup[e] = t;
                            Assert.That(lookup[e], Is.EqualTo(t));
                        } break;
                    }
                } break;
            }
        }
#endif

#if !ENABLE_TRANSFORM_V1
        public void TestGetComponent(MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity();
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(e).Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(e), Is.EqualTo(t));
                    break;
            }
        }
#else
        public void TestGetComponent(MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity();
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<Translation>(e).Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<Translation>(e), Is.EqualTo(t));
                    break;
            }
        }
#endif

#if !ENABLE_TRANSFORM_V1
        public void TestSetComponent() {
            var e = EntityManager.CreateEntity();
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            t.Value.Position += 1;
            SystemAPI.SetComponent(e, t);
            Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(e), Is.EqualTo(t));
        }

        public void TestHasComponent(MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity(typeof(LocalToWorldTransform));

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<LocalToWorldTransform>(e).GetHashCode(), Is.EqualTo(1));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(e).GetHashCode(), Is.EqualTo(0));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<LocalToWorldTransform>(e), Is.EqualTo(true));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(e), Is.EqualTo(false));
                    break;
            }
        }
#else
        public void TestSetComponent() {
            var e = EntityManager.CreateEntity();
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(e, t);

            t.Value += 1;
            SystemAPI.SetComponent(e, t);
            Assert.That(SystemAPI.GetComponent<Translation>(e), Is.EqualTo(t));
        }

        public void TestHasComponent(MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity(typeof(Translation));

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<Translation>(e).GetHashCode(), Is.EqualTo(1));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(e).GetHashCode(), Is.EqualTo(0));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<Translation>(e), Is.EqualTo(true));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(e), Is.EqualTo(false));
                    break;
            }
        }
#endif

#if !ENABLE_TRANSFORM_V1
        public void TestGetComponentForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(SystemHandle).Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(SystemHandle), Is.EqualTo(t));
                    break;
            }
        }
#else
        public void TestGetComponentForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<Translation>(SystemHandle).Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponent<Translation>(SystemHandle), Is.EqualTo(t));
                    break;
            }
        }
#endif

#if !ENABLE_TRANSFORM_V1
        public void TestGetComponentRWForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponent<LocalToWorldTransform>(SystemHandle);
            SystemAPI.GetComponentRW<LocalToWorldTransform>(SystemHandle).ValueRW = t;

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponentRW<LocalToWorldTransform>(SystemHandle).ValueRW.Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponentRW<LocalToWorldTransform>(SystemHandle).ValueRW, Is.EqualTo(t));
                    break;
            }
        }
#else
        public void TestGetComponentRWForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponent<Translation>(SystemHandle);
            SystemAPI.GetComponentRW<Translation>(SystemHandle).ValueRW = t;

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.GetComponentRW<Translation>(SystemHandle).ValueRW.Value, Is.EqualTo(t.Value));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.GetComponentRW<Translation>(SystemHandle).ValueRW, Is.EqualTo(t));
                    break;
            }
        }
#endif

#if !ENABLE_TRANSFORM_V1
        public void TestSetComponentForSystem()
        {
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            t.Value.Position += 1;
            SystemAPI.SetComponent(SystemHandle, t);
            Assert.That(SystemAPI.GetComponent<LocalToWorldTransform>(SystemHandle), Is.EqualTo(t));
        }

        public void TestHasComponentForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new LocalToWorldTransform { Value = UniformScaleTransform.FromPosition(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<LocalToWorldTransform>(SystemHandle).GetHashCode(), Is.EqualTo(1));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(SystemHandle).GetHashCode(), Is.EqualTo(0));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<LocalToWorldTransform>(SystemHandle), Is.EqualTo(true));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(SystemHandle), Is.EqualTo(false));
                    break;
            }
        }
#else
        public void TestSetComponentForSystem()
        {
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            t.Value += 1;
            SystemAPI.SetComponent(SystemHandle, t);
            Assert.That(SystemAPI.GetComponent<Translation>(SystemHandle), Is.EqualTo(t));
        }

        public void TestHasComponentForSystem(MemberUnderneath memberUnderneath)
        {
            var t = new Translation { Value = new float3(0, 2, 0) };
            EntityManager.AddComponentData(SystemHandle, t);

            switch (memberUnderneath)
            {
                case MemberUnderneath.WithMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<Translation>(SystemHandle).GetHashCode(), Is.EqualTo(1));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(SystemHandle).GetHashCode(), Is.EqualTo(0));
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    Assert.That(SystemAPI.HasComponent<Translation>(SystemHandle), Is.EqualTo(true));
                    Assert.That(SystemAPI.HasComponent<EcsTestData>(SystemHandle), Is.EqualTo(false));
                    break;
            }
        }
#endif
        #endregion

        #region Buffer Access
        public void TestGetBufferLookup(SystemAPIAccess access, MemberUnderneath memberUnderneath, ReadAccess readAccess) {
            var e = EntityManager.CreateEntity();
            var t = new EcsIntElement { Value = 42 };
            var buffer = EntityManager.AddBuffer<EcsIntElement>(e);
            buffer.Add(t);

            switch (readAccess) {
                case ReadAccess.ReadOnly:
                    // Get works
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath:
                            switch (access) {
                                case SystemAPIAccess.SystemAPI: {
                                    var tGet = SystemAPI.GetBufferLookup<EcsIntElement>(true)[e];
                                    Assert.That(tGet[0], Is.EqualTo(t));
                                } break;
                                case SystemAPIAccess.Using: {
                                    var tGet = GetBufferLookup<EcsIntElement>(true)[e];
                                    Assert.That(tGet[0], Is.EqualTo(t));
                                } break;
                            }
                            break;
                        case MemberUnderneath.WithoutMemberUnderneath:
                            switch (access) {
                                case SystemAPIAccess.SystemAPI: {
                                    var bfe = SystemAPI.GetBufferLookup<EcsIntElement>(true);
                                    var tGet = bfe[e];
                                    Assert.That(tGet[0], Is.EqualTo(t));
                                } break;
                                case SystemAPIAccess.Using: {
                                    var bfe = GetBufferLookup<EcsIntElement>(true);
                                    var tGet = bfe[e];
                                    Assert.That(tGet[0], Is.EqualTo(t));
                                } break;
                            }
                            break;
                    } break;

                // Set works
                case ReadAccess.ReadWrite: {
                    switch (memberUnderneath) {
                        case MemberUnderneath.WithMemberUnderneath:
                            switch (access) {
                                case SystemAPIAccess.SystemAPI: {
                                    t.Value += 1;
                                    var bfe = SystemAPI.GetBufferLookup<EcsIntElement>();
                                    bfe[e].ElementAt(0) = t;
                                    var tSet = SystemAPI.GetBufferLookup<EcsIntElement>(true)[e];
                                    Assert.That(tSet[0], Is.EqualTo(t));
                                } break;
                                case SystemAPIAccess.Using: {
                                    t.Value += 1;
                                    var bfe = GetBufferLookup<EcsIntElement>();
                                    bfe[e].ElementAt(0) = t;
                                    var tSet = GetBufferLookup<EcsIntElement>(true)[e];
                                    Assert.That(tSet[0], Is.EqualTo(t));
                                } break;
                            }
                            break;
                        case MemberUnderneath.WithoutMemberUnderneath:
                            switch (access) {
                                case SystemAPIAccess.SystemAPI: {
                                    t.Value += 1;
                                    var bfe = SystemAPI.GetBufferLookup<EcsIntElement>();
                                    bfe[e].ElementAt(0) = t;
                                    Assert.That(bfe[e][0], Is.EqualTo(t));
                                } break;
                                case SystemAPIAccess.Using: {
                                    t.Value += 1;
                                    var bfe = GetBufferLookup<EcsIntElement>();
                                    bfe[e].ElementAt(0) = t;
                                    Assert.That(bfe[e][0], Is.EqualTo(t));
                                } break;
                            }
                            break;
                    }
                } break;
            }
        }

        public void TestGetBuffer(SystemAPIAccess access, MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity();
            var buffer = EntityManager.AddBuffer<EcsIntElement>(e);
            var t = new EcsIntElement() { Value = 42 };
            buffer.Add(t);

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    switch (access) {
                        case SystemAPIAccess.SystemAPI:
                            Assert.That(SystemAPI.GetBuffer<EcsIntElement>(e)[0].Value, Is.EqualTo(t.Value));
                            break;
                        case SystemAPIAccess.Using:
                            Assert.That(GetBuffer<EcsIntElement>(e)[0].Value, Is.EqualTo(t.Value));
                            break;
                    }
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    switch (access) {
                        case SystemAPIAccess.SystemAPI:
                            Assert.That(SystemAPI.GetBuffer<EcsIntElement>(e)[0], Is.EqualTo(t));
                            break;
                        case SystemAPIAccess.Using:
                            Assert.That(GetBuffer<EcsIntElement>(e)[0], Is.EqualTo(t));
                            break;
                    }
                    break;
            }
        }

        public void TestHasBuffer(SystemAPIAccess access, MemberUnderneath memberUnderneath) {
            var e = EntityManager.CreateEntity(typeof(EcsIntElement));

            switch (memberUnderneath) {
                case MemberUnderneath.WithMemberUnderneath:
                    switch (access) {
                        case SystemAPIAccess.SystemAPI:
                            Assert.That(SystemAPI.HasBuffer<EcsIntElement>(e).GetHashCode(), Is.EqualTo(1));
                            Assert.That(SystemAPI.HasBuffer<EcsIntElement2>(e).GetHashCode(), Is.EqualTo(0));
                            break;
                        case SystemAPIAccess.Using:
                            Assert.That(HasBuffer<EcsIntElement>(e).GetHashCode(), Is.EqualTo(1));
                            Assert.That(HasBuffer<EcsIntElement2>(e).GetHashCode(), Is.EqualTo(0));
                            break;
                    }
                    break;
                case MemberUnderneath.WithoutMemberUnderneath:
                    switch (access) {
                        case SystemAPIAccess.SystemAPI:
                            Assert.That(SystemAPI.HasBuffer<EcsIntElement>(e), Is.EqualTo(true));
                            Assert.That(SystemAPI.HasBuffer<EcsIntElement2>(e), Is.EqualTo(false));
                            break;
                        case SystemAPIAccess.Using:
                            Assert.That(HasBuffer<EcsIntElement>(e), Is.EqualTo(true));
                            Assert.That(HasBuffer<EcsIntElement2>(e), Is.EqualTo(false));
                            break;
                    }
                    break;
            }
        }

        #endregion

        #region StorageInfo Access

        public void TestGetEntityStorageInfoLookup(SystemAPIAccess access)
        {
            var e = EntityManager.CreateEntity();

            switch (access) {
                case SystemAPIAccess.SystemAPI: {
                    var storageInfoLookup = SystemAPI.GetEntityStorageInfoLookup();
                    Assert.IsTrue(storageInfoLookup.Exists(e));
                } break;
                case SystemAPIAccess.Using: {
                    var storageInfoLookup = GetEntityStorageInfoLookup();
                    Assert.IsTrue(storageInfoLookup.Exists(e));
                } break;
            }
        }

        public void TestExists(SystemAPIAccess access)
        {
            var e = EntityManager.CreateEntity();

            switch (access) {
                case SystemAPIAccess.SystemAPI: {
                    Assert.IsTrue(SystemAPI.Exists(e));
                } break;
                case SystemAPIAccess.Using: {
                    Assert.IsTrue(Exists(e));
                } break;
            }
        }

        #endregion

        #region Singleton Access
        public void TestGetSingleton()
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new EcsTestData(5));
            Assert.AreEqual(SystemAPI.GetSingleton<EcsTestData>().value, 5);
        }

        public void TestGetSingletonWithSystemEntity()
        {
            EntityManager.AddComponentData(SystemHandle, new EcsTestData(5));
            Assert.AreEqual(SystemAPI.GetSingleton<EcsTestData>().value, 5);
        }

        public void TestTryGetSingleton(TypeArgumentExplicit typeArgumentExplicit)
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new EcsTestData(5));

            switch (typeArgumentExplicit)
            {
                case TypeArgumentExplicit.TypeArgumentShown:
                    Assert.True(SystemAPI.TryGetSingleton<EcsTestData>(out var valSystemAPITypeArgumentShown));
                    Assert.AreEqual(valSystemAPITypeArgumentShown.value, 5);
                    break;
                case TypeArgumentExplicit.TypeArgumentHidden:
                    Assert.True(SystemAPI.TryGetSingleton(out EcsTestData valSystemAPITypeArgumentHidden));
                    Assert.AreEqual(valSystemAPITypeArgumentHidden.value, 5);
                    break;
            }
        }

        public void TestGetSingletonRW()
        {
            EntityManager.CreateEntity(typeof(EcsTestData));
            SystemAPI.GetSingletonRW<EcsTestData>().ValueRW.value = 5;
            Assert.AreEqual(5, GetSingleton<EcsTestData>().value);
        }

        public void TestSetSingleton(TypeArgumentExplicit typeArgumentExplicit)
        {
            EntityManager.CreateEntity(typeof(EcsTestData));
            var data = new EcsTestData(5);
            switch (typeArgumentExplicit)
            {
                case TypeArgumentExplicit.TypeArgumentShown:
                    SystemAPI.SetSingleton<EcsTestData>(data);
                    break;
                case TypeArgumentExplicit.TypeArgumentHidden:
                    SystemAPI.SetSingleton(data);
                    break;
            }

            Assert.AreEqual(5, GetSingleton<EcsTestData>().value);
        }

        public void TestGetSingletonEntity()
        {
            var e1 = EntityManager.CreateEntity(typeof(EcsTestData));
            Assert.AreEqual(e1, SystemAPI.GetSingletonEntity<EcsTestData>());
        }

        public void TestTryGetSingletonEntity()
        {
            var e1 = EntityManager.CreateEntity(typeof(EcsTestData));
            Assert.True(SystemAPI.TryGetSingletonEntity<EcsTestData>(out var e2));
            Assert.AreEqual(e1, e2);
        }

        public void TestGetSingletonBuffer()
        {
            var e = EntityManager.CreateEntity();
            var buffer1 = EntityManager.AddBuffer<EcsIntElement>(e);
            buffer1.Add(5);
            Assert.AreEqual(buffer1[0],SystemAPI.GetSingletonBuffer<EcsIntElement>()[0]);
        }

        public void TestGetSingletonBufferWithSystemEntity()
        {
            EntityManager.AddComponent<EcsIntElement>(SystemHandle);
            var buffer1 = EntityManager.GetBuffer<EcsIntElement>(SystemHandle);
            buffer1.Add(5);
            Assert.AreEqual(buffer1[0], SystemAPI.GetSingletonBuffer<EcsIntElement>()[0]);
        }

        public void TestTryGetSingletonBuffer(TypeArgumentExplicit typeArgumentExplicit)
        {
            var e = EntityManager.CreateEntity();
            var buffer1 = EntityManager.AddBuffer<EcsIntElement>(e);
            buffer1.Add(5);

            switch (typeArgumentExplicit)
            {
                case TypeArgumentExplicit.TypeArgumentShown:
                    Assert.True(SystemAPI.TryGetSingletonBuffer<EcsIntElement>(out var buffer2SystemAPITypeArgumentShown));
                    Assert.AreEqual(buffer1[0], buffer2SystemAPITypeArgumentShown[0]);
                    break;
                case TypeArgumentExplicit.TypeArgumentHidden:
                    Assert.True(SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<EcsIntElement> buffer2SystemAPITypeArgumentHidden));
                    Assert.AreEqual(buffer1[0], buffer2SystemAPITypeArgumentHidden[0]);
                    break;
            }
        }

        public void TestHasSingleton(SingletonVersion singletonVersion)
        {
            EntityManager.CreateEntity(typeof(EcsTestData), typeof(EcsIntElement));

            switch (singletonVersion)
            {
                case SingletonVersion.ComponentData:
                    Assert.True(SystemAPI.HasSingleton<EcsTestData>());
                    break;
                case SingletonVersion.Buffer:
                    Assert.True(SystemAPI.HasSingleton<EcsIntElement>());
                    break;
            }
        }

        #endregion

        #region Aspect

        public void TestGetAspectRW(SystemAPIAccess access)
        {
            var entity = EntityManager.CreateEntity(typeof(EcsTestData));
            switch (access)
            {
                case SystemAPIAccess.SystemAPI:
                    SystemAPI.GetAspectRW<EcsTestAspect0RW>(entity ).EcsTestData.ValueRW.value = 5;
                    break;
                case SystemAPIAccess.Using:
                    SystemAPI.GetAspectRW<EcsTestAspect0RW>(entity).EcsTestData.ValueRW.value = 5;
                    break;
            }

            Assert.AreEqual(5, GetComponent<EcsTestData>(entity).value);
        }

        public void TestGetAspectRO(SystemAPIAccess access)
        {
            var entity = EntityManager.CreateEntity(typeof(EcsTestData));
            SetComponent(entity, new EcsTestData() { value = 5 });

            int result = 0;
            switch (access)
            {
                case SystemAPIAccess.SystemAPI:
                    result = SystemAPI.GetAspectRO<EcsTestAspect0RO>(entity).EcsTestData.ValueRO.value;
                    break;
                case SystemAPIAccess.Using:
                    result = GetAspectRO<EcsTestAspect0RO>(entity).EcsTestData.ValueRO.value;
                    break;
            }

            Assert.AreEqual(5, result);
        }

        #endregion

        #region NoError

#if !ENABLE_TRANSFORM_V1
        void NestingSetup()
        {
            // Setup Archetypes
            var playerArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestFloatData>(), ComponentType.ReadWrite<LocalToWorldTransform>(), ComponentType.ReadWrite<EcsTestTag>()
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));
            var coinArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestFloatData>(), ComponentType.ReadWrite<LocalToWorldTransform>(),
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));
            var coinCounterArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestData>()
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));

            // Setup Players
            var players = EntityManager.CreateEntity(playerArchetype, 5, World.UpdateAllocator.ToAllocator);
            foreach (var player in players)
                SetComponent(player, new EcsTestFloatData {Value = 0.1f});
            SetComponent(players[0], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(0,1,0)});
            SetComponent(players[1], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,1,0)});
            SetComponent(players[2], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(0,1,1)});
            SetComponent(players[3], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,1,1)});
            SetComponent(players[4], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,0,1)});

            // Setup Enemies
            var coins = EntityManager.CreateEntity(coinArchetype, 5, World.UpdateAllocator.ToAllocator);
            foreach (var coin in coins)
                SetComponent(coin, new EcsTestFloatData {Value = 1f});
            SetComponent(coins[0], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(0,1,0)});
            SetComponent(coins[1], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,1,0)});
            SetComponent(coins[2], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(0,1,1)});
            SetComponent(coins[3], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,1,1)});
            SetComponent(coins[4], new LocalToWorldTransform{Value = UniformScaleTransform.FromPosition(1,0,1)});

            // Setup Coin Counter
            EntityManager.CreateEntity(coinCounterArchetype);
        }
#else
        void NestingSetup()
        {
            // Setup Archetypes
            var playerArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestFloatData>(), ComponentType.ReadWrite<Translation>(), ComponentType.ReadWrite<EcsTestTag>()
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));
            var coinArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestFloatData>(), ComponentType.ReadWrite<Translation>(),
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));
            var coinCounterArchetype = EntityManager.CreateArchetype(new FixedList128Bytes<ComponentType> {
                ComponentType.ReadWrite<EcsTestData>()
            }.ToNativeArray(World.UpdateAllocator.ToAllocator));

            // Setup Players
            var players = EntityManager.CreateEntity(playerArchetype, 5, World.UpdateAllocator.ToAllocator);
            foreach (var player in players)
                SetComponent(player, new EcsTestFloatData {Value = 0.1f});
            SetComponent(players[0], new Translation{Value = new float3(0,1,0)});
            SetComponent(players[1], new Translation{Value = new float3(1,1,0)});
            SetComponent(players[2], new Translation{Value = new float3(0,1,1)});
            SetComponent(players[3], new Translation{Value = new float3(1,1,1)});
            SetComponent(players[4], new Translation{Value = new float3(1,0,1)});

            // Setup Enemies
            var coins = EntityManager.CreateEntity(coinArchetype, 5, World.UpdateAllocator.ToAllocator);
            foreach (var coin in coins)
                SetComponent(coin, new EcsTestFloatData {Value = 1f});
            SetComponent(coins[0], new Translation{Value = new float3(0,1,0)});
            SetComponent(coins[1], new Translation{Value = new float3(1,1,0)});
            SetComponent(coins[2], new Translation{Value = new float3(0,1,1)});
            SetComponent(coins[3], new Translation{Value = new float3(1,1,1)});
            SetComponent(coins[4], new Translation{Value = new float3(1,0,1)});

            // Setup Coin Counter
            EntityManager.CreateEntity(coinCounterArchetype);
        }
#endif
        public void TestNesting()
        {
            NestingSetup();

#if !ENABLE_TRANSFORM_V1
            foreach (var (playerTranslation, playerRadius) in SystemAPI.Query<RefRO<LocalToWorldTransform>, RefRO<EcsTestFloatData>>().WithAll<EcsTestTag>())
            foreach (var (coinTranslation, coinRadius, coinEntity) in SystemAPI.Query<RefRO<LocalToWorldTransform>, RefRO<EcsTestFloatData>>().WithEntityAccess().WithNone<EcsTestTag>())
                if (math.distancesq(playerTranslation.ValueRO.Value.Position, coinTranslation.ValueRO.Value.Position) < coinRadius.ValueRO.Value + playerRadius.ValueRO.Value)
                    SystemAPI.GetSingletonRW<EcsTestData>().ValueRW.value++; // Three-layer SystemAPI nesting
#else
            foreach (var (playerTranslation, playerRadius) in SystemAPI.Query<RefRO<Translation>, RefRO<EcsTestFloatData>>().WithAll<EcsTestTag>())
            foreach (var (coinTranslation, coinRadius, coinEntity) in SystemAPI.Query<RefRO<Translation>, RefRO<EcsTestFloatData>>().WithEntityAccess().WithNone<EcsTestTag>())
                if (math.distancesq(playerTranslation.ValueRO.Value, coinTranslation.ValueRO.Value) < coinRadius.ValueRO.Value + playerRadius.ValueRO.Value)
                    SystemAPI.GetSingletonRW<EcsTestData>().ValueRW.value++; // Three-layer SystemAPI nesting
#endif
            var coinsCollected = SystemAPI.GetSingleton<EcsTestData>().value;
            Assert.AreEqual(15, coinsCollected);
        }

        /// <summary>
        /// This will throw in cases where SystemAPI doesn't properly insert .Update and .CompleteDependencyXX statements.
        /// </summary>
        public void TestStatementInsert()
        {
            // Asserts that does not throw - Not using Assert.DoesNotThrow since a lambda capture to ref state will fail.
            foreach (var (transform, target) in Query<TransformAspect, RefRO<EcsTestDataEntity>>())
            {
                if (SystemAPI.Exists(target.ValueRO.value1))
                {
                    var targetTransform = SystemAPI.GetAspectRO<TransformAspect>(target.ValueRO.value1);
                    var src = transform.Position;
                    var dst = targetTransform.Position;
                    Assert.That(src, Is.Not.EqualTo(dst));
                }
            }
        }

        public partial class GenericSystem<T> : SystemBase where T : unmanaged, IComponentData {
            protected override void OnUpdate() {}

            public void TestGenericSystem() {
                var e = EntityManager.CreateEntity(typeof(EcsTestData));
                Assert.True(HasComponent<T>(e));
            }
        }

        public partial class VariableInOnCreateSystem : SystemBase {
            protected override void OnCreate() {
                var readOnly = true;
                var lookup = GetComponentLookup<EcsTestData>(readOnly);
            }

            protected override void OnUpdate() {}
        }

        #endregion
    }
}
