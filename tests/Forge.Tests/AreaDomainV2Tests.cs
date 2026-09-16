using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class AreaDomainV2Tests
    {
        [Case]
        public static void AreaStateRoundTripsAndUsesFiveByFiveMask()
        {
            uint mask = (1u << 0) | (1u << 12) | (1u << 24);
            AreaStateV2 state = new AreaStateV2(mask);
            AreaStateV2 copy = AreaDomainCodecV2.DecodeState(AreaDomainCodecV2.EncodeState(state));
            Assert.Equal(mask, copy.UnlockedMask);
            Assert.True(copy.IsUnlocked(0, 0));
            Assert.True(copy.IsUnlocked(2, 2));
            Assert.True(copy.IsUnlocked(4, 4));
            Assert.True(!copy.IsUnlocked(1, 1));
            Assert.Equal(state.Root, copy.Root);
        }

        [Case]
        public static void AreaIntentRoundTripsCoordinates()
        {
            AreaUnlockIntentV2 intent = new AreaUnlockIntentV2(4, 3);
            AreaUnlockIntentV2 copy = AreaDomainCodecV2.DecodeIntent(AreaDomainCodecV2.EncodeIntent(intent));
            Assert.Equal(4, copy.X);
            Assert.Equal(3, copy.Z);
        }

        [Case]
        public static void AreaRejectsOutOfBoundsAndMalformedState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaUnlockIntentV2(5, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaStateV2(1u << 25); });
            Assert.Throws<InvalidDataException>(delegate { AreaDomainCodecV2.DecodeState(new byte[1]); });
            Assert.Throws<InvalidDataException>(delegate { AreaDomainCodecV2.DecodeIntent(new byte[1]); });
        }
    }
}
