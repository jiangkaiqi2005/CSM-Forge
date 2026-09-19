using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class SimulationClockStateV2
    {
        public bool Paused { get; private set; }
        public int Speed { get; private set; }

        public SimulationClockStateV2(bool paused, int speed)
        {
            Check.OutOfRange(speed < 0 || speed > 3, "speed");
            Paused = paused;
            Speed = speed;
        }
    }

    public sealed class SimulationClockIntentV2
    {
        public SimulationClockStateV2 Requested { get; private set; }

        public SimulationClockIntentV2(SimulationClockStateV2 requested)
        {
            Check.NotNull(requested, "requested");
            Requested = requested;
        }
    }

    public static class SimulationClockDomainCodecV2
    {
        private const int Bytes = 2;

        public static byte[] Encode(SimulationClockStateV2 value)
        {
            Check.NotNull(value, "value");
            return new byte[] { (byte)(value.Paused ? 1 : 0), (byte)value.Speed };
        }

        public static SimulationClockStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != Bytes) throw new InvalidDataException("Invalid simulation clock payload length.");
            if (bytes[0] > 1 || bytes[1] > 3) throw new InvalidDataException("Invalid simulation clock payload.");
            return new SimulationClockStateV2(bytes[0] == 1, bytes[1]);
        }

        public static byte[] EncodeIntent(SimulationClockIntentV2 value)
        {
            Check.NotNull(value, "value");
            return Encode(value.Requested);
        }

        public static SimulationClockIntentV2 DecodeIntent(byte[] bytes)
        {
            return new SimulationClockIntentV2(Decode(bytes));
        }

        public static Hash256 Root(SimulationClockStateV2 value)
        {
            return Hash256.Compute(Encode(value));
        }
    }
}
