using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class EconomyCashStateV2
    {
        /// <summary>Raw CS1 cash amount in hundredths of the displayed currency unit.</summary>
        public long RawCash { get; private set; }

        public EconomyCashStateV2(long rawCash)
        {
            RawCash = rawCash;
        }

        public Hash256 Root
        {
            get { return Hash256.Compute(EconomyCashCodecV2.Encode(this)); }
        }
    }

    public static class EconomyCashCodecV2
    {
        private const uint Magic = 0x32434645u; // EFC2
        public const int Bytes = 12;

        public static byte[] Encode(EconomyCashStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(value.RawCash);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static EconomyCashStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != Bytes) throw new InvalidDataException("Invalid economy cash payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unknown economy cash payload magic.");
                return new EconomyCashStateV2(reader.ReadInt64());
            }
        }
    }
}
