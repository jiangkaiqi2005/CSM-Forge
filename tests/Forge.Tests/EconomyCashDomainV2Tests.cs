using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class EconomyCashDomainV2Tests
    {
        [Case]
        public static void CashRoundTripPreservesRawHundredths()
        {
            EconomyCashStateV2 value = new EconomyCashStateV2(1234567890123L);
            EconomyCashStateV2 copy = EconomyCashCodecV2.Decode(EconomyCashCodecV2.Encode(value));
            Assert.Equal(value.RawCash, copy.RawCash);
            Assert.Equal(value.Root, copy.Root);
        }

        [Case]
        public static void CashSupportsDebtAndLargeBalances()
        {
            EconomyCashStateV2 debt = new EconomyCashStateV2(-500000L);
            EconomyCashStateV2 large = new EconomyCashStateV2(long.MaxValue - 1);
            Assert.Equal((long)-500000, EconomyCashCodecV2.Decode(EconomyCashCodecV2.Encode(debt)).RawCash);
            Assert.Equal(long.MaxValue - 1, EconomyCashCodecV2.Decode(EconomyCashCodecV2.Encode(large)).RawCash);
            Assert.True(!debt.Root.Equals(large.Root));
        }

        [Case]
        public static void CashCodecRejectsWrongLengthAndMagic()
        {
            Assert.Throws<InvalidDataException>(delegate { EconomyCashCodecV2.Decode(new byte[8]); });
            byte[] bytes = EconomyCashCodecV2.Encode(new EconomyCashStateV2(100));
            bytes[0] ^= 0x7F;
            Assert.Throws<InvalidDataException>(delegate { EconomyCashCodecV2.Decode(bytes); });
        }
    }
}
