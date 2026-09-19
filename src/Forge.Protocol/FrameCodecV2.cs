using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public enum SessionLane : byte
    {
        State = 0,
        Control = 1,
        Bulk = 2,
        Presentation = 3
    }

    public enum MessageKindV2 : ushort
    {
        Intent = 1,
        IntentReceipt = 2,
        AuthorityBatch = 3,
        AppliedAck = 4,
        SnapshotOffer = 5,
        SnapshotChunk = 6,
        SnapshotProgress = 7,
        WorldInstalled = 8,
        ReplayBarrier = 9,
        BarrierAck = 10,
        ActivationGrant = 11,
        Activated = 12,
        GapRequest = 13,
        ResyncRequired = 14,
        CancelJoin = 15,
        Cancelled = 16,
        Heartbeat = 17,
        Progress = 18,
        PermissionChanged = 19,
        SessionClosing = 20,
        RosterSnapshot = 21,
        ChatSubmit = 22,
        ChatEvent = 23,
        PlayerPresentation = 24
    }

    public sealed class SessionFrameV2
    {
        private readonly byte[] payload;
        public SessionLane Lane { get; private set; }
        public MessageKindV2 Kind { get; private set; }
        public SessionStamp Stamp { get; private set; }
        public Guid ConnectionBinding { get; private set; }
        public ulong Sequence { get; private set; }
        public Guid CorrelationId { get; private set; }
        public ushort PayloadSchemaVersion { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public SessionFrameV2(SessionLane lane, MessageKindV2 kind, SessionStamp stamp, Guid connectionBinding,
            ulong sequence, Guid correlationId, ushort payloadSchemaVersion, byte[] bytes)
        {
            Check.OutOfRange(!Enum.IsDefined(typeof(SessionLane), lane), "lane");
            Check.OutOfRange(!Enum.IsDefined(typeof(MessageKindV2), kind), "kind");
            Check.Condition(!stamp.IsValid, "stamp", "Invalid session stamp.");
            Check.Condition(connectionBinding == Guid.Empty, "connectionBinding", "Missing connection binding.");
            Check.OutOfRange(sequence == 0, "sequence");
            Check.OutOfRange(payloadSchemaVersion == 0, "payloadSchemaVersion");
            if (bytes == null || bytes.Length > Limits.FramePayloadBytes)
                throw new ArgumentException("Invalid frame payload.", "bytes");
            if (ExpectedLane(kind) != lane) throw new ArgumentException("Message kind is not valid on this lane.");
            Lane = lane;
            Kind = kind;
            Stamp = stamp;
            ConnectionBinding = connectionBinding;
            Sequence = sequence;
            CorrelationId = correlationId;
            PayloadSchemaVersion = payloadSchemaVersion;
            payload = (byte[])bytes.Clone();
        }

        public static SessionLane ExpectedLane(MessageKindV2 kind)
        {
            switch (kind)
            {
                case MessageKindV2.AuthorityBatch:
                case MessageKindV2.ReplayBarrier:
                case MessageKindV2.ActivationGrant:
                    return SessionLane.State;
                case MessageKindV2.SnapshotChunk:
                    return SessionLane.Bulk;
                case MessageKindV2.PlayerPresentation:
                    return SessionLane.Presentation;
                default:
                    return SessionLane.Control;
            }
        }
    }

    /// <summary>Protocol-v2 fixed header. Integrity digest is not peer authentication.</summary>
    public static class SessionFrameCodecV2
    {
        public const int HeaderBytes = 88;
        public const int TrailerBytes = Hash256.Size;
        public const ushort Major = 2;
        public const ushort Minor = 0;
        private const uint Magic = 0x32465343; // CSF2 little endian

        public static byte[] Encode(SessionFrameV2 frame)
        {
            Check.NotNull(frame, "frame");
            byte[] payload = frame.Payload;
            byte[] body;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Major);
                writer.Write(Minor);
                writer.Write((ushort)HeaderBytes);
                writer.Write((byte)frame.Lane);
                writer.Write((byte)0);
                writer.Write((ushort)frame.Kind);
                writer.Write((ushort)0);
                writer.Write(GuidBytes(frame.Stamp.WorldId));
                writer.Write(frame.Stamp.Epoch);
                writer.Write(GuidBytes(frame.ConnectionBinding));
                writer.Write(frame.Sequence);
                writer.Write(GuidBytes(frame.CorrelationId));
                writer.Write((uint)payload.Length);
                writer.Write(frame.PayloadSchemaVersion);
                writer.Write((ushort)0);
                writer.Write(payload);
                writer.Flush();
                body = stream.ToArray();
            }
            if (body.Length != HeaderBytes + payload.Length)
                throw new InvalidOperationException("Protocol-v2 header layout drifted.");
            byte[] packet = new byte[body.Length + TrailerBytes];
            Buffer.BlockCopy(body, 0, packet, 0, body.Length);
            Buffer.BlockCopy(Hash256.Compute(body).ToArray(), 0, packet, body.Length, TrailerBytes);
            return packet;
        }

        public static SessionFrameV2 Decode(byte[] packet)
        {
            if (packet == null || packet.Length < HeaderBytes + TrailerBytes ||
                packet.Length > HeaderBytes + Limits.FramePayloadBytes + TrailerBytes)
                throw new InvalidDataException("Frame size is outside the allowed range.");

            byte[] body = new byte[packet.Length - TrailerBytes];
            Buffer.BlockCopy(packet, 0, body, 0, body.Length);
            byte[] trailer = new byte[TrailerBytes];
            Buffer.BlockCopy(packet, body.Length, trailer, 0, trailer.Length);
            if (!Hash256.Compute(body).Equals(new Hash256(trailer)))
                throw new InvalidDataException("Frame digest mismatch.");

            using (BinaryReader reader = new BinaryReader(new MemoryStream(body, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Bad protocol-v2 magic.");
                if (reader.ReadUInt16() != Major || reader.ReadUInt16() != Minor)
                    throw new InvalidDataException("Unsupported protocol-v2 version.");
                if (reader.ReadUInt16() != HeaderBytes) throw new InvalidDataException("Unexpected protocol-v2 header size.");
                SessionLane lane = (SessionLane)reader.ReadByte();
                byte flags = reader.ReadByte();
                MessageKindV2 kind = (MessageKindV2)reader.ReadUInt16();
                ushort reserved = reader.ReadUInt16();
                if (!Enum.IsDefined(typeof(SessionLane), lane) || !Enum.IsDefined(typeof(MessageKindV2), kind) ||
                    flags != 0 || reserved != 0 || SessionFrameV2.ExpectedLane(kind) != lane)
                    throw new InvalidDataException("Invalid lane, kind or flags.");
                Guid world = ReadGuid(reader);
                ulong epoch = reader.ReadUInt64();
                Guid binding = ReadGuid(reader);
                ulong sequence = reader.ReadUInt64();
                Guid correlation = ReadGuid(reader);
                uint payloadBytes = reader.ReadUInt32();
                ushort schema = reader.ReadUInt16();
                if (reader.ReadUInt16() != 0) throw new InvalidDataException("Reserved protocol-v2 field is nonzero.");
                if (world == Guid.Empty || epoch == 0 || binding == Guid.Empty || sequence == 0 || schema == 0)
                    throw new InvalidDataException("Protocol-v2 identity is incomplete.");
                if (payloadBytes > Limits.FramePayloadBytes || body.Length != HeaderBytes + (long)payloadBytes)
                    throw new InvalidDataException("Invalid payload length or trailing bytes.");
                byte[] payload = reader.ReadBytes((int)payloadBytes);
                if (payload.Length != payloadBytes) throw new InvalidDataException("Truncated payload.");
                return new SessionFrameV2(lane, kind, new SessionStamp(world, epoch), binding, sequence,
                    correlation, schema, payload);
            }
        }

        internal static byte[] GuidBytes(Guid value)
        {
            byte[] bytes = value.ToByteArray();
            SwapGuidFields(bytes);
            return bytes;
        }

        internal static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new InvalidDataException("Truncated UUID.");
            SwapGuidFields(bytes);
            return new Guid(bytes);
        }

        private static void SwapGuidFields(byte[] bytes)
        {
            Array.Reverse(bytes, 0, 4);
            Array.Reverse(bytes, 4, 2);
            Array.Reverse(bytes, 6, 2);
        }
    }

    public enum BootstrapKind : ushort
    {
        Hello = 1,
        IdentityProof = 2,
        ManifestPage = 3,
        CompatibilityResult = 4,
        SessionWelcome = 5,
        Reject = 6
    }

    public sealed class BootstrapFrame
    {
        private readonly byte[] payload;
        public BootstrapKind Kind { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public BootstrapFrame(BootstrapKind kind, byte[] bytes)
        {
            Check.OutOfRange(!Enum.IsDefined(typeof(BootstrapKind), kind), "kind");
            if (bytes == null || bytes.Length > BootstrapCodec.MaxPayloadBytes)
                throw new ArgumentException("Bootstrap payload is outside the allowed range.", "bytes");
            Kind = kind;
            payload = (byte[])bytes.Clone();
        }
    }

    /// <summary>Small fixed pre-session codec; it never depends on negotiated world schemas.</summary>
    public static class BootstrapCodec
    {
        public const int HeaderBytes = 16;
        public const int MaxPayloadBytes = 16 * 1024;
        public const ushort Major = 2;
        public const ushort Minor = 0;
        private const uint Magic = 0x42465343; // CSFB little endian

        public static byte[] Encode(BootstrapFrame frame)
        {
            Check.NotNull(frame, "frame");
            byte[] payload = frame.Payload;
            byte[] body;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Major);
                writer.Write(Minor);
                writer.Write((ushort)frame.Kind);
                writer.Write((ushort)0);
                writer.Write((uint)payload.Length);
                writer.Write(payload);
                writer.Flush();
                body = stream.ToArray();
            }
            byte[] packet = new byte[body.Length + Hash256.Size];
            Buffer.BlockCopy(body, 0, packet, 0, body.Length);
            Buffer.BlockCopy(Hash256.Compute(body).ToArray(), 0, packet, body.Length, Hash256.Size);
            return packet;
        }

        public static BootstrapFrame Decode(byte[] packet)
        {
            if (packet == null || packet.Length < HeaderBytes + Hash256.Size ||
                packet.Length > HeaderBytes + MaxPayloadBytes + Hash256.Size)
                throw new InvalidDataException("Bootstrap frame size is outside the allowed range.");
            byte[] body = new byte[packet.Length - Hash256.Size];
            Buffer.BlockCopy(packet, 0, body, 0, body.Length);
            byte[] trailer = new byte[Hash256.Size];
            Buffer.BlockCopy(packet, body.Length, trailer, 0, trailer.Length);
            if (!Hash256.Compute(body).Equals(new Hash256(trailer)))
                throw new InvalidDataException("Bootstrap digest mismatch.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(body, false)))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Major || reader.ReadUInt16() != Minor)
                    throw new InvalidDataException("Unsupported bootstrap protocol.");
                BootstrapKind kind = (BootstrapKind)reader.ReadUInt16();
                if (!Enum.IsDefined(typeof(BootstrapKind), kind) || reader.ReadUInt16() != 0)
                    throw new InvalidDataException("Invalid bootstrap kind or flags.");
                uint length = reader.ReadUInt32();
                if (length > MaxPayloadBytes || body.Length != HeaderBytes + (long)length)
                    throw new InvalidDataException("Invalid bootstrap payload length or trailing bytes.");
                byte[] payload = reader.ReadBytes((int)length);
                if (payload.Length != length) throw new InvalidDataException("Truncated bootstrap payload.");
                return new BootstrapFrame(kind, payload);
            }
        }
    }
}
