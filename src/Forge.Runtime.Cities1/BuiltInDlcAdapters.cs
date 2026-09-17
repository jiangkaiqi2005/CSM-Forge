using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class BuiltInDlcAdapters
    {
        private static bool registered;
        internal static void RegisterAll()
        {
            if (registered) return;
            ForgeExtensionApi.Register(new DistrictParkStateAdapter());
            registered = true;
        }
    }

    /// <summary>
    /// Shared persistent container for Parklife, Industries, Campus, Airports and pedestrian-area
    /// style DistrictPark entities. Native byte slots are never serialized as network identity.
    /// Area-grid cells and specialized high-volume simulation counters are intentionally separate.
    /// </summary>
    internal sealed class DistrictParkStateAdapter : IForgeStateAdapterV2
    {
        private const uint Magic = 0x31504446u; // FDP1
        private static readonly string[] ScalarFieldNames =
        {
            "m_parkPolicies", "m_eventPolicies", "m_academicStaffCount", "m_cheerleadingBudget",
            "m_coachCount", "m_ticketPrice", "m_varsityIdentityIndex", "m_grantType"
        };
        private static readonly FieldInfo[] ScalarFields = ResolveScalarFields();

        public string AdapterId { get { return "builtin.districtpark"; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            if (context == null) throw new ArgumentNullException("context");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            List<ParkState> states = new List<ParkState>();
            HashSet<ulong> liveEntities = new HashSet<ulong>();
            int limit = manager.m_parks.m_buffer.Length;
            if (limit > byte.MaxValue + 1) limit = byte.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                byte native = (byte)i;
                DistrictPark park = manager.m_parks.m_buffer[native];
                if (!Live(park)) continue;
                EntityIdentityV2 identity;
                if (!context.TryGetIdentity(native, out identity))
                {
                    if (!context.IsAuthoritative)
                        throw new InvalidOperationException("Replica DistrictPark has no Host-issued Forge identity.");
                    identity = context.GetOrAllocateIdentity(native);
                }
                liveEntities.Add(identity.EntityId);
                states.Add(CaptureState(identity, park));
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
                if (!liveEntities.Contains(mappings[i].Identity.EntityId) && !context.RetireIdentity(mappings[i].Identity))
                    throw new InvalidOperationException("Could not retire a stale DistrictPark identity.");

            states.Sort(delegate(ParkState a, ParkState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(states);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            ParkState[] desired = Decode(state);
            Dictionary<ulong, ParkState> wanted = new Dictionary<ulong, ParkState>();
            for (int i = 0; i < desired.Length; i++) wanted.Add(desired[i].Identity.EntityId, desired[i]);

            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            for (int i = 0; i < desired.Length; i++)
            {
                ParkState value = desired[i];
                uint nativeValue;
                byte native;
                if (context.TryGetNative(value.Identity, out nativeValue))
                {
                    if (nativeValue == 0 || nativeValue > byte.MaxValue) throw new InvalidOperationException("DistrictPark mapping exceeds CS1 byte slot range.");
                    native = (byte)nativeValue;
                    if (!Live(manager.m_parks.m_buffer[native]))
                        throw new InvalidOperationException("Restored DistrictPark mapping targets an empty native slot.");
                }
                else
                {
                    if (context.IsAuthoritative) throw new InvalidOperationException("Host lost its DistrictPark identity mapping.");
                    if (!manager.CreatePark(out native, (DistrictPark.ParkType)value.ParkType, (DistrictPark.ParkLevel)value.ParkLevel) || native == 0)
                        throw new InvalidOperationException("CS1 could not allocate a replica DistrictPark slot.");
                    context.BindKnownIdentity(value.Identity, native);
                }
                InstallState(native, value);
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                if (wanted.ContainsKey(mappings[i].Identity.EntityId)) continue;
                if (mappings[i].NativeId == 0 || mappings[i].NativeId > byte.MaxValue)
                    throw new InvalidOperationException("DistrictPark mapping exceeds CS1 byte slot range.");
                byte native = (byte)mappings[i].NativeId;
                if (Live(manager.m_parks.m_buffer[native])) manager.ReleasePark(native);
                if (!context.RetireIdentity(mappings[i].Identity))
                    throw new InvalidOperationException("Replica DistrictPark identity retirement failed.");
            }
        }

        private static bool Live(DistrictPark park)
        {
            return (park.m_flags & DistrictPark.Flags.Created) != DistrictPark.Flags.None;
        }

        private static ParkState CaptureState(EntityIdentityV2 identity, DistrictPark park)
        {
            long[] scalars = new long[ScalarFields.Length];
            object boxed = park;
            for (int i = 0; i < ScalarFields.Length; i++)
                scalars[i] = ScalarFields[i] == null ? 0 : ToInt64(ScalarFields[i].GetValue(boxed));
            return new ParkState
            {
                Identity = identity,
                ParkType = (int)park.m_parkType,
                ParkLevel = (int)park.m_parkLevel,
                RandomSeed = park.m_randomSeed,
                Scalars = scalars
            };
        }

        private static void InstallState(byte native, ParkState state)
        {
            DistrictManager manager = DistrictManager.instance;
            DistrictPark current = manager.m_parks.m_buffer[native];
            if (!Live(current)) throw new InvalidOperationException("DistrictPark projection target is unavailable.");
            if ((int)current.m_parkType != state.ParkType || (int)current.m_parkLevel != state.ParkLevel)
                manager.SetParkTypeLevel(native, (DistrictPark.ParkType)state.ParkType, (DistrictPark.ParkLevel)state.ParkLevel);
            current = manager.m_parks.m_buffer[native];
            int oldCheerBonus = current.CalculateCheerleadingAttractivenessBonus();
            current.m_randomSeed = state.RandomSeed;
            object boxed = current;
            for (int i = 0; i < ScalarFields.Length; i++)
            {
                FieldInfo field = ScalarFields[i];
                if (field == null) continue;
                field.SetValue(boxed, FromInt64(field.FieldType, state.Scalars[i]));
            }
            current = (DistrictPark)boxed;
            int newCheerBonus = current.CalculateCheerleadingAttractivenessBonus();
            if (newCheerBonus != oldCheerBonus) current.ModifyStaticAttractiveness(newCheerBonus - oldCheerBonus);
            manager.m_parks.m_buffer[native] = current;
        }

        private static FieldInfo[] ResolveScalarFields()
        {
            FieldInfo[] result = new FieldInfo[ScalarFieldNames.Length];
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < result.Length; i++) result[i] = typeof(DistrictPark).GetField(ScalarFieldNames[i], flags);
            return result;
        }

        private static long ToInt64(object value)
        {
            if (value == null) return 0;
            Type type = value.GetType();
            if (type.IsEnum) return Convert.ToInt64(value);
            return Convert.ToInt64(value);
        }

        private static object FromInt64(Type type, long value)
        {
            if (type.IsEnum) return Enum.ToObject(type, value);
            if (type == typeof(byte)) return checked((byte)value);
            if (type == typeof(sbyte)) return checked((sbyte)value);
            if (type == typeof(short)) return checked((short)value);
            if (type == typeof(ushort)) return checked((ushort)value);
            if (type == typeof(int)) return checked((int)value);
            if (type == typeof(uint)) return checked((uint)value);
            if (type == typeof(long)) return value;
            if (type == typeof(ulong)) return checked((ulong)value);
            if (type == typeof(bool)) return value != 0;
            throw new InvalidOperationException("Unsupported DistrictPark scalar field type: " + type.FullName);
        }

        private static byte[] Encode(List<ParkState> states)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((ushort)states.Count);
                for (int i = 0; i < states.Count; i++)
                {
                    ParkState value = states[i];
                    writer.Write(value.Identity.EntityId);
                    writer.Write(value.Identity.Generation);
                    writer.Write(value.ParkType);
                    writer.Write(value.ParkLevel);
                    writer.Write(value.RandomSeed);
                    writer.Write((byte)value.Scalars.Length);
                    for (int j = 0; j < value.Scalars.Length; j++) writer.Write(value.Scalars[j]);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("DistrictPark absolute state exceeds one adapter frame.");
                return stream.ToArray();
            }
        }

        private static ParkState[] Decode(byte[] bytes)
        {
            if (bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("DistrictPark state frame is too large.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid DistrictPark state magic.");
                int count = reader.ReadUInt16();
                if (count > byte.MaxValue) throw new InvalidDataException("Too many DistrictPark entities.");
                ParkState[] result = new ParkState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("DistrictPark entities are not canonically ordered.");
                    previous = identity.EntityId;
                    ParkState value = new ParkState
                    {
                        Identity = identity,
                        ParkType = reader.ReadInt32(),
                        ParkLevel = reader.ReadInt32(),
                        RandomSeed = reader.ReadUInt64()
                    };
                    int scalarCount = reader.ReadByte();
                    if (scalarCount != ScalarFields.Length) throw new InvalidDataException("DistrictPark scalar schema mismatch.");
                    value.Scalars = new long[scalarCount];
                    for (int j = 0; j < scalarCount; j++) value.Scalars[j] = reader.ReadInt64();
                    result[i] = value;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing DistrictPark state bytes.");
                return result;
            }
        }

        private sealed class ParkState
        {
            public EntityIdentityV2 Identity;
            public int ParkType;
            public int ParkLevel;
            public ulong RandomSeed;
            public long[] Scalars;
        }
    }
}
