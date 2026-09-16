using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class SimulationClockDomainV2Tests
    {
        [Case]
        public static void ClockRoundTripPreservesAbsoluteState()
        {
            SimulationClockStateV2 value = new SimulationClockStateV2(true, 3);
            SimulationClockStateV2 decoded = SimulationClockDomainCodecV2.Decode(SimulationClockDomainCodecV2.Encode(value));
            Assert.True(decoded.Paused);
            Assert.Equal(3, decoded.Speed);
            Assert.Equal(SimulationClockDomainCodecV2.Root(value), SimulationClockDomainCodecV2.Root(decoded));
        }

        [Case]
        public static void ClockCodecRejectsInvalidFlagsAndLengths()
        {
            Assert.Throws<InvalidDataException>(delegate { SimulationClockDomainCodecV2.Decode(new byte[] { 2, 1 }); });
            Assert.Throws<InvalidDataException>(delegate { SimulationClockDomainCodecV2.Decode(new byte[] { 0, 4 }); });
            Assert.Throws<InvalidDataException>(delegate { SimulationClockDomainCodecV2.Decode(new byte[] { 0 }); });
        }

        [Case]
        public static void ClockStateRejectsUnsupportedSpeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new SimulationClockStateV2(false, -1); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new SimulationClockStateV2(false, 4); });
        }
    }
}
