using System;
using System.Collections.Generic;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Campus state that is not a simple UI scalar: coach hire timestamps and the dynamic varsity
    /// attractiveness modifier are Host-owned because academic-year and coach hiring use local time
    /// and simulation decisions. Park identity is shared with builtin.districtpark.
    /// </summary>
    internal sealed class CampusDeepStateAdapter : IForgeStateAdapterV2
    {
        private const uint Magic = 0x31434446u; // FDC1
        internal const string Adapter = "builtin.districtpark-campus";
        private const string IdentityNamespace = "builtin.districtpark";
        private const int MaxHireTimes = 64;

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            Check.NotNull(context, "context");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            if (context.IsAuthoritative) EnsureHostMappings(ids, manager);
            EntityMapEntryV2[] mappings = ids.SnapshotEntries();
            List<CampusState> values = new List<CampusState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                if (mappings[i].NativeId == 0 || mappings[i].NativeId > byte.MaxValue) continue;
                byte native = (byte)mappings[i].NativeId;
                if (!Live(native)) continue;
                DistrictPark park = manager.m_parks.m_buffer[native];
                DateTime[] hireTimes = park.m_coachHireTimes;
                if (hireTimes != null && hireTimes.Length > MaxHireTimes)
                    throw new InvalidOperationException("Campus coach hire-time array exceeds Forge bound.");
                values.Add(new CampusState
                {
                    Identity = mappings[i].Identity,
                    CoachCount = park.m_coachCount,
                    GrantType = park.m_grantType,
                    DynamicAttractiveness = park.m_dynamicVarsityAttractivenessModifier,
                    HireTimes = hireTimes == null ? new DateTime[0] : (DateTime[])hireTimes.Clone()
                });
            }
            values.Sort(delegate(CampusState a, CampusState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(values);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            CampusState[] values = Decode(state);
            for (int i = 0; i < values.Length; i++)
            {
                CampusState value = values[i];
                uint nativeValue;
                if (!ids.TryGetNative(value.Identity, out nativeValue) || nativeValue == 0 || nativeValue > byte.MaxValue)
                    throw new InvalidOperationException("Campus deep state references an unavailable DistrictPark identity.");
                byte native = (byte)nativeValue;
                if (!Live(native)) throw new InvalidOperationException("Campus deep-state target is not live.");
                DistrictPark park = manager.m_parks.m_buffer[native];
                park.m_coachCount = value.CoachCount;
                park.m_grantType = value.GrantType;
                park.m_dynamicVarsityAttractivenessModifier = value.DynamicAttractiveness;
                park.m_coachHireTimes = value.HireTimes.Length == 0 ? null : (DateTime[])value.HireTimes.Clone();
                manager.m_parks.m_buffer[native] = park;
            }
        }

        private static void EnsureHostMappings(EntityIdMapV2 ids, DistrictManager manager)
        {
            int limit = manager.m_parks.m_buffer.Length;
            if (limit > byte.MaxValue + 1) limit = byte.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                byte native = (byte)i;
                if (!Live(native)) continue;
                EntityIdentityV2 identity;
                if (!ids.TryGetIdentity(native, out identity)) ids.Allocate(native);
            }
        }

        private static bool Live(byte native)
        {
            return native != 0 && DistrictManager.instance != null &&
                (DistrictManager.instance.m_parks.m_buffer[native].m_flags & DistrictPark.Flags.Created) != DistrictPark.Flags.None;
        }

        private static byte[] Encode(List<CampusState> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    CampusState value = values[i];
                    writer.Write(value.Identity.EntityId); writer.Write(value.Identity.Generation);
                    writer.Write(value.CoachCount); writer.Write(value.GrantType); writer.Write(value.DynamicAttractiveness);
                    writer.Write((byte)value.HireTimes.Length);
                    for (int j = 0; j < value.HireTimes.Length; j++) writer.Write(value.HireTimes[j].Ticks);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Campus deep state exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static CampusState[] Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid Campus deep-state size.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid Campus deep-state magic.");
                int count = reader.ReadUInt16();
                CampusState[] result = new CampusState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("Campus deep state is not canonically ordered.");
                    previous = identity.EntityId;
                    int hireCount;
                    CampusState value = new CampusState
                    {
                        Identity = identity,
                        CoachCount = reader.ReadByte(),
                        GrantType = reader.ReadByte(),
                        DynamicAttractiveness = reader.ReadInt32()
                    };
                    hireCount = reader.ReadByte();
                    if (hireCount > MaxHireTimes) throw new InvalidDataException("Campus coach hire-time array exceeds Forge bound.");
                    value.HireTimes = new DateTime[hireCount];
                    for (int j = 0; j < hireCount; j++) value.HireTimes[j] = new DateTime(reader.ReadInt64());
                    result[i] = value;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Campus deep-state bytes.");
                return result;
            }
        }

        private sealed class CampusState
        {
            public EntityIdentityV2 Identity;
            public byte CoachCount;
            public byte GrantType;
            public int DynamicAttractiveness;
            public DateTime[] HireTimes;
        }
    }
}
