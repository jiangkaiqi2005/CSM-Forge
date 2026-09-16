using System;
using System.IO;

namespace CsmForge.Core
{
    public sealed class DistrictPolicyIntentV2
    {
        public EntityIdentityV2 District { get; private set; }
        public int PolicyValue { get; private set; }
        public bool Enabled { get; private set; }

        public DistrictPolicyIntentV2(EntityIdentityV2 district, int policyValue, bool enabled)
        {
            if (!district.IsValid) throw new ArgumentException("Invalid district identity.", "district");
            if (policyValue == 0) throw new ArgumentOutOfRangeException("policyValue");
            District = district;
            PolicyValue = policyValue;
            Enabled = enabled;
        }
    }

    public static class DistrictPolicyCodecV2
    {
        private const byte Marker = 0xD4;
        public const int Bytes = 18;

        public static bool LooksLikePolicy(byte[] bytes)
        {
            return bytes != null && bytes.Length == Bytes && bytes[0] == Marker;
        }

        public static byte[] Encode(DistrictPolicyIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Marker);
                writer.Write(value.District.EntityId);
                writer.Write(value.District.Generation);
                writer.Write(value.PolicyValue);
                writer.Write((byte)(value.Enabled ? 1 : 0));
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static DistrictPolicyIntentV2 Decode(byte[] bytes)
        {
            if (!LooksLikePolicy(bytes)) throw new InvalidDataException("Invalid district policy payload.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadByte() != Marker) throw new InvalidDataException("Invalid district policy marker.");
                EntityIdentityV2 district = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                int policy = reader.ReadInt32();
                byte enabled = reader.ReadByte();
                if (enabled > 1) throw new InvalidDataException("Invalid district policy enabled flag.");
                return new DistrictPolicyIntentV2(district, policy, enabled == 1);
            }
        }
    }
}
