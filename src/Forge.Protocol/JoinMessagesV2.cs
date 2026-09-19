using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public sealed class SnapshotOfferV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public ulong BaselineRevision { get; private set; }
        public Hash256 BaselineRoot { get; private set; }
        public bool RequiresTransfer { get; private set; }
        public Guid SnapshotId { get; private set; }
        public Guid TransferId { get; private set; }
        public ulong ContentBytes { get; private set; }
        public Hash256 ContentHash { get; private set; }

        public SnapshotOfferV2(Guid joinId, uint joinGeneration, ulong baselineRevision, Hash256 baselineRoot,
            bool requiresTransfer, Guid snapshotId, Guid transferId, ulong contentBytes, Hash256 contentHash)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || baselineRoot == null)
                throw new ArgumentException("Snapshot offer identity is incomplete.");
            if (requiresTransfer)
            {
                if (snapshotId == Guid.Empty || transferId == Guid.Empty || contentBytes == 0 || contentHash == null)
                    throw new ArgumentException("Transfer-backed snapshot offer is incomplete.");
            }
            else if (snapshotId != Guid.Empty || transferId != Guid.Empty || contentBytes != 0 || contentHash != null)
                throw new ArgumentException("No-transfer offer must not carry transfer content identity.");
            JoinId = joinId;
            JoinGeneration = joinGeneration;
            BaselineRevision = baselineRevision;
            BaselineRoot = baselineRoot;
            RequiresTransfer = requiresTransfer;
            SnapshotId = snapshotId;
            TransferId = transferId;
            ContentBytes = contentBytes;
            ContentHash = contentHash;
        }
    }

    public sealed class WorldInstalledV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }

        public WorldInstalledV2(Guid joinId, uint joinGeneration, ulong revision, Hash256 root)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || root == null)
                throw new ArgumentException("World-installed identity is incomplete.");
            JoinId = joinId;
            JoinGeneration = joinGeneration;
            Revision = revision;
            Root = root;
        }
    }

    public sealed class ReplayBarrierV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public Guid BarrierId { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }

        public ReplayBarrierV2(Guid joinId, uint joinGeneration, Guid barrierId, ulong revision, Hash256 root)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || barrierId == Guid.Empty || root == null)
                throw new ArgumentException("Replay barrier identity is incomplete.");
            JoinId = joinId; JoinGeneration = joinGeneration; BarrierId = barrierId; Revision = revision; Root = root;
        }
    }

    public sealed class BarrierAckV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public Guid BarrierId { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }

        public BarrierAckV2(Guid joinId, uint joinGeneration, Guid barrierId, ulong revision, Hash256 root)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || barrierId == Guid.Empty || root == null)
                throw new ArgumentException("Barrier acknowledgement identity is incomplete.");
            JoinId = joinId; JoinGeneration = joinGeneration; BarrierId = barrierId; Revision = revision; Root = root;
        }
    }

    public sealed class ActivationGrantV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public Guid GrantId { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }
        public ulong PermissionVersion { get; private set; }

        public ActivationGrantV2(Guid joinId, uint joinGeneration, Guid grantId, ulong revision,
            Hash256 root, ulong permissionVersion)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || grantId == Guid.Empty || root == null || permissionVersion == 0)
                throw new ArgumentException("Activation grant identity is incomplete.");
            JoinId = joinId; JoinGeneration = joinGeneration; GrantId = grantId;
            Revision = revision; Root = root; PermissionVersion = permissionVersion;
        }
    }

    public sealed class ActivatedV2
    {
        public Guid JoinId { get; private set; }
        public uint JoinGeneration { get; private set; }
        public Guid GrantId { get; private set; }
        public ulong Revision { get; private set; }

        public ActivatedV2(Guid joinId, uint joinGeneration, Guid grantId, ulong revision)
        {
            if (joinId == Guid.Empty || joinGeneration == 0 || grantId == Guid.Empty)
                throw new ArgumentException("Activated identity is incomplete.");
            JoinId = joinId; JoinGeneration = joinGeneration; GrantId = grantId; Revision = revision;
        }
    }

    public static class JoinMessagesV2
    {
        public static byte[] EncodeSnapshotOffer(SnapshotOfferV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.JoinId.ToByteArray()); writer.Write(value.JoinGeneration);
                writer.Write(value.BaselineRevision); writer.Write(value.BaselineRoot.ToArray());
                writer.Write((byte)(value.RequiresTransfer ? 1 : 0));
                writer.Write(value.SnapshotId.ToByteArray()); writer.Write(value.TransferId.ToByteArray());
                writer.Write(value.ContentBytes);
                writer.Write(value.ContentHash == null ? new byte[Hash256.Size] : value.ContentHash.ToArray());
                writer.Flush(); return stream.ToArray();
            }
        }

        public static SnapshotOfferV2 DecodeSnapshotOffer(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 133) throw new InvalidDataException("Snapshot offer length is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                Guid join = ReadGuid(reader); uint generation = reader.ReadUInt32(); ulong revision = reader.ReadUInt64();
                Hash256 root = ReadHash(reader); byte transfer = reader.ReadByte();
                if (transfer > 1) throw new InvalidDataException("Snapshot offer transfer flag is invalid.");
                Guid snapshot = ReadOptionalGuid(reader); Guid transferId = ReadOptionalGuid(reader);
                ulong length = reader.ReadUInt64(); byte[] hashBytes = reader.ReadBytes(Hash256.Size);
                if (hashBytes.Length != Hash256.Size) throw new EndOfStreamException();
                Hash256 contentHash = transfer == 1 ? new Hash256(hashBytes) : null;
                return new SnapshotOfferV2(join, generation, revision, root, transfer == 1,
                    snapshot, transferId, length, contentHash);
            }
        }

        public static byte[] EncodeWorldInstalled(WorldInstalledV2 value)
        {
            Check.NotNull(value, "value");
            return EncodeJoinRevisionRoot(value.JoinId, value.JoinGeneration, value.Revision, value.Root);
        }

        public static WorldInstalledV2 DecodeWorldInstalled(byte[] bytes)
        {
            Guid join; uint generation; ulong revision; Hash256 root;
            DecodeJoinRevisionRoot(bytes, out join, out generation, out revision, out root);
            return new WorldInstalledV2(join, generation, revision, root);
        }

        public static byte[] EncodeReplayBarrier(ReplayBarrierV2 value)
        {
            Check.NotNull(value, "value");
            return EncodeJoinMarker(value.JoinId, value.JoinGeneration, value.BarrierId, value.Revision, value.Root, 0);
        }

        public static ReplayBarrierV2 DecodeReplayBarrier(byte[] bytes)
        {
            Guid join, marker; uint generation; ulong revision, extra; Hash256 root;
            DecodeJoinMarker(bytes, out join, out generation, out marker, out revision, out root, out extra);
            if (extra != 0) throw new InvalidDataException("Replay barrier reserved field is nonzero.");
            return new ReplayBarrierV2(join, generation, marker, revision, root);
        }

        public static byte[] EncodeBarrierAck(BarrierAckV2 value)
        {
            Check.NotNull(value, "value");
            return EncodeJoinMarker(value.JoinId, value.JoinGeneration, value.BarrierId, value.Revision, value.Root, 0);
        }

        public static BarrierAckV2 DecodeBarrierAck(byte[] bytes)
        {
            Guid join, marker; uint generation; ulong revision, extra; Hash256 root;
            DecodeJoinMarker(bytes, out join, out generation, out marker, out revision, out root, out extra);
            if (extra != 0) throw new InvalidDataException("Barrier acknowledgement reserved field is nonzero.");
            return new BarrierAckV2(join, generation, marker, revision, root);
        }

        public static byte[] EncodeActivationGrant(ActivationGrantV2 value)
        {
            Check.NotNull(value, "value");
            return EncodeJoinMarker(value.JoinId, value.JoinGeneration, value.GrantId, value.Revision,
                value.Root, value.PermissionVersion);
        }

        public static ActivationGrantV2 DecodeActivationGrant(byte[] bytes)
        {
            Guid join, marker; uint generation; ulong revision, permission; Hash256 root;
            DecodeJoinMarker(bytes, out join, out generation, out marker, out revision, out root, out permission);
            return new ActivationGrantV2(join, generation, marker, revision, root, permission);
        }

        public static byte[] EncodeActivated(ActivatedV2 value)
        {
            Check.NotNull(value, "value");
            byte[] result = new byte[44];
            Buffer.BlockCopy(value.JoinId.ToByteArray(), 0, result, 0, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(value.JoinGeneration), 0, result, 16, 4);
            Buffer.BlockCopy(value.GrantId.ToByteArray(), 0, result, 20, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(value.Revision), 0, result, 36, 8);
            return result;
        }

        public static ActivatedV2 DecodeActivated(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 44) throw new InvalidDataException("Activated payload length is invalid.");
            byte[] a = new byte[16]; byte[] b = new byte[16];
            Buffer.BlockCopy(bytes, 0, a, 0, 16); Buffer.BlockCopy(bytes, 20, b, 0, 16);
            return new ActivatedV2(new Guid(a), BitConverter.ToUInt32(bytes, 16), new Guid(b), BitConverter.ToUInt64(bytes, 36));
        }

        private static byte[] EncodeJoinRevisionRoot(Guid join, uint generation, ulong revision, Hash256 root)
        {
            if (join == Guid.Empty || generation == 0 || root == null) throw new ArgumentException("Join revision identity is incomplete.");
            byte[] result = new byte[60];
            Buffer.BlockCopy(join.ToByteArray(), 0, result, 0, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(generation), 0, result, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(revision), 0, result, 20, 8);
            Buffer.BlockCopy(root.ToArray(), 0, result, 28, 32);
            return result;
        }

        private static void DecodeJoinRevisionRoot(byte[] bytes, out Guid join, out uint generation,
            out ulong revision, out Hash256 root)
        {
            if (bytes == null || bytes.Length != 60) throw new InvalidDataException("Join revision payload length is invalid.");
            byte[] id = new byte[16]; byte[] hash = new byte[32];
            Buffer.BlockCopy(bytes, 0, id, 0, 16); Buffer.BlockCopy(bytes, 28, hash, 0, 32);
            join = new Guid(id); generation = BitConverter.ToUInt32(bytes, 16); revision = BitConverter.ToUInt64(bytes, 20); root = new Hash256(hash);
            if (join == Guid.Empty || generation == 0) throw new InvalidDataException("Join revision identity is incomplete.");
        }

        private static byte[] EncodeJoinMarker(Guid join, uint generation, Guid marker, ulong revision, Hash256 root, ulong extra)
        {
            if (join == Guid.Empty || generation == 0 || marker == Guid.Empty || root == null)
                throw new ArgumentException("Join marker identity is incomplete.");
            byte[] result = new byte[84];
            Buffer.BlockCopy(join.ToByteArray(), 0, result, 0, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(generation), 0, result, 16, 4);
            Buffer.BlockCopy(marker.ToByteArray(), 0, result, 20, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(revision), 0, result, 36, 8);
            Buffer.BlockCopy(root.ToArray(), 0, result, 44, 32);
            Buffer.BlockCopy(BitConverter.GetBytes(extra), 0, result, 76, 8);
            return result;
        }

        private static void DecodeJoinMarker(byte[] bytes, out Guid join, out uint generation, out Guid marker,
            out ulong revision, out Hash256 root, out ulong extra)
        {
            if (bytes == null || bytes.Length != 84) throw new InvalidDataException("Join marker payload length is invalid.");
            byte[] a = new byte[16]; byte[] b = new byte[16]; byte[] hash = new byte[32];
            Buffer.BlockCopy(bytes, 0, a, 0, 16); Buffer.BlockCopy(bytes, 20, b, 0, 16); Buffer.BlockCopy(bytes, 44, hash, 0, 32);
            join = new Guid(a); generation = BitConverter.ToUInt32(bytes, 16); marker = new Guid(b);
            revision = BitConverter.ToUInt64(bytes, 36); root = new Hash256(hash); extra = BitConverter.ToUInt64(bytes, 76);
            if (join == Guid.Empty || generation == 0 || marker == Guid.Empty) throw new InvalidDataException("Join marker identity is incomplete.");
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException();
            Guid value = new Guid(bytes); if (value == Guid.Empty) throw new InvalidDataException("Missing UUID."); return value;
        }

        private static Guid ReadOptionalGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new Guid(bytes);
        }

        private static Hash256 ReadHash(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Hash256.Size); if (bytes.Length != Hash256.Size) throw new EndOfStreamException(); return new Hash256(bytes);
        }
    }
}
