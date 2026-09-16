using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class AreaStateV2
    {
        private const uint ValidMask = (1u << 25) - 1u;
        public uint UnlockedMask { get; private set; }
        public Hash256 Root { get { return Hash256.Compute(AreaDomainCodecV2.EncodeState(this)); } }

        public AreaStateV2(uint unlockedMask)
        {
            if ((unlockedMask & ~ValidMask) != 0) throw new ArgumentOutOfRangeException("unlockedMask");
            UnlockedMask = unlockedMask;
        }

        public bool IsUnlocked(int x, int z)
        {
            if (x < 0 || x >= 5 || z < 0 || z >= 5) return false;
            return (UnlockedMask & (1u << (z * 5 + x))) != 0;
        }
    }

    public sealed class AreaUnlockIntentV2
    {
        public int X { get; private set; }
        public int Z { get; private set; }
        public AreaUnlockIntentV2(int x, int z)
        {
            if (x < 0 || x >= 5 || z < 0 || z >= 5) throw new ArgumentOutOfRangeException("area coordinates");
            X = x; Z = z;
        }
    }

    public static class AreaDomainCodecV2
    {
        private const uint StateMagic = 0x32524146u; // FAR2
        private const uint IntentMagic = 0x32494146u; // FAI2
        public const int StateBytes = 8;
        public const int IntentBytes = 6;

        public static byte[] EncodeState(AreaStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(StateMagic); writer.Write(value.UnlockedMask);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static AreaStateV2 DecodeState(byte[] bytes)
        {
            if (bytes == null || bytes.Length != StateBytes) throw new InvalidDataException("Invalid area state length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != StateMagic) throw new InvalidDataException("Unknown area state magic.");
                return new AreaStateV2(reader.ReadUInt32());
            }
        }

        public static byte[] EncodeIntent(AreaUnlockIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(IntentMagic); writer.Write((byte)value.X); writer.Write((byte)value.Z);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static AreaUnlockIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length != IntentBytes) throw new InvalidDataException("Invalid area intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != IntentMagic) throw new InvalidDataException("Unknown area intent magic.");
                return new AreaUnlockIntentV2(reader.ReadByte(), reader.ReadByte());
            }
        }
    }
}
