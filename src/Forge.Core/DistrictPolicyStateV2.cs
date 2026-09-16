using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public sealed class DistrictPolicyStateV2
    {
        public EntityIdentityV2 District { get; private set; }
        public ulong Services { get; private set; }
        public ulong Taxation { get; private set; }
        public ulong CityPlanning { get; private set; }
        public ulong Special { get; private set; }

        public DistrictPolicyStateV2(EntityIdentityV2 district, ulong services, ulong taxation,
            ulong cityPlanning, ulong special)
        {
            if (!district.IsValid) throw new ArgumentException("Invalid district identity.", "district");
            District = district;
            Services = services;
            Taxation = taxation;
            CityPlanning = cityPlanning;
            Special = special;
        }
    }

    public sealed class DistrictPolicySnapshotV2
    {
        private readonly DistrictPolicyStateV2[] districts;
        public ulong CityServices { get; private set; }
        public ulong CityTaxation { get; private set; }
        public ulong CityPlanning { get; private set; }
        public ulong CitySpecial { get; private set; }
        public DistrictPolicyStateV2[] Districts { get { return (DistrictPolicyStateV2[])districts.Clone(); } }
        public Hash256 Root { get { return Hash256.Compute(DistrictPolicyEnvelopeCodecV2.EncodePolicySnapshot(this)); } }

        public DistrictPolicySnapshotV2(ulong cityServices, ulong cityTaxation, ulong cityPlanning,
            ulong citySpecial, DistrictPolicyStateV2[] values)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (values.Length > 127) throw new ArgumentException("Too many district policy entries.", "values");
            CityServices = cityServices;
            CityTaxation = cityTaxation;
            CityPlanning = cityPlanning;
            CitySpecial = citySpecial;
            districts = (DistrictPolicyStateV2[])values.Clone();
            Array.Sort(districts, delegate(DistrictPolicyStateV2 a, DistrictPolicyStateV2 b)
            {
                if (a == null || b == null) throw new ArgumentException("Null district policy state.", "values");
                return a.District.EntityId.CompareTo(b.District.EntityId);
            });
            ulong previous = 0;
            for (int i = 0; i < districts.Length; i++)
            {
                DistrictPolicyStateV2 value = districts[i];
                if (value == null || !value.District.IsValid || value.District.EntityId <= previous)
                    throw new ArgumentException("District policy identities must be unique and valid.", "values");
                previous = value.District.EntityId;
            }
        }

        public bool TryGet(EntityIdentityV2 district, out DistrictPolicyStateV2 value)
        {
            value = null;
            if (!district.IsValid) return false;
            int low = 0, high = districts.Length - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                DistrictPolicyStateV2 current = districts[mid];
                int compare = current.District.EntityId.CompareTo(district.EntityId);
                if (compare == 0)
                {
                    if (!current.District.Equals(district)) return false;
                    value = current;
                    return true;
                }
                if (compare < 0) low = mid + 1; else high = mid - 1;
            }
            return false;
        }
    }

    public sealed class DistrictAuthorityEnvelopeV2
    {
        public DistrictMutationV2 DistrictMutation { get; private set; }
        public Hash256 DistrictAfterRoot { get; private set; }
        public DistrictPolicySnapshotV2 Policies { get; private set; }

        public DistrictAuthorityEnvelopeV2(DistrictMutationV2 districtMutation, Hash256 districtAfterRoot,
            DistrictPolicySnapshotV2 policies)
        {
            if (districtMutation == null) throw new ArgumentNullException("districtMutation");
            if (districtAfterRoot == null) throw new ArgumentNullException("districtAfterRoot");
            if (policies == null) throw new ArgumentNullException("policies");
            DistrictMutation = districtMutation;
            DistrictAfterRoot = districtAfterRoot;
            Policies = policies;
        }
    }

    public static class DistrictCombinedRootV2
    {
        public static Hash256 Combine(Hash256 districtRoot, Hash256 policyRoot)
        {
            if (districtRoot == null || policyRoot == null) throw new ArgumentNullException("districtRoot");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x32524446u); // FDR2
                writer.Write(districtRoot.ToArray());
                writer.Write(policyRoot.ToArray());
                writer.Flush();
                return Hash256.Compute(stream.ToArray());
            }
        }
    }

    public static class DistrictPolicyEnvelopeCodecV2
    {
        private const uint PolicyMagic = 0x32504446u; // FDP2
        private const uint EnvelopeMagic = 0x32454446u; // FDE2

        public static byte[] EncodePolicySnapshot(DistrictPolicySnapshotV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            DistrictPolicyStateV2[] districts = value.Districts;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(PolicyMagic);
                writer.Write(value.CityServices);
                writer.Write(value.CityTaxation);
                writer.Write(value.CityPlanning);
                writer.Write(value.CitySpecial);
                writer.Write((byte)districts.Length);
                for (int i = 0; i < districts.Length; i++)
                {
                    DistrictPolicyStateV2 district = districts[i];
                    writer.Write(district.District.EntityId);
                    writer.Write(district.District.Generation);
                    writer.Write(district.Services);
                    writer.Write(district.Taxation);
                    writer.Write(district.CityPlanning);
                    writer.Write(district.Special);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static DistrictPolicySnapshotV2 DecodePolicySnapshot(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 37 || bytes.Length > 8192)
                throw new InvalidDataException("Invalid district policy snapshot length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != PolicyMagic) throw new InvalidDataException("Unknown district policy snapshot magic.");
                ulong cityServices = reader.ReadUInt64();
                ulong cityTaxation = reader.ReadUInt64();
                ulong cityPlanning = reader.ReadUInt64();
                ulong citySpecial = reader.ReadUInt64();
                byte count = reader.ReadByte();
                if (count > 127) throw new InvalidDataException("District policy snapshot exceeds supported bounds.");
                DistrictPolicyStateV2[] districts = new DistrictPolicyStateV2[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 id = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (id.EntityId <= previous) throw new InvalidDataException("District policy snapshot is not canonical.");
                    previous = id.EntityId;
                    districts[i] = new DistrictPolicyStateV2(id, reader.ReadUInt64(), reader.ReadUInt64(),
                        reader.ReadUInt64(), reader.ReadUInt64());
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected trailing district policy bytes.");
                return new DistrictPolicySnapshotV2(cityServices, cityTaxation, cityPlanning, citySpecial, districts);
            }
        }

        public static byte[] EncodeEnvelope(DistrictAuthorityEnvelopeV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            byte[] district = DistrictDomainCodecV2.EncodeMutation(value.DistrictMutation);
            byte[] policies = EncodePolicySnapshot(value.Policies);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(EnvelopeMagic);
                writer.Write(value.DistrictAfterRoot.ToArray());
                writer.Write((uint)district.Length);
                writer.Write(district);
                writer.Write((ushort)policies.Length);
                writer.Write(policies);
                writer.Flush();
                byte[] result = stream.ToArray();
                if (result.Length > Limits.FramePayloadBytes)
                    throw new InvalidDataException("District authority envelope exceeds frame payload budget.");
                return result;
            }
        }

        public static DistrictAuthorityEnvelopeV2 DecodeEnvelope(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 48 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid district authority envelope length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != EnvelopeMagic) throw new InvalidDataException("Unknown district authority envelope magic.");
                byte[] rootBytes = reader.ReadBytes(Hash256.Size);
                if (rootBytes.Length != Hash256.Size) throw new InvalidDataException("Truncated district child root.");
                Hash256 districtAfterRoot = new Hash256(rootBytes);
                uint districtLength = reader.ReadUInt32();
                if (districtLength == 0 || districtLength > Limits.FramePayloadBytes || districtLength > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("Invalid district mutation length in envelope.");
                byte[] districtBytes = reader.ReadBytes((int)districtLength);
                ushort policyLength = reader.ReadUInt16();
                if (policyLength == 0 || policyLength > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("Invalid district policy length in envelope.");
                byte[] policyBytes = reader.ReadBytes(policyLength);
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected trailing district authority envelope bytes.");
                return new DistrictAuthorityEnvelopeV2(DistrictDomainCodecV2.DecodeMutation(districtBytes),
                    districtAfterRoot, DecodePolicySnapshot(policyBytes));
            }
        }

        public static DistrictMutationV2 EmptyMutation()
        {
            return new DistrictMutationV2(new DistrictEntityStateV2[0], new EntityIdentityV2[0], new DistrictCellStateV2[0]);
        }
    }
}
