using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode.LowLevel.Unsafe;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.NetCode.Tests
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class CheckConnectionSystem : SystemBase
    {
        public int numConnected;
        public int numInGame;
        private EntityQuery inGame;
        private EntityQuery connected;
        protected override void OnCreate()
        {
            connected = GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>());
            inGame = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamInGame>());
        }

        protected override void OnUpdate()
        {
            numConnected = connected.CalculateEntityCount();
            numInGame = inGame.CalculateEntityCount();
        }
    }
    public class ConnectionTests
    {
        [Test]
        public void ConnectSingleClient()
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                testWorld.Bootstrap(true, typeof(CheckConnectionSystem));
                testWorld.CreateWorlds(true, 1);

                var ep = NetworkEndpoint.LoopbackIpv4;
                ep.Port = 7979;
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ServerWorld).ValueRW.Listen(ep);
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(1, testWorld.ServerWorld.GetExistingSystemManaged<CheckConnectionSystem>().numConnected);
                Assert.AreEqual(1, testWorld.ClientWorlds[0].GetExistingSystemManaged<CheckConnectionSystem>().numConnected);

                testWorld.GoInGame();
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(1, testWorld.ServerWorld.GetExistingSystemManaged<CheckConnectionSystem>().numConnected);
                Assert.AreEqual(1, testWorld.ServerWorld.GetExistingSystemManaged<CheckConnectionSystem>().numInGame);
                Assert.AreEqual(1, testWorld.ClientWorlds[0].GetExistingSystemManaged<CheckConnectionSystem>().numConnected);
                Assert.AreEqual(1, testWorld.ClientWorlds[0].GetExistingSystemManaged<CheckConnectionSystem>().numInGame);
            }
        }
    }

    public class VersionTests
    {
        [Test]
        public void SameVersion_ConnectSuccessfully()
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                testWorld.Bootstrap(true);
                //Don't tick the world after creation. that will generate the default protocol version.
                //We want to use a custom one here
                testWorld.CreateWorlds(true, 1, false);
                var serverVersion = testWorld.ServerWorld.EntityManager.CreateEntity(typeof(NetworkProtocolVersion));
                testWorld.ServerWorld.EntityManager.SetComponentData(serverVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 0,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 1
                });
                var clientVersion = testWorld.ClientWorlds[0].EntityManager.CreateEntity(typeof(NetworkProtocolVersion));
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(clientVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 0,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 1
                });

                var ep = NetworkEndpoint.LoopbackIpv4;
                ep.Port = 7979;
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ServerWorld).ValueRW.Listen(ep);
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                var query = testWorld.ClientWorlds[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                Assert.AreEqual(1, query.CalculateEntityCount());
            }
        }

        [Test]
        public void DifferentVersions_AreDisconnnected()
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                testWorld.Bootstrap(true);
                //Don't tick the world after creation. that will generate the default protocol version.
                //We want to use a custom one here
                testWorld.CreateWorlds(true, 1, false);
                var serverVersion = testWorld.ServerWorld.EntityManager.CreateEntity(typeof(NetworkProtocolVersion));
                testWorld.ServerWorld.EntityManager.SetComponentData(serverVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 0,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 1
                });
                var ep = NetworkEndpoint.LoopbackIpv4;
                ep.Port = 7979;
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ServerWorld).ValueRW.Listen(ep);

                //Different NetCodeVersion
                var clientVersion = testWorld.ClientWorlds[0].EntityManager.CreateEntity(typeof(NetworkProtocolVersion));
                var query = testWorld.ClientWorlds[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(clientVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 2,
                    GameVersion = 0,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 1
                });
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                // The ordering of the protocol version error messages can be scrambled, so we can't log.expect exact ordering
                LogAssert.ignoreFailingMessages = true;
                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(0, query.CalculateEntityCount());
                //Different GameVersion
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(clientVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 1,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 1
                });
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(0, query.CalculateEntityCount());
                //Different Rpcs
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(clientVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 0,
                    RpcCollectionVersion = 2,
                    ComponentCollectionVersion = 1
                });
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(0, query.CalculateEntityCount());

                //Different Ghost
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(clientVersion, new NetworkProtocolVersion
                {
                    NetCodeVersion = 1,
                    GameVersion = 0,
                    RpcCollectionVersion = 1,
                    ComponentCollectionVersion = 2
                });
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                LogAssert.Expect(LogType.Error, new Regex("\\[(.*)\\] RpcSystem received bad protocol version from connection 0"));
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                Assert.AreEqual(0, query.CalculateEntityCount());
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void ProtocolVersionDebugInfoAppearsOnMismatch(bool debugServer)
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                testWorld.Bootstrap(true);
                testWorld.CreateWorlds(true, 1, false);

                // Only print the protocol version debug errors in one world, so the output can be deterministically validated
                // if it's printed in both worlds (client and server) the output can interweave and log checks will fail
                var debugWorld = testWorld.ServerWorld;
                if (debugServer)
                    debugWorld = testWorld.ClientWorlds[0];
                var netDebug = debugWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<NetCodeDebugConfig>());
                debugWorld.EntityManager.SetComponentData(netDebug, new NetCodeDebugConfig(){ DumpPackets = false, LogLevel = NetDebug.LogLevelType.Exception });

                float dt = 16f / 1000f;
                var entity = testWorld.ClientWorlds[0].EntityManager.CreateEntity(ComponentType.ReadWrite<GameProtocolVersion>());
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(entity, new GameProtocolVersion(){Version = 9000});
                testWorld.Tick(dt);
                testWorld.Tick(dt);

                var ep = NetworkEndpoint.LoopbackIpv4;
                ep.Port = 7979;
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ServerWorld).ValueRW.Listen(ep);
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);

                if (debugServer)
                {
                    LogAssert.Expect(LogType.Error,
                        "[ServerTest] RpcSystem received bad protocol version from connection 0");
                    LogExpectProtocolError(
                        "NetCode=1 Game=0 RpcCollection=(\\d+) ComponentCollection=(\\d+)",
                        "NetCode=1 Game=9000 RpcCollection=(\\d+) ComponentCollection=(\\d+)", testWorld, testWorld.ServerWorld);
                }
                else
                {
                    LogAssert.Expect(LogType.Error,
                        "[ClientTest0] RpcSystem received bad protocol version from connection 0");
                    LogExpectProtocolError(
                        "NetCode=1 Game=9000 RpcCollection=(\\d+) ComponentCollection=(\\d+)",
                        "NetCode=1 Game=0 RpcCollection=(\\d+) ComponentCollection=(\\d+)", testWorld, testWorld.ClientWorlds[0]);
                }

                // Allow disconnect to happen
                for (int i = 0; i < 16; ++i)
                    testWorld.Tick(16f/1000f);

                // Verify client connection is disconnected
                var query = testWorld.ClientWorlds[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                Assert.AreEqual(0, query.CalculateEntityCount());
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void DisconnectEventAndRPCVersionErrorProcessedInSameFrame(bool checkServer)
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                testWorld.Bootstrap(true);
                testWorld.CreateWorlds(true, 1, false);

                float dt = 16f / 1000f;
                var entity = testWorld.ClientWorlds[0].EntityManager.CreateEntity(ComponentType.ReadWrite<GameProtocolVersion>());
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(entity, new GameProtocolVersion(){Version = 9000});
                entity = testWorld.ClientWorlds[0].EntityManager.CreateEntity(ComponentType.ReadWrite<NetCodeDebugConfig>());
                testWorld.ClientWorlds[0].EntityManager.SetComponentData(entity, new NetCodeDebugConfig(){LogLevel = checkServer ? NetDebug.LogLevelType.Exception : NetDebug.LogLevelType.Debug});
                entity = testWorld.ServerWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<NetCodeDebugConfig>());
                testWorld.ServerWorld.EntityManager.SetComponentData(entity, new NetCodeDebugConfig(){LogLevel = checkServer ? NetDebug.LogLevelType.Debug : NetDebug.LogLevelType.Exception});
                testWorld.Tick(dt);

                var ep = NetworkEndpoint.LoopbackIpv4;
                ep.Port = 7979;
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ServerWorld).ValueRW.Listen(ep);
                testWorld.GetSingletonRW<NetworkStreamDriver>(testWorld.ClientWorlds[0]).ValueRW.Connect(testWorld.ClientWorlds[0].EntityManager, ep);
                testWorld.Tick(dt);

                if (checkServer)
                {
                    // Connection is not included in the version error handling, so connection ID appears as -1 here
                    LogAssert.Expect(LogType.Error,
                        "[ServerTest] RpcSystem received bad protocol version from connection 0");
                    LogExpectProtocolError(
                        "NetCode=1 Game=0 RpcCollection=(\\d+) ComponentCollection=(\\d+)",
                        "NetCode=1 Game=9000 RpcCollection=(\\d+) ComponentCollection=(\\d+)", testWorld, testWorld.ServerWorld);
                }
                else
                {
                    LogAssert.Expect(LogType.Error,
                        "[ClientTest0] RpcSystem received bad protocol version from connection 0");
                    LogExpectProtocolError(
                        "NetCode=1 Game=9000 RpcCollection=(\\d+) ComponentCollection=(\\d+)",
                        "NetCode=1 Game=0 RpcCollection=(\\d+) ComponentCollection=(\\d+)", testWorld, testWorld.ClientWorlds[0]);
                }
                for (int i = 0; i < 8; ++i)
                    testWorld.Tick(dt);

                // Verify client connection is disconnected
                var query = testWorld.ClientWorlds[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                Assert.AreEqual(0, query.CalculateEntityCount());
            }
        }

        void LogExpectProtocolError(string localProtocol, string remoteProtocol, NetCodeTestWorld testWorld, World world)
        {
            LogAssert.Expect(LogType.Error, new Regex($"Local protocol: {localProtocol}"));
            LogAssert.Expect(LogType.Error, new Regex($"Remote protocol: {remoteProtocol}"));
            var rpcs = testWorld.GetSingleton<RpcCollection>(world).Rpcs;
            LogAssert.Expect(LogType.Error, "RPC List: " + rpcs.Length);
            for (int i = 0; i < rpcs.Length; ++i)
                LogAssert.Expect(LogType.Error, new Regex("Unity.NetCode"));
            var collection = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostCollection>());
            var serializers = world.EntityManager.GetBuffer<GhostComponentSerializer.State>(collection.ToEntityArray(Allocator.Temp)[0]);
            LogAssert.Expect(LogType.Error, "Component serializer data: " + serializers.Length);
            for (int i = 0; i < serializers.Length; ++i)
                LogAssert.Expect(LogType.Error, new Regex("Type:"));
        }

        public class TestConverter : TestNetCodeAuthoring.IConverter
        {
            public void Bake(GameObject gameObject, IBaker baker)
            {
                baker.AddComponent(new GhostOwnerComponent());
                baker.AddComponent(new GhostGenTestTypeFlat());
            }
        }
        [Test]
        public void GhostCollectionGenerateSameHashOnClientAndServer()
        {
            using (var testWorld = new NetCodeTestWorld())
            {
                var ghost1 = new GameObject();
                ghost1.AddComponent<TestNetCodeAuthoring>().Converter = new TestConverter();
                ghost1.AddComponent<GhostAuthoringComponent>().DefaultGhostMode = GhostMode.Predicted;
                var ghost2 = new GameObject();
                ghost2.AddComponent<TestNetCodeAuthoring>().Converter = new TestConverter();
                ghost2.AddComponent<GhostAuthoringComponent>().DefaultGhostMode = GhostMode.Interpolated;

                testWorld.Bootstrap(true);
                testWorld.CreateGhostCollection(ghost1, ghost2);

                testWorld.CreateWorlds(true, 1);
                float frameTime = 1.0f / 60.0f;
                var serverCollectionSingleton = testWorld.TryGetSingletonEntity<GhostCollection>(testWorld.ServerWorld);
                var clientCollectionSingleton = testWorld.TryGetSingletonEntity<GhostCollection>(testWorld.ClientWorlds[0]);
                //First tick: compute on both client and server the ghost collection hash
                testWorld.Tick(frameTime);
                Assert.AreEqual(GhostCollectionSystem.CalculateComponentCollectionHash(testWorld.ServerWorld.EntityManager.GetBuffer<GhostComponentSerializer.State>(serverCollectionSingleton)),
                    GhostCollectionSystem.CalculateComponentCollectionHash(testWorld.ClientWorlds[0].EntityManager.GetBuffer<GhostComponentSerializer.State>(clientCollectionSingleton)));

                // compare the list of loaded prefabs
                Assert.AreNotEqual(Entity.Null, serverCollectionSingleton);
                Assert.AreNotEqual(Entity.Null, clientCollectionSingleton);
                var serverCollection = testWorld.ServerWorld.EntityManager.GetBuffer<GhostCollectionPrefab>(serverCollectionSingleton);
                var clientCollection = testWorld.ClientWorlds[0].EntityManager.GetBuffer<GhostCollectionPrefab>(clientCollectionSingleton);
                Assert.AreEqual(serverCollection.Length, clientCollection.Length);
                for (int i = 0; i < serverCollection.Length; ++i)
                {
                    Assert.AreEqual(serverCollection[i].GhostType, clientCollection[i].GhostType);
                    Assert.AreEqual(serverCollection[i].Hash, clientCollection[i].Hash);
                }

                //Check that and server can connect (same component hash)
                Assert.IsTrue(testWorld.Connect(frameTime, 4));

                testWorld.GoInGame();
                for(int i=0;i<10;++i)
                    testWorld.Tick(frameTime);

                Assert.IsTrue(testWorld.TryGetSingletonEntity<NetworkIdComponent>(testWorld.ClientWorlds[0]) != Entity.Null);
            }
        }
    }
}
