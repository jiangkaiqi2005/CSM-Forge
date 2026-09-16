using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CsmForge.Transport.LiteNet;

namespace CsmForge.Tests
{
    public static class LiteNetTransportTests
    {
        [Case]
        public static void LoopbackCarriesReliableBytesWithoutBusinessDispatch()
        {
            int port = FreeUdpPort();
            using (LiteNetServerTransport server = new LiteNetServerTransport())
            using (LiteNetClientTransport client = new LiteNetClientTransport())
            {
                Assert.True(server.Start(port, "forge-test-room"));
                Assert.True(client.Start(new IPEndPoint(IPAddress.Loopback, port), "forge-test-room"));

                TransportEvent serverConnected = Wait(server, TransportEventKind.Connected, 5000);
                TransportEvent clientConnected = Wait(client, TransportEventKind.Connected, 5000);
                Assert.True(serverConnected.ConnectionId != Guid.Empty);
                Assert.True(clientConnected.ConnectionId != Guid.Empty);

                byte[] fromClient = new byte[] { 1, 2, 3, 4 };
                Assert.True(client.TrySend(fromClient));
                TransportEvent receivedByServer = Wait(server, TransportEventKind.Data, 5000);
                Assert.Equal(serverConnected.ConnectionId, receivedByServer.ConnectionId);
                Assert.Equal(4, receivedByServer.Payload.Length);
                Assert.Equal((byte)3, receivedByServer.Payload[2]);

                byte[] fromServer = new byte[] { 9, 8, 7 };
                Assert.True(server.TrySend(serverConnected.ConnectionId, fromServer));
                TransportEvent receivedByClient = Wait(client, TransportEventKind.Data, 5000);
                Assert.Equal(3, receivedByClient.Payload.Length);
                Assert.Equal((byte)7, receivedByClient.Payload[2]);
            }
        }

        [Case]
        public static void TransportRefusesOversizedApplicationPackets()
        {
            using (LiteNetClientTransport client = new LiteNetClientTransport())
                Assert.True(!client.TrySend(new byte[LiteNetTransportBase.MaxPacketBytes + 1]));
        }

        private static TransportEvent Wait(LiteNetTransportBase transport, TransportEventKind kind, int timeoutMilliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                TransportEvent current;
                while (transport.TryTake(out current))
                {
                    if (current.Kind == TransportEventKind.Error)
                        throw new Exception("Transport error while waiting: " + current.Detail);
                    if (current.Kind == kind) return current;
                }
                Thread.Sleep(10);
            }
            throw new Exception("Timed out waiting for transport event " + kind + ".");
        }

        private static int FreeUdpPort()
        {
            UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            try { return ((IPEndPoint)socket.Client.LocalEndPoint).Port; }
            finally { socket.Close(); }
        }
    }
}
