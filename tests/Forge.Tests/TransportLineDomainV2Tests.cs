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
            TransportStopV2[] stops = new[]
            {
                new TransportStopV2(1f, 2f, 3f, false),
                new TransportStopV2(4f, 5f, 6f, true)
            };
            TransportLineStateV2 state = new TransportLineStateV2(id, "Bus Line", 1, 2, 3, 255, 100, 8, true, false, true, stops);
            TransportLineMutationV2 copy = TransportLineDomainCodecV2.DecodeMutation(
                TransportLineDomainCodecV2.EncodeMutation(new TransportLineMutationV2(new[] { state }, new EntityIdentityV2[0])));
            Assert.Equal(1, copy.Upserts.Length);
            Assert.Equal(id, copy.Upserts[0].Entity);
            Assert.Equal("Bus Line", copy.Upserts[0].PrefabKey);
            Assert.Equal((ushort)100, copy.Upserts[0].Budget);
            Assert.True(copy.Upserts[0].Day && !copy.Upserts[0].Night);
            Assert.True(copy.Upserts[0].Complete);
            Assert.Equal(2, copy.Upserts[0].Stops.Length);
            Assert.Equal(1f, copy.Upserts[0].Stops[0].X);
            Assert.Equal(6f, copy.Upserts[0].Stops[1].Z);
            Assert.True(copy.Upserts[0].Stops[1].FixedPlatform);
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
            TransportLineIntentV2 route = new TransportLineIntentV2(TransportLineIntentKindV2.AddStop,
                new EntityIdentityV2(9, 1), 2, new TransportStopV2(10f, 11f, 12f, true));
            TransportLineIntentV2 routeCopy = TransportLineDomainCodecV2.DecodeIntent(TransportLineDomainCodecV2.EncodeIntent(route));
            Assert.Equal(2, routeCopy.StopIndex);
            Assert.Equal(11f, routeCopy.Stop.Y);
            Assert.True(routeCopy.Stop.FixedPlatform);
            Assert.Throws<EndOfStreamException>(delegate { TransportLineDomainCodecV2.DecodeIntent(new byte[1]); });
        }
    }
}
