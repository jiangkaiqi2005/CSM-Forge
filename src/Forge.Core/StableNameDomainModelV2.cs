using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public enum StableNameTargetKindV2 : byte
    {
        Building = 1,
        NetSegment = 2,
        District = 3,
        TransportLine = 4
    }

    public struct StableNameKeyV2 : IEquatable<StableNameKeyV2>, IComparable<StableNameKeyV2>
    {
        public readonly StableNameTargetKindV2 Kind;
        public readonly EntityIdentityV2 Entity;
        public StableNameKeyV2(StableNameTargetKindV2 kind, EntityIdentityV2 entity)
        {
            if (kind < StableNameTargetKindV2.Building || kind > StableNameTargetKindV2.TransportLine)
                throw new ArgumentOutOfRangeException("kind");
            if (!entity.IsValid) throw new ArgumentException("Invalid name entity identity.", "entity");
            Kind = kind; Entity = entity;
        }
        public bool Equals(StableNameKeyV2 other) { return Kind == other.Kind && Entity.Equals(other.Entity); }
        public override bool Equals(object obj) { return obj is StableNameKeyV2 && Equals((StableNameKeyV2)obj); }
        public override int GetHashCode() { return ((int)Kind * 397) ^ Entity.GetHashCode(); }
        public int CompareTo(StableNameKeyV2 other)
        {
            int value = ((byte)Kind).CompareTo((byte)other.Kind); if (value != 0) return value;
            value = Entity.EntityId.CompareTo(other.Entity.EntityId); if (value != 0) return value;
            return Entity.Generation.CompareTo(other.Entity.Generation);
        }
    }

    public sealed class StableNameStateV2
    {
        public StableNameKeyV2 Key { get; private set; }
        public string Name { get; private set; }
        public StableNameStateV2(StableNameKeyV2 key, string name)
        {
            if (name == null) name = string.Empty;
            if (Encoding.UTF8.GetByteCount(name) > 512) throw new ArgumentException("Custom name exceeds Forge wire bounds.", "name");
            Key = key; Name = name;
        }
    }

    public sealed class StableNameStateIndexV2
    {
        private readonly SortedDictionary<StableNameKeyV2, string> values = new SortedDictionary<StableNameKeyV2, string>();
        public int Count { get { return values.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }
        public void Set(StableNameStateV2 value)
        {
            Check.NotNull(value, "value");
            if (string.IsNullOrEmpty(value.Name)) values.Remove(value.Key); else values[value.Key] = value.Name;
        }
        public void Remove(StableNameKeyV2 key) { values.Remove(key); }
        public bool TryGet(StableNameKeyV2 key, out string name) { return values.TryGetValue(key, out name); }
        public StableNameKeyV2[] Keys()
        {
            StableNameKeyV2[] result = new StableNameKeyV2[values.Count]; int i = 0;
            foreach (StableNameKeyV2 key in values.Keys) result[i++] = key;
            return result;
        }
        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(0x324D4E46u); writer.Write((uint)values.Count); // FNM2
                foreach (KeyValuePair<StableNameKeyV2, string> pair in values)
                {
                    writer.Write((byte)pair.Key.Kind); writer.Write(pair.Key.Entity.EntityId); writer.Write(pair.Key.Entity.Generation);
                    byte[] text = Encoding.UTF8.GetBytes(pair.Value); writer.Write((ushort)text.Length); writer.Write(text);
                }
                writer.Flush(); return stream.ToArray();
            }
        }
    }

    public static class StableNameCodecV2
    {
        private const uint Magic = 0x324E5346u; // FSN2
        public static byte[] Encode(StableNameStateV2 value)
        {
            Check.NotNull(value, "value");
            byte[] text = Encoding.UTF8.GetBytes(value.Name ?? string.Empty);
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(Magic); writer.Write((byte)value.Key.Kind);
                writer.Write(value.Key.Entity.EntityId); writer.Write(value.Key.Entity.Generation); writer.Write((ushort)text.Length); writer.Write(text);
                writer.Flush(); return stream.ToArray();
            }
        }
        public static StableNameStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 19 || bytes.Length > 531) throw new InvalidDataException("Invalid stable name payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unknown stable name payload magic.");
                StableNameTargetKindV2 kind = (StableNameTargetKindV2)reader.ReadByte();
                EntityIdentityV2 id = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                ushort length = reader.ReadUInt16(); byte[] text = reader.ReadBytes(length);
                if (text.Length != length || reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Truncated or trailing stable name payload.");
                return new StableNameStateV2(new StableNameKeyV2(kind, id), Encoding.UTF8.GetString(text));
            }
        }
    }
}
