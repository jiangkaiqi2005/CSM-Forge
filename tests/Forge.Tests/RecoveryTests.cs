using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class RecoveryTests
    {
        private static readonly SessionStamp Stamp = new SessionStamp(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 1);
        private static SnapshotManifest Manifest(byte[] bytes, int chunkBytes, Hash256 overrideHash)
        {
            return new SnapshotManifest(Stamp, Guid.NewGuid(), 0, bytes.Length, chunkBytes,
                overrideHash ?? Hash256.Compute(bytes), Hash256.Compute(bytes));
        }
        private static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] bytes = new byte[count]; Array.Copy(source, offset, bytes, 0, count); return bytes;
        }

        [Case] public static void SnapshotChunksCanArriveOutOfOrder()
        {
            byte[] data = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
            SnapshotManifest manifest = Manifest(data, 3, null);
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                for (int i = manifest.ChunkCount - 1; i >= 0; i--)
                {
                    byte[] bytes = Slice(data, i * 3, manifest.LengthOf(i));
                    Assert.Equal(ChunkDecision.Accepted, assembler.Add(Stamp, manifest.TransferId, i, bytes, Hash256.Compute(bytes)));
                }
                Assert.True(assembler.Verify());
                using (MemoryStream output = new MemoryStream())
                {
                    assembler.CopyVerifiedTo(output);
                    Assert.True(Hash256.Compute(data).Equals(Hash256.Compute(output.ToArray())));
                }
            }
        }

        [Case] public static void MissingFirstChunkCannotBeMistakenForCompleteFile()
        {
            byte[] data = new byte[] { 1, 2, 3, 4 };
            SnapshotManifest manifest = Manifest(data, 2, null);
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                byte[] bytes = new byte[] { 3, 4 };
                assembler.Add(Stamp, manifest.TransferId, 1, bytes, Hash256.Compute(bytes));
                Assert.True(!assembler.Verify());
                Assert.Throws<InvalidOperationException>(delegate { assembler.CopyVerifiedTo(new MemoryStream()); });
            }
        }

        [Case] public static void DuplicateChunkDoesNotAdvanceProgress()
        {
            byte[] data = new byte[] { 1, 2 };
            SnapshotManifest manifest = Manifest(data, 2, null);
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data));
                Assert.Equal(ChunkDecision.Duplicate, assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data)));
                Assert.Equal(1, assembler.ReceivedChunks);
                Assert.Equal((long)2, assembler.ReceivedBytes);
                Assert.True(assembler.Verify());
            }
        }

        [Case] public static void ConflictingDuplicateChunkFencesTransfer()
        {
            byte[] data = new byte[] { 1, 2 };
            byte[] other = new byte[] { 2, 1 };
            SnapshotManifest manifest = Manifest(data, 2, null);
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data));
                Assert.Equal(ChunkDecision.Conflict, assembler.Add(Stamp, manifest.TransferId, 0, other, Hash256.Compute(other)));
                Assert.Equal(TransferPhase.Failed, assembler.Phase);
                Assert.True(!assembler.Verify());
            }
        }

        [Case] public static void WrongLengthDigestTransferAndEpochLeaveProgressUntouched()
        {
            byte[] data = new byte[] { 1, 2 };
            SnapshotManifest manifest = Manifest(data, 2, null);
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                Assert.Equal(ChunkDecision.Rejected, assembler.Add(Stamp, manifest.TransferId, 0, new byte[1], Hash256.Compute(new byte[1])));
                Assert.Equal(ChunkDecision.Rejected, assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(new byte[1])));
                Assert.Equal(ChunkDecision.Rejected, assembler.Add(Stamp, Guid.NewGuid(), 0, data, Hash256.Compute(data)));
                Assert.Equal(ChunkDecision.Rejected, assembler.Add(new SessionStamp(Stamp.WorldId, 2), manifest.TransferId, 0, data, Hash256.Compute(data)));
                Assert.Equal(0, assembler.ReceivedChunks);
                Assert.Equal((long)0, assembler.ReceivedBytes);
            }
        }

        [Case] public static void WholeFileDigestIsCheckedEvenWhenEveryChunkIsValid()
        {
            byte[] data = new byte[] { 1, 2 };
            SnapshotManifest manifest = Manifest(data, 2, Hash256.Compute(new byte[1]));
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, new MemoryStream()))
            {
                assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data));
                Assert.True(!assembler.Verify());
                Assert.Equal(TransferPhase.Failed, assembler.Phase);
            }
        }

        [Case] public static void OversizeSnapshotAndChunkBombAreRejectedBeforeStorageAllocation()
        {
            Hash256 hash = Hash256.Compute(new byte[1]);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new SnapshotManifest(Stamp, Guid.NewGuid(), 0, long.MaxValue, 1024, hash, hash); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new SnapshotManifest(Stamp, Guid.NewGuid(), 0, 0, 1024, hash, hash); });
            Assert.Throws<ArgumentException>(delegate { new SnapshotManifest(Stamp, Guid.NewGuid(), 0, Limits.SnapshotBytes, 1, hash, hash); });
        }

        [Case] public static void CancelDisposesStorageAndInvalidatesLateChunks()
        {
            byte[] data = new byte[] { 1, 2 };
            SnapshotManifest manifest = Manifest(data, 2, null);
            MemoryStream storage = new MemoryStream();
            using (SnapshotAssembler assembler = new SnapshotAssembler(manifest, storage))
            {
                assembler.Cancel(); assembler.Cancel();
                Assert.True(!storage.CanWrite);
                Assert.Equal(ChunkDecision.Rejected, assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data)));
                Assert.True(!assembler.Verify());
            }
        }

        [Case] public static void SnapshotAssemblerWorksWithDiskStorage()
        {
            string path = Path.GetTempFileName();
            byte[] data = new byte[] { 5, 4, 3, 2, 1 };
            SnapshotManifest manifest = Manifest(data, 5, null);
            try
            {
                using (SnapshotAssembler assembler = new SnapshotAssembler(manifest,
                    new FileStream(path, FileMode.Truncate, FileAccess.ReadWrite, FileShare.None)))
                {
                    assembler.Add(Stamp, manifest.TransferId, 0, data, Hash256.Compute(data));
                    Assert.True(assembler.Verify());
                }
                Assert.True(Hash256.Compute(data).Equals(Hash256.Compute(File.ReadAllBytes(path))));
            }
            finally { File.Delete(path); }
        }

        [Case] public static void LinkHealthUsesMonotonicThresholds()
        {
            PeerLiveness link = new PeerLiveness(0, 1000, 5000);
            Assert.Equal(LinkHealth.Healthy, link.Poll(999));
            Assert.Equal(LinkHealth.Degraded, link.Poll(1000));
            Assert.True(link.AuthenticatedHeartbeat(2000));
            Assert.Equal(LinkHealth.Healthy, link.Poll(2999));
            Assert.Equal(LinkHealth.Degraded, link.Poll(3000));
            Assert.Equal(LinkHealth.Expired, link.Poll(7000));
        }

        [Case] public static void ExpiredConnectionCannotBeRevivedByLateHeartbeat()
        {
            PeerLiveness link = new PeerLiveness(0, 1000, 5000);
            Assert.True(!link.AuthenticatedHeartbeat(5000));
            Assert.True(!link.AuthenticatedHeartbeat(5001));
            Assert.Equal(LinkHealth.Expired, link.Health);
        }

        [Case] public static void BackwardsClockIsRejectedInsteadOfSuppressingTimeouts()
        {
            PeerLiveness link = new PeerLiveness(10, 1000, 5000);
            link.Poll(20);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { link.Poll(19); });
        }
    }
}
