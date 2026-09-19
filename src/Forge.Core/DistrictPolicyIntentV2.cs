using System;
using System.IO;

namespace CsmForge.Core
{
    public enum DistrictPolicyTargetKindV2 : byte
    {
        City = 0,
        District = 1
    }

    public sealed class DistrictPolicyIntentV2
    {
        public DistrictPolicyTargetKindV2 TargetKind { get; private set; }
        public EntityIdentityV2 District { get; private set; }
        public int PolicyValue { get; private set; }
        public bool Enabled { get; private set; }

        public DistrictPolicyIntentV2(DistrictPolicyTargetKindV2 targetKind, EntityIdentityV2 district,
            int policyValue, bool enabled)
        {
            if (targetKind != DistrictPolicyTargetKindV2.City && targetKind != DistrictPolicyTargetKindV2.District)
                throw new ArgumentOutOfRangeException("targetKind");
            if (targetKind == DistrictPolicyTargetKindV2.District && !district.IsValid)
                throw new ArgumentException("District policy requires a stable district identity.", "district");
            if (targetKind == DistrictPolicyTargetKindV2.City && district.IsValid)
                throw new ArgumentException("City policy must not carry a district identity.", "district");
            if (policyValue == 0) throw new ArgumentOutOfRangeException("policyValue");
            TargetKind = targetKind;
            District = district;
            PolicyValue = policyValue;
            Enabled = enabled;
        }

        public DistrictPolicyIntentV2(EntityIdentityV2 district, int policyValue, bool enabled)
            : this(DistrictPolicyTargetKindV2.District, district, policyValue, enabled) { }

        public static DistrictPolicyIntentV2 City(int policyValue, bool enabled)
        {
            return new DistrictPolicyIntentV2(DistrictPolicyTargetKindV2.City,
                default(EntityIdentityV2), policyValue, enabled);
        }
    }

    public static class DistrictPolicyCodecV2
    {
        private const byte Marker = 0xD4;
        public const int Bytes = 19;

        public static bool LooksLikePolicy(byte[] bytes)
        {
            return bytes != null && bytes.Length == Bytes && bytes[0] == Marker;
        }

        public static byte[] Encode(DistrictPolicyIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Marker);
                writer.Write((byte)value.TargetKind);
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
                DistrictPolicyTargetKindV2 target = (DistrictPolicyTargetKindV2)reader.ReadByte();
                ulong entity = reader.ReadUInt64();
                uint generation = reader.ReadUInt32();
                EntityIdentityV2 district = entity == 0 && generation == 0
                    ? default(EntityIdentityV2)
                    : new EntityIdentityV2(entity, generation);
                int policy = reader.ReadInt32();
                byte enabled = reader.ReadByte();
                if (enabled > 1) throw new InvalidDataException("Invalid district policy enabled flag.");
                return new DistrictPolicyIntentV2(target, district, policy, enabled == 1);
            }
        }
    }
}
