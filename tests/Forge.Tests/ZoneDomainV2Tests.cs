using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class ZoneDomainV2Tests
    {
        private static ZoneBlockKeyV2 Key(ulong segment, int x, int z, int angle)
        {
            return new ZoneBlockKeyV2(new EntityIdentityV2(segment, 1), x, z, angle);
        }

        [Case]
        public static void ZoneIntentAndMutationRoundTripUseStableSegmentGeometryKey()
        {
            ZoneStateV2 requested = new ZoneStateV2(Key(22, 160, -48, 1024), 0x1234UL, 0xABCDEFUL);
            ZoneIntentV2 decoded = ZoneDomainCodecV2.DecodeIntent(
                ZoneDomainCodecV2.EncodeIntent(new ZoneIntentV2(requested)));
            Assert.Equal(requested.Key, decoded.Requested.Key);
            Assert.Equal(requested.Zone1, decoded.Requested.Zone1);
            Assert.Equal(requested.Zone2, decoded.Requested.Zone2);

            ZoneMutationV2 mutation = new ZoneMutationV2(
                new ZoneStateV2[] { requested }, new ZoneBlockKeyV2[] { Key(23, 0, 0, 0) });
            ZoneMutationV2 copy = ZoneDomainCodecV2.DecodeMutation(ZoneDomainCodecV2.EncodeMutation(mutation));
            Assert.Equal(1, copy.Upserts.Length);
            Assert.Equal(1, copy.Deletes.Length);
            Assert.Equal(requested.Key, copy.Upserts[0].Key);
        }

        [Case]
        public static void ZoneRootIsCanonicalAndSparse()
        {
            ZoneStateV2 a = new ZoneStateV2(Key(10, 20, 30, 40), 1, 2);
            ZoneStateV2 b = new ZoneStateV2(Key(5, -20, 70, 11), 3, 4);
            ZoneStateIndexV2 first = new ZoneStateIndexV2(); first.Seed(a); first.Seed(b);
            ZoneStateIndexV2 second = new ZoneStateIndexV2(); second.Seed(b); second.Seed(a);
            Assert.Equal(first.Root, second.Root);

            first.Apply(new ZoneMutationV2(new ZoneStateV2[0], new ZoneBlockKeyV2[] { a.Key }));
            Assert.Equal(1, first.Count);
            ZoneStateV2 remaining;
            Assert.True(first.TryGet(b.Key, out remaining));
        }

        [Case]
        public static void EmptyZoneUpsertAndMalformedPayloadFailClosed()
        {
            ZoneBlockKeyV2 key = Key(7, 1, 2, 3);
            Assert.Throws<ArgumentException>(delegate
            {
                new ZoneMutationV2(new ZoneStateV2[] { new ZoneStateV2(key, 0, 0) }, new ZoneBlockKeyV2[0]);
            });

            byte[] encoded = ZoneDomainCodecV2.EncodeIntent(new ZoneIntentV2(new ZoneStateV2(key, 8, 9)));
            byte[] tainted = new byte[encoded.Length + 1]; Buffer.BlockCopy(encoded, 0, tainted, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { ZoneDomainCodecV2.DecodeIntent(tainted); });
        }
    }
}
