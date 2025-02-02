﻿using Unity.Entities;
using UnityEngine;

namespace Unity.NetCode
{
    /// <summary>
    /// Add this component to a gameobject present in a sub-scene to configure the <see cref="NetDebug"/> logging level and
    /// enable packet dumps.
    /// </summary>
    public class NetCodeDebugConfigAuthoring : MonoBehaviour
    {
        /// <summary>
        /// The current debug level used by netcode.
        /// </summary>
        public NetDebug.LogLevelType LogLevel = NetDebug.LogLevelType.Notify;
        /// <summary>
        /// Enable/Disable per connection packet dumps. When enabled, for each connection a file is created containing all the packet sent
        /// (for the server) or received (for the client).
        /// The packet dump use quite a lot of resources and should be used mostly (if not only) for debugging replication issues.
        /// </summary>
        public bool DumpPackets;
    }

    class NetCodeDebugConfigAuthoringBaker : Baker<NetCodeDebugConfigAuthoring>
    {
        public override void Bake(NetCodeDebugConfigAuthoring authoring)
        {
            AddComponent(new NetCodeDebugConfig
            {
                LogLevel = authoring.LogLevel,
                DumpPackets = authoring.DumpPackets
            });
        }
    }
}
