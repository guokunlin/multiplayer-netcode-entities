using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Error;

namespace Unity.NetCode
{
    ///<summary>
    /// A connection is represented by an entity having a NetworkStreamConnection.
    /// The component hold a reference to the underlying transport <see cref="NetworkConnection"/> and the <see cref="NetworkDriver"/>
    /// that created it.
    /// All connections share a common set of components:
    /// <para>- <see cref="NetworkIdComponent"/></para>
    /// <para>- <see cref="IncomingRpcDataStreamBufferComponent"/></para>
    /// <para>- <see cref="OutgoingCommandDataStreamBufferComponent"/></para>
    /// <para>- <see cref="OutgoingRpcDataStreamBufferComponent"/></para>
    /// <para>- <see cref="PrespawnSectionAck"/></para>
    /// <para>- <see cref="CommandTargetComponent"/></para>
    /// Client connections also have a <see cref="IncomingSnapshotDataStreamBufferComponent"/> to handle server ghost snapshots.
    ///</summary>
    public struct NetworkStreamConnection : IComponentData
    {
        /// <summary>
        /// The underlyng transport <see cref="NetworkConnection"/>
        /// </summary>
        public NetworkConnection Value;
        /// <summary>
        /// The driver identifier that create the connection. Can be used to retrieve the <see cref="NetworkDriver"/> or the
        /// <see cref="NetworkDriverStore.NetworkDriverInstance"/> from the <see cref="NetworkDriverStore"/>.
        /// </summary>
        public int DriverId;
        /// <summary>
        /// Flag used to mark if the connection has exchanged the protocol version.
        /// 1 indicates that the remote version protocol has been received.
        /// </summary>
        public int ProtocolVersionReceived;
    }

    /// <summary>
    /// A component used to signal that a connection should send and receive snapshots and commands.
    /// Before adding this component the connection only processes RPCs. Must be Added by game logic to start sending snapshots and commands.
    /// </summary>
    public struct NetworkStreamInGame : IComponentData
    {
    }

    /// <summary>
    /// Used to tell the <see cref="GhostSendSystem"/> on the server to use a non-default packet size for snapshots.
    /// Must be added by game logic to change snapshot packet size.
    /// </summary>
    public struct NetworkStreamSnapshotTargetSize : IComponentData
    {
        /// <summary>
        /// The desired packet size to use for the snapshot. By default the packet size is the <see cref="NetworkParameterConstants.MTU"/>
        /// minus some header.
        /// It is possible to specify a packet size larger than a single <see cref="NetworkParameterConstants.MTU"/>, in which case the
        /// snapshot data are sent using a pipeline that support fragmentation (see <see cref="NetworkDriverStore.NetworkDriverInstance.unreliableFragmentedPipeline"/>.
        /// The upper bound limit for this value is payload capacity of the fragmentation pipeline (see <see cref="Unity.Networking.Transport.Utilities.FragmentationUtility"/>).
        /// </summary>
        public int Value;
    }

    /// <inheritdoc cref="DisconnectReason"/>
    /// <remarks>Maps directly to <see cref="DisconnectReason"/>, with NetCode specific additions.</remarks>
    public enum NetworkStreamDisconnectReason
    {
        /// <inheritdoc cref="DisconnectReason.Default"/>
        ConnectionClose,
        /// <inheritdoc cref="DisconnectReason.Timeout"/>
        Timeout,
        /// <inheritdoc cref="DisconnectReason.MaxConnectionAttempts"/>
        MaxConnectionAttempts,
        /// <inheritdoc cref="DisconnectReason.ClosedByRemote"/>
        ClosedByRemote,
        /// <summary>NetCode-specific: Denotes that we've detected an unknown or unexpected ghost hash, implying that this is an incompatible server/client pair.</summary>
        BadProtocolVersion,
        /// <summary>NetCode-specific: Denotes that we've detected a hash miss-match in an RPC, or an unknown RPC. Implies that this is an incompatible server/client pair.</summary>
        InvalidRpc,
    }

    /// <summary>
    /// An optional cleanup component that can be added to a newly created connection to monitor its state changes.
    /// Must be added and removed by the gameplay logic. When the <see cref="ConnectionState"/> is present, the NetCode package
    /// will update the component when the connection state changes.
    /// By adding the ConnectionState state component, the connection <see cref="NetworkId"/> and <see cref="DisconnectReason"/>
    /// are retained until the game don't remove the state component.
    /// </summary>
    public struct ConnectionState : ICleanupComponentData
    {
        /// <summary>
        /// The current state of the connection.
        /// </summary>
        public enum State
        {
            /// <summary>
            /// Default, connection not created or not initialized.
            /// </summary>
            Unknown,
            /// <summary>
            /// The connection has been closed.
            /// </summary>
            Disconnected,
            /// <summary>
            /// Client-Only, the connection is trying to contact the server and establish a communication channel.
            /// </summary>
            Connecting,
            /// <summary>
            /// The client connected to the server and is exchanging some initial messages, such as verify the <see cref="NetworkProtocolVersion"/>
            /// and <see cref="GameProtocolVersion"/> are compatible, and assign the network id.
            /// </summary>
            Handshake,
            /// <summary>
            /// The connection has been established, the handshake is termiated and the connection is now fully connected.
            /// </summary>
            Connected
        }

        /// <summary>
        /// The current state of the connection. Updated internally by the <see cref="NetworkStreamReceiveSystem"/>.
        /// </summary>
        public State CurrentState;
        /// <summary>
        /// The id assigned to the connection. Identical to the <see cref="NetworkIdComponent"/> value.
        /// </summary>
        public int NetworkId;
        /// <summary>
        /// Set when the connection is in <see cref="State.Disconnected"/> state, the reason why the connection has been terminated.
        /// </summary>
        public NetworkStreamDisconnectReason DisconnectReason;

        /// <summary>
        /// Check if two connection state are equals. They are if:
        /// <para>- The <see cref="State"/> is the same.</para>
        /// <para>- The <see cref="NetworkId"/> is the same.</para>
        /// <para>- The <see cref="DisconnectReason"/> is the same.</para>
        /// </summary>
        /// <param name="other">The component to compare</param>
        /// <returns></returns>
        public bool Equals(ConnectionState other) => CurrentState == other.CurrentState && NetworkId == other.NetworkId && DisconnectReason == other.DisconnectReason;
    }

    /// <summary>
    /// A component used to signal that the game logic wants to close the connection
    /// </summary>
    public struct NetworkStreamRequestDisconnect : IComponentData
    {
        /// <summary>
        /// Optional, the reason for the disconnection. The default is <see cref="NetworkStreamDisconnectReason.ConnectionClose"/>.
        /// </summary>
        public NetworkStreamDisconnectReason Reason;
    }
    /// <summary>
    /// A component that can be added to a new entity to create a new connection instead of calling <see cref="NetworkStreamDriver.Connect"/>
    /// </summary>
    public struct NetworkStreamRequestConnect : IComponentData
    {
        /// <summary>
        /// The remote server address.
        /// </summary>
        public NetworkEndpoint Endpoint;
    }

    /// <summary>
    /// A component that can be added to a new entity to start listening to a new connection instead of calling <see cref="NetworkStreamDriver.Listen"/>
    /// </summary>
    public struct NetworkStreamRequestListen : IComponentData
    {
        /// <summary>
        /// The remote server address.
        /// </summary>
        public NetworkEndpoint Endpoint;
    }

    /// <summary>
    /// This buffer stores a single incoming command packet. One per NetworkStream (client).
    /// A command packet contains commands for CommandSendSystem.k_InputBufferSendSize (default 4) ticks where 3 of them are delta compressed.
    /// It also contains some timestamps etc for ping calculations.
    /// </summary>
    public struct IncomingCommandDataStreamBufferComponent : IBufferElementData
    {
        /// <summary>
        /// The buffer content
        /// </summary>
        public byte Value;
    }
    /// <summary>
    /// This buffer stores a single outgoing command packet without the headers for timestamps and ping.
    /// A command packet contains commands for CommandSendSystem.k_InputBufferSendSize (default 4) ticks where 3 of them are delta compressed.
    /// It also contains some timestamps etc for ping calculations.
    /// </summary>
    public struct OutgoingCommandDataStreamBufferComponent : IBufferElementData
    {
        /// <summary>
        /// The buffer content
        /// </summary>
        public byte Value;
    }

    /// <summary>
    /// One per NetworkConnection.
    /// Stores the incoming, yet-to-be-processed snapshot stream data for a connection.
    /// Each snapshot is designed to fit inside <see cref="NetworkParameterConstants.MTU"/>,
    /// so expect this to be MTU or less.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct IncomingSnapshotDataStreamBufferComponent : IBufferElementData
    {
        /// <summary>
        /// The buffer content
        /// </summary>
        public byte Value;
    }

    internal static class NetCodeBufferComponentExtensions
    {
        public static unsafe DataStreamReader AsDataStreamReader<T>(this DynamicBuffer<T> self)
            where T: unmanaged, IBufferElementData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (UnsafeUtility.SizeOf<T>() != 1)
                throw new System.InvalidOperationException("Can only convert DynamicBuffers of size 1 to DataStreamWriters");
#endif
            var na = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(self.GetUnsafeReadOnlyPtr(), self.Length, Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(self.AsNativeArray());
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref na, safety);
#endif
            return new DataStreamReader(na);
        }
        public static unsafe void Add<T>(this DynamicBuffer<T> self, ref DataStreamReader reader)
            where T: unmanaged, IBufferElementData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (UnsafeUtility.SizeOf<T>() != 1)
                throw new System.InvalidOperationException("Can only Add to DynamicBuffers of size 1 from DataStreamReaders");
#endif
            var oldLen = self.Length;
            var length = reader.Length - reader.GetBytesRead();
            self.ResizeUninitialized(oldLen + length);
            reader.ReadBytesUnsafe((byte*)self.GetUnsafePtr() + oldLen, length);
        }
    }
}
