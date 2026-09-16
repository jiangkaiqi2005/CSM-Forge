using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class TaxDomainV2Tests
    {
        [Case]
        public static void TaxPayloadRoundTripPreservesIdentityAndRate()
        {
            TaxStateV2 state = new TaxStateV2(new TaxKeyV2(1, 7, 3), 12);
            TaxStateV2 decoded = TaxDomainCodecV2.Decode(TaxDomainCodecV2.Encode(state));
            Assert.Equal(state.Key, decoded.Key);
            Assert.Equal(12, decoded.Rate);

            TaxIntentV2 intent = TaxDomainCodecV2.DecodeIntent(TaxDomainCodecV2.EncodeIntent(new TaxIntentV2(state)));
            Assert.Equal(state.Key, intent.Requested.Key);
            Assert.Equal(12, intent.Requested.Rate);
        }

        [Case]
        public static void TaxRootIsCanonicalAcrossInsertionOrder()
        {
            TaxStateV2 a = new TaxStateV2(new TaxKeyV2(1, 1, 1), 9);
            TaxStateV2 b = new TaxStateV2(new TaxKeyV2(4, 2, 5), 13);
            TaxStateIndexV2 first = new TaxStateIndexV2(); first.Upsert(a); first.Upsert(b);
            TaxStateIndexV2 second = new TaxStateIndexV2(); second.Upsert(b); second.Upsert(a);
            Assert.Equal(first.Root, second.Root);
        }

        [Case]
        public static void TaxRateAndTrailingPayloadFailClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new TaxStateV2(new TaxKeyV2(1, 2, 3), 30); });
            byte[] encoded = TaxDomainCodecV2.Encode(new TaxStateV2(new TaxKeyV2(1, 2, 3), 10));
            byte[] tainted = new byte[encoded.Length + 1]; Buffer.BlockCopy(encoded, 0, tainted, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { TaxDomainCodecV2.Decode(tainted); });
        }
    }
}
