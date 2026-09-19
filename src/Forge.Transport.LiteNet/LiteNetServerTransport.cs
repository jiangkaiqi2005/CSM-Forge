using CsmForge.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace CsmForge.Transport.LiteNet
{
    public sealed class LiteNetServerTransport : LiteNetTransportBase
    {
        private sealed class PeerState
        {
            public NetPeer Peer;
            public Guid ConnectionId;
        }

        private readonly Dictionary<int, PeerState> peers = new Dictionary<int, PeerState>();
        private string roomKey;

        public LiteNetServerTransport() : base(DefaultEventLimit, DefaultQueueBytes) { }
        public LiteNetServerTransport(int eventLimit, int queueBytes) : base(eventLimit, queueBytes) { }

        public bool Start(int port, string key)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            Check.Condition(string.IsNullOrEmpty(key) || key.Length > 128, "key", "Invalid room key.");
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager manager = new NetManager(listener) { AutoRecycle = true }; // WP-1.7: recycle pooled packets per receive
            listener.ConnectionRequestEvent += delegate(ConnectionRequest request) { request.AcceptIfKey(roomKey); };
            listener.PeerConnectedEvent += OnConnected;
            listener.PeerDisconnectedEvent += OnDisconnected;
            listener.NetworkReceiveEvent += OnReceive;
            listener.NetworkErrorEvent += OnError;

            lock (Gate)
            {
                if (Manager != null) throw new InvalidOperationException("Server transport already started.");
                roomKey = key;
                Manager = manager;
                if (!manager.Start(port))
                {
                    Manager = null;
                    roomKey = null;
                    return false;
                }
            }
            StartWorker();
            return true;
        }

        public bool TrySend(Guid connectionId, byte[] bytes)
        {
            if (connectionId == Guid.Empty || bytes == null || bytes.Length == 0 || bytes.Length > MaxPacketBytes) return false;
            lock (Gate)
            {
                foreach (PeerState state in peers.Values)
                {
                    if (state.ConnectionId != connectionId) continue;
                    if (state.Peer.ConnectionState != ConnectionState.Connected) return false;
                    state.Peer.Send(bytes, DeliveryMethod.ReliableOrdered);
                    return true;
                }
            }
            return false;
        }

        public int ReliableQueuePackets(Guid connectionId)
        {
            if (connectionId == Guid.Empty) return int.MaxValue;
            lock (Gate)
            {
                foreach (PeerState state in peers.Values)
                {
                    if (state.ConnectionId != connectionId) continue;
                    if (state.Peer.ConnectionState != ConnectionState.Connected) return int.MaxValue;
                    return state.Peer.GetPacketsCountInReliableQueue(0, true);
                }
            }
            return int.MaxValue;
        }

        public void Disconnect(Guid connectionId)
        {
            lock (Gate)
            {
                foreach (PeerState state in peers.Values)
                {
                    if (state.ConnectionId == connectionId)
                    {
                        state.Peer.Disconnect();
                        return;
                    }
                }
            }
        }

        public override void Stop()
        {
            lock (Gate) peers.Clear();
            base.Stop();
            roomKey = null;
        }

        private void OnConnected(NetPeer peer)
        {
            Guid id = Guid.NewGuid();
            lock (Gate) peers[peer.Id] = new PeerState { Peer = peer, ConnectionId = id };
            if (!Enqueue(new TransportEvent(TransportEventKind.Connected, id, null, null))) peer.Disconnect();
        }

        private void OnDisconnected(NetPeer peer, DisconnectInfo info)
        {
            PeerState state = null;
            lock (Gate)
            {
                PeerState current;
                if (peers.TryGetValue(peer.Id, out current) && ReferenceEquals(current.Peer, peer))
                {
                    state = current;
                    peers.Remove(peer.Id);
                }
            }
            if (state != null)
                Enqueue(new TransportEvent(TransportEventKind.Disconnected, state.ConnectionId, null, info.Reason.ToString()));
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            PeerState state;
            lock (Gate)
            {
                if (!peers.TryGetValue(peer.Id, out state) || !ReferenceEquals(state.Peer, peer)) return;
            }
            if (deliveryMethod != DeliveryMethod.ReliableOrdered)
            {
                peer.Disconnect();
                return;
            }
            byte[] bytes = ReadPacket(reader);
            if (bytes == null || !Enqueue(new TransportEvent(TransportEventKind.Data, state.ConnectionId, bytes, null)))
                peer.Disconnect();
        }

        private void OnError(IPEndPoint endpoint, SocketError error)
        {
            Enqueue(new TransportEvent(TransportEventKind.Error, Guid.Empty, null,
                (endpoint == null ? "unconnected" : endpoint.ToString()) + ":" + error));
        }
    }
}
