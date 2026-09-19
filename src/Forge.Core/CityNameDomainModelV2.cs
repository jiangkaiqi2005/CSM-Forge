using System;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public sealed class CityNameStateV2
    {
        public string Name { get; private set; }
        public Hash256 Root { get { return Hash256.Compute(CityNameCodecV2.Encode(this)); } }

        public CityNameStateV2(string name)
        {
            if (name == null) name = string.Empty;
            Check.Condition(Encoding.UTF8.GetByteCount(name) > 512, "name", "City name exceeds Forge wire bounds.");
            Name = name;
        }
    }

    public static class CityNameCodecV2
    {
        private const uint Magic = 0x324E4346u; // FCN2

        public static byte[] Encode(CityNameStateV2 value)
        {
            Check.NotNull(value, "value");
            byte[] text = Encoding.UTF8.GetBytes(value.Name);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(Magic); writer.Write((ushort)text.Length); writer.Write(text);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static CityNameStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 6 || bytes.Length > 518) throw new InvalidDataException("Invalid city-name payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unknown city-name payload magic.");
                ushort length = reader.ReadUInt16(); byte[] text = reader.ReadBytes(length);
                if (text.Length != length || reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Truncated or trailing city-name payload.");
                return new CityNameStateV2(Encoding.UTF8.GetString(text));
            }
        }
    }
}
