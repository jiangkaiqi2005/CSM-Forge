using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public sealed class SnapshotChunkV2
    {
        public const int MaxChunkBytes = 32 * 1024;
        private readonly byte[] data;
        public Guid SnapshotId { get; private set; }
        public Guid TransferId { get; private set; }
        public uint Index { get; private set; }
        public ulong Offset { get; private set; }
        public Hash256 ChunkHash { get; private set; }
        public byte[] Data { get { return (byte[])data.Clone(); } }

        public SnapshotChunkV2(Guid snapshotId, Guid transferId, uint index, ulong offset, byte[] bytes, Hash256 chunkHash)
        {
            if (snapshotId == Guid.Empty || transferId == Guid.Empty) throw new ArgumentException("Snapshot chunk identity is incomplete.");
            Check.Condition(bytes == null || bytes.Length == 0 || bytes.Length > MaxChunkBytes, "bytes", "Snapshot chunk size is invalid.");
            Hash256 actual = Hash256.Compute(bytes);
            Check.Condition(chunkHash == null || !actual.Equals(chunkHash), "chunkHash", "Snapshot chunk hash does not match its bytes.");
            SnapshotId = snapshotId; TransferId = transferId; Index = index; Offset = offset;
            data = (byte[])bytes.Clone(); ChunkHash = chunkHash;
        }
    }

    public sealed class SnapshotProgressV2
    {
        public Guid SnapshotId { get; private set; }
        public Guid TransferId { get; private set; }
        public uint NextIndex { get; private set; }
        public ulong NextOffset { get; private set; }

        public SnapshotProgressV2(Guid snapshotId, Guid transferId, uint nextIndex, ulong nextOffset)
        {
            if (snapshotId == Guid.Empty || transferId == Guid.Empty) throw new ArgumentException("Snapshot progress identity is incomplete.");
            SnapshotId = snapshotId; TransferId = transferId; NextIndex = nextIndex; NextOffset = nextOffset;
        }
    }

    public static class SnapshotTransferMessagesV2
    {
        public static byte[] EncodeChunk(SnapshotChunkV2 value)
        {
            Check.NotNull(value, "value");
            byte[] data = value.Data;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.SnapshotId.ToByteArray()); writer.Write(value.TransferId.ToByteArray());
                writer.Write(value.Index); writer.Write(value.Offset); writer.Write((ushort)data.Length);
                writer.Write(value.ChunkHash.ToArray()); writer.Write(data); writer.Flush();
                return stream.ToArray();
            }
        }

        public static SnapshotChunkV2 DecodeChunk(byte[] bytes)
        {
            int header = 16 + 16 + 4 + 8 + 2 + Hash256.Size;
            if (bytes == null || bytes.Length <= header || bytes.Length > header + SnapshotChunkV2.MaxChunkBytes)
                throw new InvalidDataException("Snapshot chunk payload length is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                Guid snapshot = ReadGuid(reader); Guid transfer = ReadGuid(reader); uint index = reader.ReadUInt32();
                ulong offset = reader.ReadUInt64(); ushort length = reader.ReadUInt16(); Hash256 hash = ReadHash(reader);
                if (length == 0 || length > SnapshotChunkV2.MaxChunkBytes || reader.BaseStream.Length - reader.BaseStream.Position != length)
                    throw new InvalidDataException("Snapshot chunk declared length is invalid.");
                return new SnapshotChunkV2(snapshot, transfer, index, offset, reader.ReadBytes(length), hash);
            }
        }

        public static byte[] EncodeProgress(SnapshotProgressV2 value)
        {
            Check.NotNull(value, "value");
            byte[] result = new byte[44];
            Buffer.BlockCopy(value.SnapshotId.ToByteArray(), 0, result, 0, 16);
            Buffer.BlockCopy(value.TransferId.ToByteArray(), 0, result, 16, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(value.NextIndex), 0, result, 32, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(value.NextOffset), 0, result, 36, 8);
            return result;
        }

        public static SnapshotProgressV2 DecodeProgress(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 44) throw new InvalidDataException("Snapshot progress payload length is invalid.");
            byte[] a = new byte[16]; byte[] b = new byte[16];
            Buffer.BlockCopy(bytes, 0, a, 0, 16); Buffer.BlockCopy(bytes, 16, b, 0, 16);
            return new SnapshotProgressV2(new Guid(a), new Guid(b), BitConverter.ToUInt32(bytes, 32), BitConverter.ToUInt64(bytes, 36));
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException();
            Guid value = new Guid(bytes); if (value == Guid.Empty) throw new InvalidDataException("Missing transfer UUID."); return value;
        }
        private static Hash256 ReadHash(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Hash256.Size); if (bytes.Length != Hash256.Size) throw new EndOfStreamException(); return new Hash256(bytes);
        }
    }
}
