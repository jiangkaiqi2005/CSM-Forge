using System;
using System.IO;
using System.Text;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class ModelTests
    {
        private static SessionStamp Stamp()
        {
            return new SessionStamp(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 7);
        }

        [Case] public static void Sha256KnownVector()
        {
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hash256.Compute(Encoding.ASCII.GetBytes("abc")).ToString());
        }

        [Case] public static void DigestsAndMessagesOwnTheirBytes()
        {
            byte[] bytes = new byte[32];
            Hash256 hash = new Hash256(bytes);
            bytes[0] = 5;
            byte[] copy = hash.ToArray();
            copy[0] = 6;
            Assert.Equal((byte)0, hash.ToArray()[0]);
            bytes = ParameterWorld.Set(1, 7);
            Intent intent = new Intent(Stamp(), 1, 0, bytes);
            bytes[0] = 9;
            copy = intent.Payload;
            copy[0] = 8;
            Assert.Equal((byte)1, intent.Payload[0]);
        }

        [Case] public static void DefaultSessionAndOversizeIntentAreRejected()
        {
            Assert.Throws<ArgumentException>(delegate { new Intent(default(SessionStamp), 1, 0, new byte[1]); });
            Assert.Throws<ArgumentException>(delegate { new Intent(Stamp(), 1, 0, new byte[Limits.CommandBytes + 1]); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new Intent(Stamp(), 0, 0, new byte[1]); });
        }

        [Case] public static void CanonicalStateIgnoresInsertionOrder()
        {
            ParameterWorld a = new ParameterWorld();
            ParameterWorld b = new ParameterWorld();
            a.Execute(ParameterWorld.Set(1, 20)); a.Execute(ParameterWorld.Set(2, 30));
            b.Execute(ParameterWorld.Set(2, 30)); b.Execute(ParameterWorld.Set(1, 20));
            Assert.True(a.StateHash.Equals(b.StateHash));
        }

        [Case] public static void InvalidDomainIntentDoesNotMutate()
        {
            ParameterWorld world = new ParameterWorld();
            Hash256 before = world.StateHash;
            Assert.True(!world.Execute(ParameterWorld.Set(0, 20)).Applied);
            Assert.True(!world.Execute(ParameterWorld.Set(1, 1000001)).Applied);
            Assert.True(!world.Execute(new byte[5]).Applied);
            Assert.True(before.Equals(world.StateHash));
        }

        [Case] public static void ReplicaChangeStagesBeforePublication()
        {
            ParameterWorld world = new ParameterWorld();
            Hash256 before = world.StateHash;
            Assert.Throws<InvalidDataException>(delegate { world.Apply(ParameterWorld.Set(1, 2), before); });
            Assert.True(before.Equals(world.StateHash));
        }

        [Case] public static void SnapshotRoundTrip()
        {
            ParameterWorld source = new ParameterWorld();
            source.Execute(ParameterWorld.Set(3, -42));
            ParameterWorld target = new ParameterWorld();
            target.Install(source.Capture());
            Assert.True(source.StateHash.Equals(target.StateHash));
            Assert.Equal(-42, target.Get(3));
        }

        [Case] public static void SnapshotCorruptionDoesNotMutate()
        {
            ParameterWorld world = new ParameterWorld();
            WorldImage image = world.Capture();
            byte[] corrupt = image.Bytes;
            corrupt[0] ^= 1;
            Assert.Throws<InvalidDataException>(delegate { world.Install(new WorldImage(corrupt, image.ContentHash, image.StateHash)); });
            Assert.True(world.StateHash.Equals(image.StateHash));
        }

        [Case] public static void SnapshotStateHashIsNotJustFileHash()
        {
            ParameterWorld world = new ParameterWorld();
            WorldImage image = world.Capture();
            Assert.Throws<InvalidDataException>(delegate
            {
                world.Install(new WorldImage(image.Bytes, image.ContentHash, Hash256.Compute(new byte[] { 1 })));
            });
            Assert.True(world.StateHash.Equals(image.StateHash));
        }

        [Case] public static void IntentWireRoundTrip()
        {
            Intent original = new Intent(Stamp(), 8, 3, ParameterWorld.Set(4, 75));
            Intent decoded = FrameCodec.DecodeIntent(FrameCodec.EncodeIntent(original));
            Assert.True(original.Stamp.Equals(decoded.Stamp));
            Assert.True(original.Fingerprint.Equals(decoded.Fingerprint));
        }

        [Case] public static void CommitWireRoundTrip()
        {
            ParameterWorld world = new ParameterWorld();
            Hash256 before = world.StateHash;
            WorldExecution change = world.Execute(ParameterWorld.Set(1, 123));
            Commit commit = new Commit(Stamp(), 1, Guid.NewGuid(), 2, before, change.AfterHash, change.Delta);
            Commit decoded = FrameCodec.DecodeCommit(FrameCodec.EncodeCommit(commit));
            Assert.True(commit.Fingerprint.Equals(decoded.Fingerprint));
        }

        [Case] public static void WireUuidHasRfc4122Order()
        {
            byte[] bytes = FrameCodec.Encode(new Frame(MessageKind.Heartbeat, Stamp(), 0, new byte[0]));
            Assert.Equal((byte)0x00, bytes[12]);
            Assert.Equal((byte)0x11, bytes[13]);
            Assert.Equal((byte)0x22, bytes[14]);
            Assert.Equal((byte)0x33, bytes[15]);
            Assert.Equal((byte)0x44, bytes[16]);
            Assert.Equal((byte)0x55, bytes[17]);
            Assert.Equal((byte)0x66, bytes[18]);
            Assert.Equal((byte)0x77, bytes[19]);
        }

        [Case] public static void EverySingleByteMutationIsDetected()
        {
            byte[] original = FrameCodec.EncodeIntent(new Intent(Stamp(), 1, 0, ParameterWorld.Set(1, 2)));
            for (int i = 0; i < original.Length; i++)
            {
                byte[] corrupt = (byte[])original.Clone();
                corrupt[i] ^= 1;
                Assert.Throws<InvalidDataException>(delegate { FrameCodec.Decode(corrupt); });
            }
        }

        [Case] public static void EveryTruncationIsRejected()
        {
            byte[] original = FrameCodec.EncodeIntent(new Intent(Stamp(), 1, 0, ParameterWorld.Set(1, 2)));
            for (int length = 0; length < original.Length; length++)
            {
                byte[] truncated = new byte[length];
                Array.Copy(original, truncated, length);
                Assert.Throws<InvalidDataException>(delegate { FrameCodec.Decode(truncated); });
            }
        }

        [Case] public static void TrailingBytesAndOversizeFramesAreRejected()
        {
            byte[] original = FrameCodec.Encode(new Frame(MessageKind.Heartbeat, Stamp(), 0, new byte[0]));
            byte[] extra = new byte[original.Length + 1];
            Array.Copy(original, extra, original.Length);
            Assert.Throws<InvalidDataException>(delegate { FrameCodec.Decode(extra); });
            Assert.Throws<InvalidDataException>(delegate { FrameCodec.Decode(new byte[Limits.FramePayloadBytes + 81]); });
        }

        [Case] public static void RandomMalformedFramesHaveBoundedDecoderFailures()
        {
            Random random = new Random(913);
            for (int i = 0; i < 2000; i++)
            {
                byte[] bytes = new byte[random.Next(0, 1024)];
                random.NextBytes(bytes);
                Assert.Throws<InvalidDataException>(delegate { FrameCodec.Decode(bytes); });
            }
        }
    }
}
