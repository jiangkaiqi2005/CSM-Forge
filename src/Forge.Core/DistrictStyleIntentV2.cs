using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class DistrictStyleIntentV2
    {
        public EntityIdentityV2 District { get; private set; }
        public ushort Style { get; private set; }
        public DistrictStyleIntentV2(EntityIdentityV2 district, ushort style)
        {
            if (!district.IsValid) throw new ArgumentException("Invalid district identity.", "district");
            District = district; Style = style;
        }
    }

    public static class DistrictStyleCodecV2
    {
        private const byte Marker = 0xD3;
        public const int Bytes = 15;

        public static bool LooksLikeStyle(byte[] bytes)
        {
            return bytes != null && bytes.Length == Bytes && bytes[0] == Marker;
        }

        public static byte[] Encode(DistrictStyleIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(Marker);
                writer.Write(value.District.EntityId); writer.Write(value.District.Generation); writer.Write(value.Style);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static DistrictStyleIntentV2 Decode(byte[] bytes)
        {
            if (!LooksLikeStyle(bytes)) throw new InvalidDataException("Invalid district style payload.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadByte() != Marker) throw new InvalidDataException("Invalid district style marker.");
                return new DistrictStyleIntentV2(new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()), reader.ReadUInt16());
            }
        }
    }
}
