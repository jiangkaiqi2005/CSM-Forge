using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class NamedEntityMapSaveV1Tests
    {
        [Case]
        public static void NamespacedMapsRoundTripWithoutOrderDependence()
        {
            NamedEntityMapSnapshotV1 parks = new NamedEntityMapSnapshotV1("builtin.districtpark", 7, new[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(2, 1), 18),
                new EntityMapEntryV2(new EntityIdentityV2(7, 1), 3)
            });
            NamedEntityMapSnapshotV1 eventsMap = new NamedEntityMapSnapshotV1("builtin.events", 4, new[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(4, 2), 22)
            });
            byte[] first = NamedEntityMapSaveCodecV1.Encode(new[] { parks, eventsMap });
            byte[] second = NamedEntityMapSaveCodecV1.Encode(new[] { eventsMap, parks });
            Assert.Equal(first.Length, second.Length);
            for (int i = 0; i < first.Length; i++) Assert.Equal(first[i], second[i]);
            NamedEntityMapSnapshotV1[] copy = NamedEntityMapSaveCodecV1.Decode(first);
            Assert.Equal(2, copy.Length);
            Assert.Equal("builtin.districtpark", copy[0].NamespaceId);
            Assert.Equal((ulong)7, copy[0].HighestIssuedId);
            Assert.Equal((uint)18, copy[0].Entries[0].NativeId);
        }

        [Case]
        public static void NamespaceAndNativeCollisionsFailClosed()
        {
            NamedEntityMapSnapshotV1 value = new NamedEntityMapSnapshotV1("adapter", 2, new[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(1, 1), 5),
                new EntityMapEntryV2(new EntityIdentityV2(2, 1), 5)
            });
            Assert.Throws<InvalidDataException>(delegate { NamedEntityMapSaveCodecV1.Encode(new[] { value }); });
            Assert.Throws<InvalidDataException>(delegate
            {
                NamedEntityMapSaveCodecV1.Encode(new[]
                {
                    new NamedEntityMapSnapshotV1("same", 0, new EntityMapEntryV2[0]),
                    new NamedEntityMapSnapshotV1("same", 0, new EntityMapEntryV2[0])
                });
            });
        }

        [Case]
        public static void WatermarkAndTrailingBytesAreValidated()
        {
            NamedEntityMapSnapshotV1 bad = new NamedEntityMapSnapshotV1("adapter", 0, new[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(1, 1), 9)
            });
            Assert.Throws<InvalidDataException>(delegate { NamedEntityMapSaveCodecV1.Encode(new[] { bad }); });
            byte[] good = NamedEntityMapSaveCodecV1.Encode(new NamedEntityMapSnapshotV1[0]);
            byte[] trailing = new byte[good.Length + 1];
            Buffer.BlockCopy(good, 0, trailing, 0, good.Length);
            Assert.Throws<InvalidDataException>(delegate { NamedEntityMapSaveCodecV1.Decode(trailing); });
        }
    }
}
