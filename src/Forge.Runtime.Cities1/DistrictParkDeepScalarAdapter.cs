using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Conservative deep value-state projection for DLCs sharing DistrictPark. It recursively
    /// captures value-type scalar leaves, but rejects any field path that looks like a native CS1
    /// entity reference. Bespoke adapters remain authoritative for identity-bearing state.
    /// </summary>
    internal sealed class DistrictParkDeepScalarAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31534450u; // PDS1
        private const string IdentityNamespace = "builtin.districtpark";
        private const int Shards = 16;
        private const int MaxDepth = 3;
        private static readonly ScalarPath[] Paths = BuildPaths();
        private static readonly uint SchemaFingerprint = ComputeSchemaFingerprint(Paths);

        private static readonly string[] ForbiddenTokens =
        {
            "id", "index", "building", "vehicle", "citizen", "instance", "node", "segment", "path", "line",
            "event", "gate", "target", "info", "prefab", "asset", "position", "location", "color", "policy",
            "randomseed", "flags", "parktype", "parklevel", "varsityidentity", "coachhiretimes", "granttype",
            "academicstaffcount", "cheerleadingbudget", "coachcount", "ticketprice", "dynamicvarsityattractivenessmodifier"
        };

        public string AdapterId { get { return "builtin.districtpark-deep"; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            if (context == null) throw new ArgumentNullException("context");
            ValidateShard(shardIndex);
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            if (context.IsAuthoritative) EnsureHostMappings(ids, manager);
            EntityMapEntryV2[] mappings = ids.SnapshotEntries();
            List<DeepState> values = new List<DeepState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex) continue;
                if (mapping.NativeId == 0 || mapping.NativeId > byte.MaxValue) continue;
                byte native = (byte)mapping.NativeId;
                if (!Live(native)) continue;
                object boxed = DistrictManager.instance.m_parks.m_buffer[native];
                long[] scalars = new long[Paths.Length];
                for (int p = 0; p < Paths.Length; p++) scalars[p] = ReadLeaf(boxed, Paths[p]);
                values.Add(new DeepState { Identity = mapping.Identity, Scalars = scalars });
            }
            values.Sort(delegate(DeepState a, DeepState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(shardIndex, values);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            ValidateShard(shardIndex);
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            DeepState[] values = Decode(shardIndex, state);
            for (int i = 0; i < values.Length; i++)
            {
                uint nativeValue;
                if (!ids.TryGetNative(values[i].Identity, out nativeValue) || nativeValue == 0 || nativeValue > byte.MaxValue)
                    throw new InvalidOperationException("Deep DistrictPark state references an unavailable stable identity.");
                byte native = (byte)nativeValue;
                if (!Live(native)) throw new InvalidOperationException("Deep DistrictPark projection target is not live.");
                object boxed = DistrictManager.instance.m_parks.m_buffer[native];
                for (int p = 0; p < Paths.Length; p++) boxed = WriteLeaf(boxed, Paths[p], values[i].Scalars[p]);
                DistrictManager.instance.m_parks.m_buffer[native] = (DistrictPark)boxed;
            }
        }

        internal static string[] SynchronizedFieldPaths
        {
            get
            {
                string[] result = new string[Paths.Length];
                for (int i = 0; i < Paths.Length; i++) result[i] = Paths[i].Name;
                return result;
            }
        }

        private static ScalarPath[] BuildPaths()
        {
            List<ScalarPath> result = new List<ScalarPath>();
            BuildPaths(typeof(DistrictPark), new List<FieldInfo>(), string.Empty, 0, result);
            result.Sort(delegate(ScalarPath a, ScalarPath b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            return result.ToArray();
        }

        private static void BuildPaths(Type type, List<FieldInfo> prefix, string name, int depth, List<ScalarPath> result)
        {
            if (depth > MaxDepth) return;
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsSpecialName ||
                    field.IsNotSerialized || UnsafeName(field.Name)) continue;
                string pathName = string.IsNullOrEmpty(name) ? field.Name : name + "." + field.Name;
                Type leaf = field.FieldType;
                List<FieldInfo> chain = new List<FieldInfo>(prefix); chain.Add(field);
                if (SupportedLeaf(leaf))
                {
                    result.Add(new ScalarPath(pathName, chain.ToArray(), leaf));
                    continue;
                }
                if (leaf.IsValueType && !leaf.IsEnum && !leaf.IsPrimitive && !leaf.IsArray && depth < MaxDepth)
                    BuildPaths(leaf, chain, pathName, depth + 1, result);
            }
        }

        private static bool UnsafeName(string name)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < ForbiddenTokens.Length; i++)
                if (value.IndexOf(ForbiddenTokens[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool SupportedLeaf(Type type)
        {
            if (type.IsEnum) return true;
            return type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
                type == typeof(char);
        }

        private static long ReadLeaf(object root, ScalarPath path)
        {
            object value = root;
            for (int i = 0; i < path.Chain.Length; i++) value = path.Chain[i].GetValue(value);
            Type type = path.LeafType;
            if (type.IsEnum) return Convert.ToInt64(value);
            if (type == typeof(bool)) return (bool)value ? 1L : 0L;
            if (type == typeof(float)) return BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0);
            if (type == typeof(double)) return BitConverter.ToInt64(BitConverter.GetBytes((double)value), 0);
            if (type == typeof(ulong)) return unchecked((long)(ulong)value);
            if (type == typeof(uint)) return (long)(uint)value;
            if (type == typeof(char)) return (char)value;
            return Convert.ToInt64(value);
        }

        private static object WriteLeaf(object root, ScalarPath path, long bits)
        {
            return WriteLeafRecursive(root, path.Chain, 0, ConvertLeaf(path.LeafType, bits));
        }

        private static object WriteLeafRecursive(object current, FieldInfo[] chain, int index, object value)
        {
            FieldInfo field = chain[index];
            if (index == chain.Length - 1)
            {
                field.SetValue(current, value);
                return current;
            }
            object child = field.GetValue(current);
            child = WriteLeafRecursive(child, chain, index + 1, value);
            field.SetValue(current, child);
            return current;
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
            throw new InvalidOperationException("Unsupported deep scalar leaf type: " + type.FullName);
        }

        private static byte[] Encode(int shardIndex, List<DeepState> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shardIndex); writer.Write(SchemaFingerprint);
                writer.Write((ushort)Paths.Length); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    DeepState value = values[i];
                    writer.Write(value.Identity.EntityId); writer.Write(value.Identity.Generation);
                    for (int p = 0; p < value.Scalars.Length; p++) writer.Write(value.Scalars[p]);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes)
                    throw new InvalidOperationException("Deep DistrictPark shard exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static DeepState[] Decode(int shardIndex, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid deep DistrictPark shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shardIndex || reader.ReadUInt32() != SchemaFingerprint)
                    throw new InvalidDataException("Deep DistrictPark shard schema mismatch.");
                int fieldCount = reader.ReadUInt16();
                if (fieldCount != Paths.Length) throw new InvalidDataException("Deep DistrictPark field-count mismatch.");
                int count = reader.ReadUInt16();
                DeepState[] result = new DeepState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("Deep DistrictPark state is not canonically ordered.");
                    previous = identity.EntityId;
                    long[] scalars = new long[fieldCount];
                    for (int p = 0; p < fieldCount; p++) scalars[p] = reader.ReadInt64();
                    result[i] = new DeepState { Identity = identity, Scalars = scalars };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing deep DistrictPark bytes.");
                return result;
            }
        }

        private static uint ComputeSchemaFingerprint(ScalarPath[] paths)
        {
            uint value = 2166136261u;
            for (int i = 0; i < paths.Length; i++)
            {
                string text = paths[i].Name + ":" + paths[i].LeafType.FullName + ";";
                for (int j = 0; j < text.Length; j++)
                {
                    value ^= text[j];
                    value *= 16777619u;
                }
            }
            return value;
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

        private static void ValidateShard(int shardIndex)
        {
            if (shardIndex < 0 || shardIndex >= Shards) throw new ArgumentOutOfRangeException("shardIndex");
        }

        private sealed class ScalarPath
        {
            public readonly string Name;
            public readonly FieldInfo[] Chain;
            public readonly Type LeafType;
            public ScalarPath(string name, FieldInfo[] chain, Type leafType)
            { Name = name; Chain = chain; LeafType = leafType; }
        }

        private sealed class DeepState
        {
            public EntityIdentityV2 Identity;
            public long[] Scalars;
        }
    }
}
