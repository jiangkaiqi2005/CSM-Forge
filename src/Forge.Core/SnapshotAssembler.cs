using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class SnapshotManifest
    {
        public SessionStamp Stamp { get; private set; }
        public Guid TransferId { get; private set; }
        public ulong Revision { get; private set; }
        public long TotalBytes { get; private set; }
        public int ChunkBytes { get; private set; }
        public int ChunkCount { get; private set; }
        public Hash256 ContentHash { get; private set; }
        public Hash256 StateHash { get; private set; }

        public SnapshotManifest(SessionStamp stamp, Guid transferId, ulong revision, long totalBytes,
            int chunkBytes, Hash256 contentHash, Hash256 stateHash)
        {
            Check.Stamp(stamp);
            Check.Condition(transferId == Guid.Empty, "transferId", "Missing transfer identity.");
            if (totalBytes < 1 || totalBytes > Limits.SnapshotBytes) throw new ArgumentOutOfRangeException("totalBytes");
            if (chunkBytes < 1 || chunkBytes > 32768) throw new ArgumentOutOfRangeException("chunkBytes");
            long count = (totalBytes + chunkBytes - 1) / chunkBytes;
            if (count > 8192) throw new ArgumentException("Too many snapshot chunks.");
            Check.NotNull(contentHash, "contentHash"); Check.NotNull(stateHash, "stateHash"); // WP-2: per-argument reporting
            Stamp = stamp; TransferId = transferId; Revision = revision;
            TotalBytes = totalBytes; ChunkBytes = chunkBytes; ChunkCount = (int)count;
            ContentHash = contentHash; StateHash = stateHash;
        }

        public int LengthOf(int index)
        {
            if (index < 0 || index >= ChunkCount) throw new ArgumentOutOfRangeException("index");
            return (int)Math.Min(ChunkBytes, TotalBytes - (long)index * ChunkBytes);
        }
    }

    public enum TransferPhase { Receiving, Verified, Failed, Cancelled, Disposed }
    public enum ChunkDecision { Accepted, Duplicate, Rejected, Conflict }

    /// <summary>
    /// Owns exclusive temporary storage after successful construction. Storage may be a file;
    /// no network-provided filename is accepted. Authentication is the coordinator's job.
    /// Single owner-thread API. The caller must not access the original stream concurrently.
    /// </summary>
    public sealed class SnapshotAssembler : IDisposable
    {
        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly SnapshotManifest manifest;
        private readonly Stream storage;
        private readonly bool[] received;
        public TransferPhase Phase { get; private set; }
        public int ReceivedChunks { get; private set; }
        public long ReceivedBytes { get; private set; }

        public SnapshotAssembler(SnapshotManifest manifest, Stream emptyTemporaryStorage)
        {
            Check.NotNull(manifest, "manifest");
            if (emptyTemporaryStorage == null || !emptyTemporaryStorage.CanRead ||
                !emptyTemporaryStorage.CanWrite || !emptyTemporaryStorage.CanSeek || emptyTemporaryStorage.Length != 0)
                throw new ArgumentException("An empty, readable, writable, seekable temporary stream is required.", "emptyTemporaryStorage");
            // All manifest bounds were checked before any storage/bitmap allocation.
            received = new bool[manifest.ChunkCount];
            emptyTemporaryStorage.SetLength(manifest.TotalBytes);
            this.manifest = manifest;
            storage = emptyTemporaryStorage;
            Phase = TransferPhase.Receiving;
        }

        public ChunkDecision Add(SessionStamp stamp, Guid transferId, int index, byte[] bytes, Hash256 chunkHash)
        {
            owner.AssertCurrent();
            if (Phase != TransferPhase.Receiving || !stamp.Equals(manifest.Stamp) || transferId != manifest.TransferId ||
                index < 0 || index >= manifest.ChunkCount || bytes == null || chunkHash == null)
                return ChunkDecision.Rejected;
            if (bytes.Length != manifest.LengthOf(index) || !Hash256.Compute(bytes).Equals(chunkHash))
                return ChunkDecision.Rejected;
            try
            {
                long offset = (long)index * manifest.ChunkBytes;
                storage.Position = offset;
                if (received[index])
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        if (storage.ReadByte() != bytes[i])
                        {
                            Phase = TransferPhase.Failed;
                            return ChunkDecision.Conflict;
                        }
                    }
                    return ChunkDecision.Duplicate;
                }
                storage.Write(bytes, 0, bytes.Length);
                received[index] = true;
                ReceivedChunks++;
                ReceivedBytes += bytes.Length;
                return ChunkDecision.Accepted;
            }
            catch (Exception) { Phase = TransferPhase.Failed; throw; }
        }

        public bool Verify()
        {
            owner.AssertCurrent();
            if (Phase != TransferPhase.Receiving || ReceivedChunks != manifest.ChunkCount || ReceivedBytes != manifest.TotalBytes)
                return false;
            try
            {
                storage.Flush();
                if (storage.Length != manifest.TotalBytes) { Phase = TransferPhase.Failed; return false; }
                storage.Position = 0;
                if (!Hash256.Compute(storage).Equals(manifest.ContentHash)) { Phase = TransferPhase.Failed; return false; }
                storage.Position = 0;
                Phase = TransferPhase.Verified;
                return true;
            }
            catch (Exception) { Phase = TransferPhase.Failed; throw; }
        }

        // Does not load a game or validate semantic state. The world adapter does that next.
        public void CopyVerifiedTo(Stream destination)
        {
            owner.AssertCurrent();
            if (Phase != TransferPhase.Verified) throw new InvalidOperationException("Only verified content may be published.");
            if (destination == null || !destination.CanWrite || ReferenceEquals(destination, storage))
                throw new ArgumentException("A separate writable destination is required.", "destination");
            storage.Position = 0;
            byte[] buffer = new byte[32768];
            long remaining = manifest.TotalBytes;
            while (remaining != 0)
            {
                int read = storage.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) { Phase = TransferPhase.Failed; throw new EndOfStreamException(); }
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        public void Cancel()
        {
            owner.AssertCurrent();
            if (Phase == TransferPhase.Disposed || Phase == TransferPhase.Cancelled) return;
            Phase = TransferPhase.Cancelled;
            storage.Dispose();
        }

        public void Dispose()
        {
            owner.AssertCurrent();
            if (Phase == TransferPhase.Disposed) return;
            storage.Dispose();
            Phase = TransferPhase.Disposed;
        }
    }
}
