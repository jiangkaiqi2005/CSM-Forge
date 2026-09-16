using System;
using System.IO;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Checkpoints
{
    public sealed class SnapshotFileDescriptor
    {
        public const long MaximumBytes = 512L * 1024L * 1024L;
        public Guid SnapshotId { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }
        public string Path { get; private set; }
        public long Length { get; private set; }
        public Hash256 ContentHash { get; private set; }

        public SnapshotFileDescriptor(Guid snapshotId, ulong revision, Hash256 root, string path, long length, Hash256 contentHash)
        {
            if (snapshotId == Guid.Empty || root == null || string.IsNullOrEmpty(path) || contentHash == null)
                throw new ArgumentException("Snapshot descriptor is incomplete.");
            if (length <= 0 || length > MaximumBytes) throw new ArgumentOutOfRangeException("length");
            SnapshotId = snapshotId; Revision = revision; Root = root; Path = path; Length = length; ContentHash = contentHash;
        }

        public static SnapshotFileDescriptor FromFile(Guid snapshotId, ulong revision, Hash256 root, string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Snapshot file does not exist.", path);
            FileInfo info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumBytes) throw new IOException("Snapshot file size is outside the supported range.");
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return new SnapshotFileDescriptor(snapshotId, revision, root, path, info.Length, Hash256.Compute(stream));
        }
    }

    public sealed class SnapshotReadCursor : IDisposable
    {
        private readonly SnapshotFileDescriptor descriptor;
        private readonly Guid transferId;
        private FileStream stream;
        private uint index;

        public SnapshotReadCursor(SnapshotFileDescriptor descriptor, Guid transferId)
        {
            if (descriptor == null) throw new ArgumentNullException("descriptor");
            if (transferId == Guid.Empty) throw new ArgumentException("Missing transfer id.", "transferId");
            this.descriptor = descriptor; this.transferId = transferId;
            stream = new FileStream(descriptor.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != descriptor.Length) throw new IOException("Snapshot changed after publication.");
        }

        public long Position { get { return stream == null ? descriptor.Length : stream.Position; } }
        public bool Complete { get { return Position >= descriptor.Length; } }

        public SnapshotChunkV2 ReadNext()
        {
            if (stream == null) throw new ObjectDisposedException("SnapshotReadCursor");
            if (stream.Position >= descriptor.Length) return null;
            long offset = stream.Position;
            int length = (int)Math.Min(SnapshotChunkV2.MaxChunkBytes, descriptor.Length - offset);
            byte[] bytes = new byte[length];
            int read = 0;
            while (read < length)
            {
                int current = stream.Read(bytes, read, length - read);
                if (current <= 0) throw new EndOfStreamException("Snapshot ended before its published length.");
                read += current;
            }
            return new SnapshotChunkV2(descriptor.SnapshotId, transferId, index++, (ulong)offset, bytes, Hash256.Compute(bytes));
        }

        public void Dispose()
        {
            FileStream current = stream; stream = null;
            if (current != null) current.Dispose();
        }
    }

    public sealed class SnapshotReceiveFile : IDisposable
    {
        private readonly SnapshotOfferV2 offer;
        private readonly string path;
        private FileStream stream;
        private uint nextIndex;
        private ulong nextOffset;
        private bool complete;
        private bool retained;

        public SnapshotReceiveFile(SnapshotOfferV2 offer, string path)
        {
            if (offer == null || !offer.RequiresTransfer) throw new ArgumentException("A transfer-backed snapshot offer is required.", "offer");
            if (offer.ContentBytes == 0 || offer.ContentBytes > (ulong)SnapshotFileDescriptor.MaximumBytes)
                throw new ArgumentOutOfRangeException("offer");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Temporary path is required.", "path");
            this.offer = offer; this.path = path;
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        }

        public bool Complete { get { return complete; } }
        public ulong NextOffset { get { return nextOffset; } }
        public uint NextIndex { get { return nextIndex; } }
        public string Path { get { return path; } }

        public SnapshotProgressV2 Accept(SnapshotChunkV2 chunk)
        {
            if (stream == null) throw new ObjectDisposedException("SnapshotReceiveFile");
            if (complete) throw new InvalidOperationException("Snapshot transfer is already complete.");
            if (chunk == null || chunk.SnapshotId != offer.SnapshotId || chunk.TransferId != offer.TransferId ||
                chunk.Index != nextIndex || chunk.Offset != nextOffset)
                throw new InvalidDataException("Snapshot chunk identity, index or offset is not continuous.");
            byte[] bytes = chunk.Data;
            if ((ulong)bytes.Length > offer.ContentBytes - nextOffset)
                throw new InvalidDataException("Snapshot chunk exceeds the published content length.");
            stream.Write(bytes, 0, bytes.Length);
            nextOffset += (ulong)bytes.Length;
            nextIndex++;
            if (nextOffset == offer.ContentBytes)
            {
                stream.Flush();
                stream.Position = 0;
                Hash256 actual = Hash256.Compute(stream);
                if (!actual.Equals(offer.ContentHash)) throw new InvalidDataException("Snapshot whole-file hash mismatch.");
                stream.Dispose();
                stream = null;
                complete = true;
            }
            return new SnapshotProgressV2(offer.SnapshotId, offer.TransferId, nextIndex, nextOffset);
        }

        public byte[] ReadAllVerifiedBytes()
        {
            if (!complete) throw new InvalidOperationException("Snapshot has not been verified.");
            FileInfo info = new FileInfo(path);
            if ((ulong)info.Length != offer.ContentBytes || info.Length > int.MaxValue)
                throw new IOException("Verified snapshot file size changed or cannot be loaded by CS1.");
            return File.ReadAllBytes(path);
        }

        public void RetainFile() { if (!complete) throw new InvalidOperationException("Cannot retain an incomplete snapshot."); retained = true; }

        public void Dispose()
        {
            FileStream current = stream; stream = null;
            if (current != null) current.Dispose();
            if (!retained)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
