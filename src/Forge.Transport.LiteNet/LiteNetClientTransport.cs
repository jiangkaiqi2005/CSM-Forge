using CsmForge.Core;
using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace CsmForge.Transport.LiteNet
{
    public sealed class LiteNetClientTransport : LiteNetTransportBase
    {
        private NetPeer server;
        private Guid connectionId;

        public LiteNetClientTransport() : base(DefaultEventLimit, DefaultQueueBytes) { }
        public LiteNetClientTransport(int eventLimit, int queueBytes) : base(eventLimit, queueBytes) { }

        public bool Start(IPEndPoint endpoint, string key)
        {
            Check.NotNull(endpoint, "endpoint");
            if (string.IsNullOrEmpty(key) || key.Length > 128) throw new ArgumentException("Invalid room key.", "key");
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager manager = new NetManager(listener) { AutoRecycle = true }; // WP-1.7: recycle pooled packets per receive
            listener.PeerConnectedEvent += OnConnected;
            listener.PeerDisconnectedEvent += OnDisconnected;
            listener.NetworkReceiveEvent += OnReceive;
            listener.NetworkErrorEvent += OnError;

            lock (Gate)
            {
                if (Manager != null) throw new InvalidOperationException("Client transport already started.");
                Manager = manager;
                connectionId = Guid.NewGuid();
                if (!manager.Start())
                {
                    Manager = null;
                    return false;
                }
                server = manager.Connect(endpoint, key);
                if (server == null)
                {
                    manager.Stop();
                    Manager = null;
                    return false;
                }
            }
            StartWorker();
            return true;
        }

        public bool TrySend(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPacketBytes) return false;
            lock (Gate)
            {
                if (server == null || server.ConnectionState != ConnectionState.Connected) return false;
                server.Send(bytes, DeliveryMethod.ReliableOrdered);
                return true;
            }
        }

        public override void Stop()
        {
            lock (Gate) server = null;
            base.Stop();
        }

        private void OnConnected(NetPeer peer)
        {
            lock (Gate)
            {
                if (server == null) server = peer;
                if (!ReferenceEquals(server, peer))
                {
                    peer.Disconnect();
                    return;
                }
            }
            if (!Enqueue(new TransportEvent(TransportEventKind.Connected, connectionId, null, null))) peer.Disconnect();
        }

        private void OnDisconnected(NetPeer peer, DisconnectInfo info)
        {
            bool ours;
            lock (Gate)
            {
                ours = ReferenceEquals(server, peer);
                if (ours) server = null;
            }
            if (ours)
                Enqueue(new TransportEvent(TransportEventKind.Disconnected, connectionId, null, info.Reason.ToString()));
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            lock (Gate) if (!ReferenceEquals(server, peer)) return;
            if (deliveryMethod != DeliveryMethod.ReliableOrdered)
            {
                peer.Disconnect();
                return;
            }
            byte[] bytes = ReadPacket(reader);
            if (bytes == null || !Enqueue(new TransportEvent(TransportEventKind.Data, connectionId, bytes, null)))
                peer.Disconnect();
        }

        private void OnError(IPEndPoint endpoint, SocketError error)
        {
            Enqueue(new TransportEvent(TransportEventKind.Error, connectionId, null,
                (endpoint == null ? "unconnected" : endpoint.ToString()) + ":" + error));
        }
    }
}
