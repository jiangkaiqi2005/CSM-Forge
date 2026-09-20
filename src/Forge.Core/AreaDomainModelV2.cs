using System;
using System.IO;

namespace CsmForge.Core
{
    /// <summary>
    /// Unlocked-area bitmask, resolution carrying. Vanilla CS1 unlocks a 5x5 grid, but area mods
    /// enlarge it (81 Tiles 2 allocates m_areaGrid = 81 and transpiles UnlockArea's stride 5 -> 9),
    /// so a fixed 25-bit mask both missed the outer areas and rejected them. The mask is two words
    /// (up to 11x11) and the resolution travels with the state so host and replica agree on the
    /// bit layout; a mismatch changes the root and is caught by the normal root comparison.
    /// </summary>
    public sealed class AreaStateV2
    {
        public const int VanillaResolution = 5;
        public const int MaximumResolution = 11; // 11*11 = 121 <= 128 bits
        public const int VanillaCellCount = VanillaResolution * VanillaResolution;

        public int Resolution { get; private set; }
        public ulong MaskLow { get; private set; }
        public ulong MaskHigh { get; private set; }
        public Hash256 Root { get { return Hash256.Compute(AreaDomainCodecV2.EncodeState(this)); } }

        public AreaStateV2(int resolution, ulong maskLow, ulong maskHigh)
        {
            Check.OutOfRange(resolution < 1 || resolution > MaximumResolution, "resolution");
            int cells = resolution * resolution;
            Check.OutOfRange(AboveCellIsSet(maskLow, maskHigh, cells), "unlockedMask");
            Resolution = resolution; MaskLow = maskLow; MaskHigh = maskHigh;
        }

        public bool IsUnlocked(int x, int z)
        {
            if (x < 0 || x >= Resolution || z < 0 || z >= Resolution) return false;
            return GetBit(z * Resolution + x);
        }

        private bool GetBit(int bit)
        {
            return bit < 64 ? (MaskLow & (1UL << bit)) != 0 : (MaskHigh & (1UL << (bit - 64))) != 0;
        }

        /// <summary>True when any bit at or above <paramref name="cells"/> is set (illegal).</summary>
        private static bool AboveCellIsSet(ulong low, ulong high, int cells)
        {
            if (cells >= 64)
            {
                int highBits = cells - 64;
                if (highBits >= 64) return high != 0;
                ulong highValid = highBits == 0 ? 0UL : (1UL << highBits) - 1UL;
                return (high & ~highValid) != 0;
            }
            ulong lowValid = cells == 0 ? 0UL : (1UL << cells) - 1UL;
            return (low & ~lowValid) != 0 || high != 0;
        }

        public static void SetBit(ref ulong low, ref ulong high, int bit)
        {
            if (bit < 64) low |= 1UL << bit; else high |= 1UL << (bit - 64);
        }
    }

    public sealed class AreaUnlockIntentV2
    {
        public int X { get; private set; }
        public int Z { get; private set; }
        public AreaUnlockIntentV2(int x, int z)
        {
            // Bounds are per-resolution at the receiving runtime; the model only rejects values
            // that could never address any supported grid.
            Check.OutOfRange(x < 0 || x >= AreaStateV2.MaximumResolution ||
                z < 0 || z >= AreaStateV2.MaximumResolution, "area coordinates");
            X = x; Z = z;
        }
    }

    public static class AreaDomainCodecV2
    {
        private const uint StateMagic = 0x32524146u; // FAR2
        private const uint IntentMagic = 0x32494146u; // FAI2
        public const int StateBytes = 4 + 1 + 8 + 8; // magic + resolution + mask low/high
        public const int IntentBytes = 6;

        public static byte[] EncodeState(AreaStateV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(StateMagic); writer.Write((byte)value.Resolution);
                writer.Write(value.MaskLow); writer.Write(value.MaskHigh);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static AreaStateV2 DecodeState(byte[] bytes)
        {
            if (bytes == null || bytes.Length != StateBytes) throw new InvalidDataException("Invalid area state length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != StateMagic) throw new InvalidDataException("Unknown area state magic.");
                return new AreaStateV2(reader.ReadByte(), reader.ReadUInt64(), reader.ReadUInt64());
            }
        }

        public static byte[] EncodeIntent(AreaUnlockIntentV2 value)
        {
            Check.NotNull(value, "value");
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
