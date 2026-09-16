using System;
using System.IO;
using CsmForge.Core;
using Xunit;

namespace Forge.Tests
{
    public sealed class SimulationClockDomainV2Tests
    {
        [Fact]
        public void Codec_round_trips_absolute_state()
        {
            SimulationClockStateV2 value = new SimulationClockStateV2(true, 3);
            SimulationClockStateV2 decoded = SimulationClockDomainCodecV2.Decode(SimulationClockDomainCodecV2.Encode(value));
            Assert.True(decoded.Paused);
            Assert.Equal(3, decoded.Speed);
            Assert.Equal(SimulationClockDomainCodecV2.Root(value), SimulationClockDomainCodecV2.Root(decoded));
        }

        [Fact]
        public void Codec_rejects_invalid_flags_and_lengths()
        {
            Assert.Throws<InvalidDataException>(() => SimulationClockDomainCodecV2.Decode(new byte[] { 2, 1 }));
            Assert.Throws<InvalidDataException>(() => SimulationClockDomainCodecV2.Decode(new byte[] { 0, 4 }));
            Assert.Throws<InvalidDataException>(() => SimulationClockDomainCodecV2.Decode(new byte[] { 0 }));
        }

        [Fact]
        public void State_rejects_unsupported_speed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClockStateV2(false, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClockStateV2(false, 4));
        }
    }
}
