using System;
using System.Collections.Generic;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class ExtensionStateDomainV2Tests
    {
        [Case]
        public static void SnapshotOrderingProducesStableRoot()
        {
            ExtensionStateEntryV2 a = new ExtensionStateEntryV2("campus", "park:2", new byte[] { 2 });
            ExtensionStateEntryV2 b = new ExtensionStateEntryV2("campus", "park:1", new byte[] { 1 });
            ExtensionStateSnapshotV2 first = new ExtensionStateSnapshotV2(new[] { a, b });
            ExtensionStateSnapshotV2 second = new ExtensionStateSnapshotV2(new[] { b, a });
            Assert.Equal(first.Root, second.Root);
            Assert.Equal("park:1", first.Entries[0].Key);
        }

        [Case]
        public static void SnapshotRoundTripsCanonically()
        {
            ExtensionStateSnapshotV2 value = new ExtensionStateSnapshotV2(new[]
            {
                new ExtensionStateEntryV2("campus", "park:1", new byte[] { 1, 2, 3 }),
                new ExtensionStateEntryV2("event", "event:9", new byte[0])
            });
            byte[] encoded = ExtensionStateCodecV2.Encode(value);
            ExtensionStateSnapshotV2 copy = ExtensionStateCodecV2.Decode(encoded);
            Assert.Equal(value.Root, copy.Root);
            Assert.Equal(encoded.Length, ExtensionStateCodecV2.Encode(copy).Length);
        }

        [Case]
        public static void EntriesAreMutationResistant()
        {
            byte[] source = new byte[] { 4, 5, 6 };
            ExtensionStateEntryV2 entry = new ExtensionStateEntryV2("adapter", "state", source);
            source[0] = 99;
            byte[] first = entry.Payload;
            first[1] = 88;
            Assert.Equal((byte)4, entry.Payload[0]);
            Assert.Equal((byte)5, entry.Payload[1]);
        }

        [Case]
        public static void DuplicateAndOversizeStateFailsClosed()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new ExtensionStateSnapshotV2(new[]
                {
                    new ExtensionStateEntryV2("a", "same", new byte[] { 1 }),
                    new ExtensionStateEntryV2("a", "same", new byte[] { 2 })
                });
            });
            Assert.Throws<ArgumentException>(delegate
            {
                ExtensionStateEntryV2[] entries = new ExtensionStateEntryV2[3];
                for (int i = 0; i < entries.Length; i++)
                    entries[i] = new ExtensionStateEntryV2("a", "k" + i, new byte[30000]);
                ExtensionStateCodecV2.Encode(new ExtensionStateSnapshotV2(entries));
            });
        }

        [Case]
        public static void DecoderRejectsTrailingBytes()
        {
            byte[] encoded = ExtensionStateCodecV2.Encode(new ExtensionStateSnapshotV2(new ExtensionStateEntryV2[0]));
            byte[] bad = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, bad, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { ExtensionStateCodecV2.Decode(bad); });
        }
    }
}
