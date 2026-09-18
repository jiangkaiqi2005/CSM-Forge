using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Host-owned Building AI scalar results. Structural identity and placement stay in the
    /// Building domain; native CS1 references are deliberately excluded from this payload.
    /// </summary>
    internal sealed class BuildingSimulationStateAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31534246u; // FBS1
        private const int Shards = 128;
        internal const string Adapter = "builtin.building-simulation";
        private static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_accessSegment", "m_angle", "m_buildIndex", "m_citizenUnits", "m_eventAccessSegment",
            "m_eventIndex", "m_eventRouteIndex", "m_guestVehicles", "m_infoIndex", "m_length",
            "m_netNode", "m_nextGridBuilding", "m_nextGridBuilding2", "m_ownVehicles", "m_parentBuilding",
            "m_park", "m_position", "m_raceTeamIndex", "m_sourceCitizens", "m_subBuilding",
            "m_targetCitizens", "m_waterSource", "m_width"
        };
        private static readonly ScalarPath[] Paths = BuildPaths();
        private static readonly uint SchemaFingerprint = ComputeSchemaFingerprint(Paths);

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            if (context == null) throw new ArgumentNullException("context");
            ValidateShard(shardIndex);
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            bool hostSide = context.IsAuthoritative;
            List<State> values = new List<State>();
            EntityMapEntryV2[] mappings = RuntimeServices.Multiplayer.SnapshotBuildingMappings(hostSide);
            for (int i = 0; i < mappings.Length; i++)
            {
                if ((int)((mappings[i].Identity.EntityId - 1UL) % Shards) != shardIndex || mappings[i].NativeId == 0 || mappings[i].NativeId > ushort.MaxValue) continue;
                ushort native = (ushort)mappings[i].NativeId; Building data = manager.m_buildings.m_buffer[native];
                if (data.m_flags == Building.Flags.None) continue;
                object boxed = data; long[] scalars = new long[Paths.Length];
                for (int p = 0; p < Paths.Length; p++) scalars[p] = ReadLeaf(boxed, Paths[p]);
                values.Add(new State { Identity = mappings[i].Identity, Scalars = scalars });
            }
            values.Sort(delegate(State a, State b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(shardIndex, values);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            ValidateShard(shardIndex); State[] values = Decode(shardIndex, state);
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            for (int i = 0; i < values.Length; i++)
            {
                uint nativeValue;
                if (!RuntimeServices.Multiplayer.TryResolveStableNameNative(StableNameTargetKindV2.Building, values[i].Identity, false, out nativeValue) ||
                    nativeValue == 0 || nativeValue > ushort.MaxValue)
                    throw new InvalidOperationException("Building simulation state references an unknown stable Building identity.");
                ushort native = (ushort)nativeValue; Building data = manager.m_buildings.m_buffer[native];
                if (data.m_flags == Building.Flags.None) throw new InvalidOperationException("Building simulation projection target is not live.");
                object boxed = data;
                for (int p = 0; p < Paths.Length; p++) boxed = WriteLeaf(boxed, Paths[p], values[i].Scalars[p]);
                manager.m_buildings.m_buffer[native] = (Building)boxed;
            }
        }

        internal static string[] SynchronizedFieldPaths
        {
            get { string[] result = new string[Paths.Length]; for (int i = 0; i < Paths.Length; i++) result[i] = Paths[i].Name; return result; }
        }

        private static ScalarPath[] BuildPaths()
        {
            List<ScalarPath> result = new List<ScalarPath>();
            FieldInfo[] fields = typeof(Building).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic || Excluded.Contains(field.Name)) continue;
                if (SupportedLeaf(field.FieldType)) result.Add(new ScalarPath(field.Name, new[] { field }, field.FieldType));
                else if (field.FieldType == typeof(Building.Frame) || field.FieldType == typeof(Notification.ProblemStruct))
                    AddNested(field, result);
            }
            return result.ToArray();
        }

        private static void AddNested(FieldInfo parent, List<ScalarPath> result)
        {
            FieldInfo[] fields = parent.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            for (int i = 0; i < fields.Length; i++)
                if (!fields[i].IsStatic && SupportedLeaf(fields[i].FieldType))
                    result.Add(new ScalarPath(parent.Name + "." + fields[i].Name, new[] { parent, fields[i] }, fields[i].FieldType));
        }

        private static bool SupportedLeaf(Type type)
        {
            return type.IsEnum || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(char);
        }

        private static long ReadLeaf(object root, ScalarPath path)
        {
            object value = root; for (int i = 0; i < path.Chain.Length; i++) value = path.Chain[i].GetValue(value);
            Type type = path.LeafType;
            if (type.IsEnum)
            {
                Type underlying = Enum.GetUnderlyingType(type);
                return underlying == typeof(ulong) ? unchecked((long)Convert.ToUInt64(value)) : Convert.ToInt64(value);
            }
            if (type == typeof(bool)) return (bool)value ? 1L : 0L;
            if (type == typeof(float)) return BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0);
            if (type == typeof(double)) return BitConverter.ToInt64(BitConverter.GetBytes((double)value), 0);
            if (type == typeof(ulong)) return unchecked((long)(ulong)value);
            if (type == typeof(uint)) return (long)(uint)value;
            if (type == typeof(char)) return (char)value;
            return Convert.ToInt64(value);
        }

        private static object WriteLeaf(object root, ScalarPath path, long bits)
        { return WriteLeafRecursive(root, path.Chain, 0, ConvertLeaf(path.LeafType, bits)); }

        private static object WriteLeafRecursive(object current, FieldInfo[] chain, int index, object value)
        {
            FieldInfo field = chain[index];
            if (index == chain.Length - 1) { field.SetValue(current, value); return current; }
            object child = field.GetValue(current); child = WriteLeafRecursive(child, chain, index + 1, value); field.SetValue(current, child); return current;
        }

        private static object ConvertLeaf(Type type, long bits)
        {
            if (type.IsEnum) return Enum.ToObject(type, bits);
            if (type == typeof(bool)) return bits != 0;
            if (type == typeof(byte)) return checked((byte)bits);
            if (type == typeof(sbyte)) return checked((sbyte)bits);
            if (type == typeof(short)) return checked((short)bits);
            if (type == typeof(ushort)) return checked((ushort)bits);
            if (type == typeof(int)) return checked((int)bits);
            if (type == typeof(uint)) return checked((uint)bits);
            if (type == typeof(long)) return bits;
            if (type == typeof(ulong)) return unchecked((ulong)bits);
            if (type == typeof(float)) return BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)bits)), 0);
            if (type == typeof(double)) return BitConverter.ToDouble(BitConverter.GetBytes(bits), 0);
            if (type == typeof(char)) return checked((char)bits);
            throw new InvalidOperationException("Unsupported Building scalar leaf type: " + type.FullName);
        }

        private static byte[] Encode(int shardIndex, List<State> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shardIndex); writer.Write(SchemaFingerprint); writer.Write((ushort)Paths.Length); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    writer.Write(values[i].Identity.EntityId); writer.Write(values[i].Identity.Generation);
                    for (int p = 0; p < values[i].Scalars.Length; p++) writer.Write(values[i].Scalars[p]);
                }
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Building simulation shard exceeds one Forge frame."); return stream.ToArray();
            }
        }

        private static State[] Decode(int shardIndex, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid Building simulation shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shardIndex || reader.ReadUInt32() != SchemaFingerprint)
                    throw new InvalidDataException("Building simulation shard schema mismatch.");
                int fields = reader.ReadUInt16(); if (fields != Paths.Length) throw new InvalidDataException("Building simulation field-count mismatch.");
                int count = reader.ReadUInt16(); State[] result = new State[count]; ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (!identity.IsValid || identity.EntityId <= previous || (int)((identity.EntityId - 1UL) % Shards) != shardIndex)
                        throw new InvalidDataException("Building simulation state is not canonical for this shard.");
                    previous = identity.EntityId; long[] scalars = new long[fields]; for (int p = 0; p < fields; p++) scalars[p] = reader.ReadInt64();
                    result[i] = new State { Identity = identity, Scalars = scalars };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Building simulation bytes."); return result;
            }
        }

        private static uint ComputeSchemaFingerprint(ScalarPath[] paths)
        {
            uint value = 2166136261u;
            for (int i = 0; i < paths.Length; i++)
            {
                string text = paths[i].Name + ":" + paths[i].LeafType.FullName + ";";
                for (int j = 0; j < text.Length; j++) { value ^= text[j]; value *= 16777619u; }
            }
            return value;
        }

        private static void ValidateShard(int shardIndex) { if (shardIndex < 0 || shardIndex >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }
        private sealed class ScalarPath
        {
            public readonly string Name; public readonly FieldInfo[] Chain; public readonly Type LeafType;
            public ScalarPath(string name, FieldInfo[] chain, Type leafType) { Name = name; Chain = chain; LeafType = leafType; }
        }
        private sealed class State { public EntityIdentityV2 Identity; public long[] Scalars; }
    }
}
