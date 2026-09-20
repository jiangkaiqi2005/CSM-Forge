using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class AreaDomainV2Tests
    {
        private static AreaStateV2 Vanilla(params int[] cells)
        {
            ulong low = 0, high = 0;
            foreach (int cell in cells) AreaStateV2.SetBit(ref low, ref high, cell);
            return new AreaStateV2(AreaStateV2.VanillaResolution, low, high);
        }

        [Case]
        public static void AreaStateRoundTripsAtVanillaResolution()
        {
            AreaStateV2 state = Vanilla(0, 12, 24); // (0,0), (2,2), (4,4)
            AreaStateV2 copy = AreaDomainCodecV2.DecodeState(AreaDomainCodecV2.EncodeState(state));
            Assert.Equal(AreaStateV2.VanillaResolution, copy.Resolution);
            Assert.True(copy.IsUnlocked(0, 0));
            Assert.True(copy.IsUnlocked(2, 2));
            Assert.True(copy.IsUnlocked(4, 4));
            Assert.True(!copy.IsUnlocked(1, 1));
            Assert.True(state.Root.Equals(copy.Root));
        }

        [Case]
        public static void ExpandedNineByNineGridRoundTripsIncludingOuterAreas()
        {
            // Regression (in-game host-start failure): 81 Tiles 2 replaces m_areaGrid with 81
            // cells and transpiles UnlockArea's stride 5 -> 9. A 25-bit mask silently dropped
            // (5,y)/(x,5)+ cells and the patch's hardcoded "< 25" rejected them, fencing the room.
            const int Nine = 9;
            ulong low = 0, high = 0;
            AreaStateV2.SetBit(ref low, ref high, 0);           // (0,0)
            AreaStateV2.SetBit(ref low, ref high, 8 * Nine + 8); // (8,8) -> bit 80, high word
            AreaStateV2.SetBit(ref low, ref high, 4 * Nine + 5); // (5,4) -> bit 41
            AreaStateV2 state = new AreaStateV2(Nine, low, high);

            AreaStateV2 copy = AreaDomainCodecV2.DecodeState(AreaDomainCodecV2.EncodeState(state));
            Assert.Equal(Nine, copy.Resolution);
            Assert.True(copy.IsUnlocked(0, 0));
            Assert.True(copy.IsUnlocked(5, 4));
            Assert.True(copy.IsUnlocked(8, 8));   // beyond the vanilla 25 bits
            Assert.True(!copy.IsUnlocked(3, 3));
            Assert.True(state.Root.Equals(copy.Root));
        }

        [Case]
        public static void ResolutionIsPartOfTheRootSoMismatchIsVisible()
        {
            ulong low = 1, high = 0;
            Assert.True(!new AreaStateV2(5, low, high).Root.Equals(new AreaStateV2(9, low, high).Root));
        }

        [Case]
        public static void AreaIntentRoundTripsCoordinates()
        {
            AreaUnlockIntentV2 intent = new AreaUnlockIntentV2(8, 7); // valid for a 9x9 grid
            AreaUnlockIntentV2 copy = AreaDomainCodecV2.DecodeIntent(AreaDomainCodecV2.EncodeIntent(intent));
            Assert.Equal(8, copy.X);
            Assert.Equal(7, copy.Z);
        }

        [Case]
        public static void AreaRejectsOutOfBoundsAndMalformedState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaUnlockIntentV2(-1, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaUnlockIntentV2(AreaStateV2.MaximumResolution, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaStateV2(12, 0, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaStateV2(5, 1UL << 25, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new AreaStateV2(5, 0, 1UL); });
            Assert.Throws<InvalidDataException>(delegate { AreaDomainCodecV2.DecodeState(new byte[1]); });
            Assert.Throws<InvalidDataException>(delegate { AreaDomainCodecV2.DecodeIntent(new byte[1]); });
        }
    }
}
