using System;
using System.IO;
using CsmForge.Checkpoints;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class SnapshotTransferTests
    {
        [Case]
        public static void SharedSnapshotUsesIndependentTransferCursorsAndVerifiedReceiver()
        {
            string source = Temp("source");
            string target = Temp("target");
            try
            {
                byte[] content = new byte[70000];
                for (int i = 0; i < content.Length; i++) content[i] = (byte)(i * 31 + 7);
                File.WriteAllBytes(source, content);
                SnapshotFileDescriptor descriptor = SnapshotFileDescriptor.FromFile(Guid.NewGuid(), 12,
                    Hash256.Compute(new byte[] { 1 }), source);
                Guid transferA = Guid.NewGuid();
                Guid transferB = Guid.NewGuid();
                using (SnapshotReadCursor first = new SnapshotReadCursor(descriptor, transferA))
                using (SnapshotReadCursor second = new SnapshotReadCursor(descriptor, transferB))
                {
                    SnapshotChunkV2 a0 = first.ReadNext();
                    SnapshotChunkV2 b0 = second.ReadNext();
                    Assert.Equal((ulong)0, a0.Offset);
                    Assert.Equal((ulong)0, b0.Offset);
                    Assert.Equal((uint)0, a0.Index);
                    Assert.Equal((uint)0, b0.Index);

                    SnapshotOfferV2 offer = new SnapshotOfferV2(Guid.NewGuid(), 1, descriptor.Revision, descriptor.Root,
                        true, descriptor.SnapshotId, transferA, (ulong)descriptor.Length, descriptor.ContentHash);
                    using (SnapshotReceiveFile receiver = new SnapshotReceiveFile(offer, target))
                    {
                        receiver.Accept(a0);
                        SnapshotChunkV2 next;
                        while ((next = first.ReadNext()) != null) receiver.Accept(next);
                        Assert.True(receiver.Complete);
                        Assert.Equal((ulong)content.Length, receiver.NextOffset);
                        Assert.Equal(descriptor.ContentHash, Hash256.Compute(receiver.ReadAllVerifiedBytes()));
                    }
                }
            }
            finally { Delete(source); Delete(target); }
        }

        [Case]
        public static void ReceiverRejectsGapOrWrongTransferWithoutAdvancing()
        {
            string source = Temp("gap-source");
            string target = Temp("gap-target");
            try
            {
                File.WriteAllBytes(source, new byte[40000]);
                SnapshotFileDescriptor descriptor = SnapshotFileDescriptor.FromFile(Guid.NewGuid(), 1,
                    Hash256.Compute(new byte[] { 2 }), source);
                Guid transfer = Guid.NewGuid();
                SnapshotOfferV2 offer = new SnapshotOfferV2(Guid.NewGuid(), 1, 1, descriptor.Root, true,
                    descriptor.SnapshotId, transfer, (ulong)descriptor.Length, descriptor.ContentHash);
                using (SnapshotReadCursor cursor = new SnapshotReadCursor(descriptor, transfer))
                using (SnapshotReceiveFile receiver = new SnapshotReceiveFile(offer, target))
                {
                    SnapshotChunkV2 first = cursor.ReadNext();
                    byte[] bytes = first.Data;
                    SnapshotChunkV2 wrong = new SnapshotChunkV2(first.SnapshotId, first.TransferId, 1,
                        first.Offset + 1, bytes, Hash256.Compute(bytes));
                    Assert.Throws<InvalidDataException>(delegate { receiver.Accept(wrong); });
                    Assert.Equal((ulong)0, receiver.NextOffset);
                    Assert.Equal((uint)0, receiver.NextIndex);
                    receiver.Accept(first);
                    Assert.True(receiver.NextOffset > 0);
                }
            }
            finally { Delete(source); Delete(target); }
        }

        [Case]
        public static void WholeFileHashMismatchIsNotPublishedAsComplete()
        {
            string source = Temp("hash-source");
            string target = Temp("hash-target");
            try
            {
                File.WriteAllBytes(source, new byte[100]);
                SnapshotFileDescriptor descriptor = SnapshotFileDescriptor.FromFile(Guid.NewGuid(), 1,
                    Hash256.Compute(new byte[] { 3 }), source);
                Guid transfer = Guid.NewGuid();
                SnapshotOfferV2 offer = new SnapshotOfferV2(Guid.NewGuid(), 1, 1, descriptor.Root, true,
                    descriptor.SnapshotId, transfer, (ulong)descriptor.Length, Hash256.Compute(new byte[] { 99 }));
                using (SnapshotReadCursor cursor = new SnapshotReadCursor(descriptor, transfer))
                using (SnapshotReceiveFile receiver = new SnapshotReceiveFile(offer, target))
                {
                    Assert.Throws<InvalidDataException>(delegate { receiver.Accept(cursor.ReadNext()); });
                    Assert.True(!receiver.Complete);
                }
            }
            finally { Delete(source); Delete(target); }
        }

        private static string Temp(string name)
        {
            return Path.Combine(Path.GetTempPath(), "csm-forge-" + name + "-" + Guid.NewGuid().ToString("N") + ".bin");
        }

        private static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
