using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public enum MessageKind : ushort
    {
        Intent = 1, Commit = 2, SnapshotChunk = 3, ReadyBarrier = 4, Heartbeat = 5
    }

    public sealed class Frame
    {
        private readonly byte[] payload;
        public MessageKind Kind { get; private set; }
        public SessionStamp Stamp { get; private set; }
        public ulong Sequence { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public Frame(MessageKind kind, SessionStamp stamp, ulong sequence, byte[] bytes)
        {
            Check.OutOfRange(!Enum.IsDefined(typeof(MessageKind), kind), "kind");
            Check.Condition(!stamp.IsValid, "stamp", "Invalid session stamp.");
            if (bytes == null || bytes.Length > Limits.FramePayloadBytes)
                throw new ArgumentException("Invalid frame payload.", "bytes");
            Kind = kind;
            Stamp = stamp;
            Sequence = sequence;
            payload = (byte[])bytes.Clone();
        }
    }

    /// <summary>
    /// Fixed, bounded binary framing. The SHA-256 trailer detects corruption; a transport
    /// must still authenticate the peer and protect the channel before accepting frames.
    /// </summary>
    public static class FrameCodec
    {
        public const int HeaderBytes = 48;
        public const ushort Major = 1;
        public const ushort Minor = 0;
        private const uint Magic = 0x47465343; // CSFG

        public static byte[] Encode(Frame frame)
        {
            Check.NotNull(frame, "frame");
            byte[] body;
            byte[] payload = frame.Payload;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Major);
                writer.Write(Minor);
                writer.Write((ushort)frame.Kind);
                writer.Write((ushort)0); // reserved flags
                writer.Write(GuidBytes(frame.Stamp.WorldId));
                writer.Write(frame.Stamp.Epoch);
                writer.Write(frame.Sequence);
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

        public static Frame Decode(byte[] packet)
        {
            if (packet == null || packet.Length < HeaderBytes + Hash256.Size ||
                packet.Length > HeaderBytes + Limits.FramePayloadBytes + Hash256.Size)
                throw new InvalidDataException("Frame size is outside the allowed range.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(packet, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Bad frame magic.");
                if (reader.ReadUInt16() != Major || reader.ReadUInt16() != Minor)
                    throw new InvalidDataException("Unsupported protocol version.");
                MessageKind kind = (MessageKind)reader.ReadUInt16();
                if (!Enum.IsDefined(typeof(MessageKind), kind) || reader.ReadUInt16() != 0)
                    throw new InvalidDataException("Unknown message kind or flags.");
                Guid world = ReadGuid(reader);
                ulong epoch = reader.ReadUInt64();
                ulong sequence = reader.ReadUInt64();
                uint length = reader.ReadUInt32();
                if (length > Limits.FramePayloadBytes || packet.Length != HeaderBytes + (long)length + Hash256.Size)
                    throw new InvalidDataException("Invalid payload length or trailing bytes.");
                if (world == Guid.Empty || epoch == 0) throw new InvalidDataException("Invalid session identity.");
                // Bounds were checked before allocating any payload-dependent arrays.
                byte[] body = new byte[packet.Length - Hash256.Size];
                Buffer.BlockCopy(packet, 0, body, 0, body.Length);
                byte[] trailer = new byte[Hash256.Size];
                Buffer.BlockCopy(packet, body.Length, trailer, 0, trailer.Length);
                if (!Hash256.Compute(body).Equals(new Hash256(trailer)))
                    throw new InvalidDataException("Frame digest mismatch.");
                return new Frame(kind, new SessionStamp(world, epoch), sequence, reader.ReadBytes((int)length));
            }
        }

        public static byte[] EncodeIntent(Intent intent)
        {
            Check.NotNull(intent, "intent");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(intent.ExpectedRevision);
                writer.Write(intent.Payload);
                writer.Flush();
                return Encode(new Frame(MessageKind.Intent, intent.Stamp, intent.RequestId, stream.ToArray()));
            }
        }

        public static Intent DecodeIntent(byte[] packet)
        {
            Frame frame = Decode(packet);
            byte[] payload = frame.Payload;
            if (frame.Kind != MessageKind.Intent || frame.Sequence == 0 || payload.Length < 9 ||
                payload.Length > 8 + Limits.CommandBytes)
                throw new InvalidDataException("Invalid intent frame.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(payload, false)))
                return new Intent(frame.Stamp, frame.Sequence, reader.ReadUInt64(), reader.ReadBytes(payload.Length - 8));
        }

        public static byte[] EncodeCommit(Commit commit)
        {
            Check.NotNull(commit, "commit");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(GuidBytes(commit.Origin));
                writer.Write(commit.RequestId);
                writer.Write(commit.BeforeHash.ToArray());
                writer.Write(commit.AfterHash.ToArray());
                writer.Write(commit.Payload);
                writer.Flush();
                return Encode(new Frame(MessageKind.Commit, commit.Stamp, commit.Revision, stream.ToArray()));
            }
        }

        public static Commit DecodeCommit(byte[] packet)
        {
            Frame frame = Decode(packet);
            byte[] payload = frame.Payload;
            if (frame.Kind != MessageKind.Commit || frame.Sequence == 0 || payload.Length < 89 ||
                payload.Length > 88 + Limits.CommandBytes)
                throw new InvalidDataException("Invalid commit frame.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(payload, false)))
            {
                Guid origin = ReadGuid(reader);
                ulong requestId = reader.ReadUInt64();
                if (origin == Guid.Empty || requestId == 0) throw new InvalidDataException("Invalid commit origin.");
                Hash256 before = new Hash256(reader.ReadBytes(Hash256.Size));
                Hash256 after = new Hash256(reader.ReadBytes(Hash256.Size));
                return new Commit(frame.Stamp, frame.Sequence, origin, requestId, before, after,
                    reader.ReadBytes(payload.Length - 88));
            }
        }

        // RFC 4122 byte order, independent of Guid.ToByteArray's mixed-endian layout.
        private static byte[] GuidBytes(Guid value)
        {
            byte[] bytes = value.ToByteArray();
            SwapGuidFields(bytes);
            return bytes;
        }

        private static Guid ReadGuid(BinaryReader reader)
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
}
