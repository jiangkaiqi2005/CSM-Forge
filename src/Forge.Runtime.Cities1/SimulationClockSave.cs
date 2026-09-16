using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed class SimulationClockSaveStore
    {
        private readonly object gate = new object();
        private SimulationClockStateV2 pending;

        public void LoadPending(byte[] bytes)
        {
            SimulationClockStateV2 value = null;
            if (bytes != null && bytes.Length != 0)
            {
                if (bytes.Length != 8) throw new InvalidDataException("Invalid Forge simulation clock save length.");
                using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
                {
                    if (reader.ReadUInt32() != 0x4B4C4346u) throw new InvalidDataException("Unknown Forge simulation clock save magic."); // FCLK
                    if (reader.ReadUInt16() != 1) throw new InvalidDataException("Unsupported Forge simulation clock save schema.");
                    byte paused = reader.ReadByte(); byte speed = reader.ReadByte();
                    if (paused > 1 || speed > 3) throw new InvalidDataException("Invalid Forge simulation clock save payload.");
                    value = new SimulationClockStateV2(paused == 1, speed);
                }
            }
            lock (gate) pending = value;
        }

        public void ApplyPending(LoadIdentity load)
        {
            SimulationClockStateV2 value;
            lock (gate) { value = pending; pending = null; }
            if (value != null) SimulationClockGameAccess.Apply(load, value);
        }

        public byte[] EncodeCurrent()
        {
            SimulationClockStateV2 value = SimulationClockGameAccess.Capture();
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x4B4C4346u); writer.Write((ushort)1);
                writer.Write((byte)(value.Paused ? 1 : 0)); writer.Write((byte)value.Speed);
                writer.Flush(); return stream.ToArray();
            }
        }

        public void Clear() { lock (gate) pending = null; }
    }
}
