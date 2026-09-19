using System;
using System.IO;

namespace CsmForge.Core
{
    public static class Limits
    {
        public const int CommandBytes = 4096;
        public const int FramePayloadBytes = 65536;
        public const int SnapshotBytes = 64 * 1024 * 1024;
        public const int Peers = 8;
        public const int ReceiptsPerPeer = 128;
        public const int JournalEntries = 512;
    }

    public struct SessionStamp : IEquatable<SessionStamp>
    {
        public readonly Guid WorldId;
        public readonly ulong Epoch;

        public SessionStamp(Guid worldId, ulong epoch)
        {
            if (worldId == Guid.Empty || epoch == 0)
                throw new ArgumentException("A session needs a world identity and nonzero incarnation.");
            WorldId = worldId;
            Epoch = epoch;
        }

        public bool IsValid { get { return WorldId != Guid.Empty && Epoch != 0; } }
        public bool Equals(SessionStamp other) { return WorldId == other.WorldId && Epoch == other.Epoch; }
        public override bool Equals(object obj) { return obj is SessionStamp && Equals((SessionStamp)obj); }
        public override int GetHashCode() { return WorldId.GetHashCode() ^ Epoch.GetHashCode(); }
    }

    /// <summary>
    /// Shared guard helpers (D2): every call preserves the exact exception type the inline check
    /// used to throw, so converting a call site changes no observable behavior. Public because
    /// Protocol and Runtime constructor validation converts to the same helpers.
    /// </summary>
    public static class Check
    {
        public static byte[] Copy(byte[] bytes, int maximum, bool allowEmpty)
        {
            Check.NotNull(bytes, "bytes");
            if (bytes.Length > maximum || (!allowEmpty && bytes.Length == 0))
                throw new ArgumentException("Payload length is outside the permitted bounds.", "bytes");
            return (byte[])bytes.Clone();
        }

        public static void Stamp(SessionStamp stamp)
        {
            Check.Condition(!stamp.IsValid, "stamp", "Uninitialized session stamp.");
        }

        public static void NotNull(object value, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
        }

        /// <summary>WP-2: pass the VIOLATION condition - the guard throws when it is true.</summary>
        public static void Condition(bool violation, string name, string message)
        {
            if (violation) throw new ArgumentException(message, name);
        }

        public static void InRange(long value, long minimum, long maximum, string name)
        {
            if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
        }

        public static void CanonicalId(string value, int maximumLength, string name, string message)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength) throw new ArgumentException(message, name);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == ':' || ch == '-' || ch == '_'))
                    throw new ArgumentException(message, name);
            }
        }
    }

    public sealed class Intent
    {
        private readonly byte[] payload;
        public SessionStamp Stamp { get; private set; }
        public ulong RequestId { get; private set; }
        public ulong ExpectedRevision { get; private set; }
        public Hash256 Fingerprint { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public Intent(SessionStamp stamp, ulong requestId, ulong expectedRevision, byte[] bytes)
        {
            Check.Stamp(stamp);
            if (requestId == 0) throw new ArgumentOutOfRangeException("requestId");
            Stamp = stamp;
            RequestId = requestId;
            ExpectedRevision = expectedRevision;
            payload = Check.Copy(bytes, Limits.CommandBytes, false);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(requestId);
                writer.Write(expectedRevision);
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                Fingerprint = Hash256.Compute(stream.ToArray());
            }
        }
    }

    /// <summary>A host outcome, not a replay of a client's tool invocation.</summary>
    public sealed class Commit
    {
        private readonly byte[] payload;
        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Guid Origin { get; private set; }
        public ulong RequestId { get; private set; }
        public Hash256 BeforeHash { get; private set; }
        public Hash256 AfterHash { get; private set; }
        public Hash256 Fingerprint { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public Commit(SessionStamp stamp, ulong revision, Guid origin, ulong requestId,
            Hash256 beforeHash, Hash256 afterHash, byte[] bytes)
        {
            Check.Stamp(stamp);
            if (revision == 0 || requestId == 0 || origin == Guid.Empty)
                throw new ArgumentException("Commit identity is incomplete.");
            Check.NotNull(beforeHash, "beforeHash"); Check.NotNull(afterHash, "afterHash"); // WP-2: per-argument reporting
            Stamp = stamp;
            Revision = revision;
            Origin = origin;
            RequestId = requestId;
            BeforeHash = beforeHash;
            AfterHash = afterHash;
            payload = Check.Copy(bytes, Limits.CommandBytes, false);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(stamp.WorldId.ToByteArray());
                writer.Write(stamp.Epoch);
                writer.Write(revision);
                writer.Write(origin.ToByteArray());
                writer.Write(requestId);
                writer.Write(beforeHash.ToArray());
                writer.Write(afterHash.ToArray());
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                Fingerprint = Hash256.Compute(stream.ToArray());
            }
        }
    }

    /// <summary>Content integrity and canonical world-state equality are distinct hashes.</summary>
    public sealed class WorldImage
    {
        private readonly byte[] bytes;
        public Hash256 ContentHash { get; private set; }
        public Hash256 StateHash { get; private set; }
        public byte[] Bytes { get { return (byte[])bytes.Clone(); } }

        public WorldImage(byte[] data, Hash256 contentHash, Hash256 stateHash)
        {
            Check.NotNull(contentHash, "contentHash"); Check.NotNull(stateHash, "stateHash"); // WP-2: per-argument reporting
            bytes = Check.Copy(data, Limits.SnapshotBytes, false);
            ContentHash = contentHash;
            StateHash = stateHash;
        }

        public bool HasValidContent() { return ContentHash.Equals(Hash256.Compute(bytes)); }
    }

    public sealed class WorldSnapshot
    {
        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Guid TransferId { get; private set; }
        public WorldImage Image { get; private set; }

        public WorldSnapshot(SessionStamp stamp, ulong revision, Guid transferId, WorldImage image)
        {
            Check.Stamp(stamp);
            Check.Condition(transferId == Guid.Empty, "transferId", "Missing transfer identity.");
            Check.NotNull(image, "image");
            Stamp = stamp;
            Revision = revision;
            TransferId = transferId;
            Image = image;
        }
    }
}
