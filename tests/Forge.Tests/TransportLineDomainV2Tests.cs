using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class TransportLineDomainV2Tests
    {
        [Case]
        public static void TransportLineMutationRoundTrips()
        {
            EntityIdentityV2 id = new EntityIdentityV2(12, 2);
            TransportLineStateV2 state = new TransportLineStateV2(id, "Bus Line", 1, 2, 3, 255, 100, 8, true, false);
            TransportLineMutationV2 copy = TransportLineDomainCodecV2.DecodeMutation(
                TransportLineDomainCodecV2.EncodeMutation(new TransportLineMutationV2(new[] { state }, new EntityIdentityV2[0])));
            Assert.Equal(1, copy.Upserts.Length);
            Assert.Equal(id, copy.Upserts[0].Entity);
            Assert.Equal("Bus Line", copy.Upserts[0].PrefabKey);
            Assert.Equal((ushort)100, copy.Upserts[0].Budget);
            Assert.True(copy.Upserts[0].Day && !copy.Upserts[0].Night);
        }

        [Case]
        public static void TransportRootIsCanonicalAcrossInsertionOrder()
        {
            TransportLineStateV2 a = new TransportLineStateV2(new EntityIdentityV2(1, 1), "A", 1, 1, 1, 255, 100, 2, true, true);
            TransportLineStateV2 b = new TransportLineStateV2(new EntityIdentityV2(2, 1), "B", 2, 2, 2, 255, 110, 3, false, true);
            TransportLineStateIndexV2 first = new TransportLineStateIndexV2(); first.Seed(a); first.Seed(b);
            TransportLineStateIndexV2 second = new TransportLineStateIndexV2(); second.Seed(b); second.Seed(a);
            Assert.Equal(first.Root, second.Root);
        }

        [Case]
        public static void TransportIntentRejectsMalformedPayload()
        {
            TransportLineIntentV2 value = new TransportLineIntentV2(TransportLineIntentKindV2.SetProperties,
                new EntityIdentityV2(9, 1), 1, 2, 3, 255, 100, 4, true, true);
            byte[] bytes = TransportLineDomainCodecV2.EncodeIntent(value);
            TransportLineIntentV2 copy = TransportLineDomainCodecV2.DecodeIntent(bytes);
            Assert.Equal(value.Target, copy.Target);
            Assert.Throws<InvalidDataException>(delegate { TransportLineDomainCodecV2.DecodeIntent(new byte[1]); });
        }
    }
}
