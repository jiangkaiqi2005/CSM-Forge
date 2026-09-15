using System;
using System.IO;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class ProtocolV2Tests
    {
        [Case]
        public static void SessionFrameRoundTripsFixedIdentityAndPayload()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 9);
            Guid binding = Guid.NewGuid();
            Guid correlation = Guid.NewGuid();
            SessionFrameV2 source = new SessionFrameV2(SessionLane.Control, MessageKindV2.Intent,
                stamp, binding, 17, correlation, 3, new byte[] { 1, 2, 3, 4 });
            byte[] packet = SessionFrameCodecV2.Encode(source);
            Assert.Equal(SessionFrameCodecV2.HeaderBytes + 4 + Hash256.Size, packet.Length);

            SessionFrameV2 decoded = SessionFrameCodecV2.Decode(packet);
            Assert.Equal(SessionLane.Control, decoded.Lane);
            Assert.Equal(MessageKindV2.Intent, decoded.Kind);
            Assert.Equal(stamp, decoded.Stamp);
            Assert.Equal(binding, decoded.ConnectionBinding);
            Assert.Equal((ulong)17, decoded.Sequence);
            Assert.Equal(correlation, decoded.CorrelationId);
            Assert.Equal((ushort)3, decoded.PayloadSchemaVersion);
            Assert.Equal(4, decoded.Payload.Length);
        }

        [Case]
        public static void SessionFrameRejectsCorruptionAndWrongLane()
        {
            SessionFrameV2 source = new SessionFrameV2(SessionLane.State, MessageKindV2.AuthorityBatch,
                new SessionStamp(Guid.NewGuid(), 1), Guid.NewGuid(), 1, Guid.Empty, 1, new byte[] { 7 });
            byte[] packet = SessionFrameCodecV2.Encode(source);
            packet[20] ^= 0x10;
            Assert.Throws<InvalidDataException>(delegate { SessionFrameCodecV2.Decode(packet); });

            Assert.Throws<ArgumentException>(delegate
            {
                new SessionFrameV2(SessionLane.Control, MessageKindV2.AuthorityBatch,
                    new SessionStamp(Guid.NewGuid(), 1), Guid.NewGuid(), 1, Guid.Empty, 1, new byte[0]);
            });
        }

        [Case]
        public static void SessionFrameRejectsTrailingBytesBeforeAllocation()
        {
            SessionFrameV2 source = new SessionFrameV2(SessionLane.Control, MessageKindV2.Heartbeat,
                new SessionStamp(Guid.NewGuid(), 2), Guid.NewGuid(), 1, Guid.Empty, 1, new byte[] { 1 });
            byte[] packet = SessionFrameCodecV2.Encode(source);
            byte[] longer = new byte[packet.Length + 1];
            Buffer.BlockCopy(packet, 0, longer, 0, packet.Length);
            Assert.Throws<InvalidDataException>(delegate { SessionFrameCodecV2.Decode(longer); });
        }

        [Case]
        public static void BootstrapIsIndependentAndBounded()
        {
            BootstrapFrame hello = new BootstrapFrame(BootstrapKind.Hello, new byte[] { 1, 4, 9 });
            byte[] packet = BootstrapCodec.Encode(hello);
            BootstrapFrame decoded = BootstrapCodec.Decode(packet);
            Assert.Equal(BootstrapKind.Hello, decoded.Kind);
            Assert.Equal(3, decoded.Payload.Length);
            Assert.Throws<ArgumentException>(delegate
            {
                new BootstrapFrame(BootstrapKind.ManifestPage, new byte[BootstrapCodec.MaxPayloadBytes + 1]);
            });
        }

        [Case]
        public static void ProtocolV2RequiresFreshConnectionBindingAndSequence()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            Assert.Throws<ArgumentException>(delegate
            {
                new SessionFrameV2(SessionLane.Control, MessageKindV2.Heartbeat, stamp,
                    Guid.Empty, 1, Guid.Empty, 1, new byte[0]);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                new SessionFrameV2(SessionLane.Control, MessageKindV2.Heartbeat, stamp,
                    Guid.NewGuid(), 0, Guid.Empty, 1, new byte[0]);
            });
        }
    }
}
