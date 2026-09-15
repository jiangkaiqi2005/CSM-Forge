using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public enum BuildingIntentKindV2 : byte { Create = 1, Delete = 2 }
    public enum BuildingResultKindV2 : byte { Created = 1, Deleted = 2 }

    public sealed class BuildingIntentV2
    {
        public BuildingIntentKindV2 Kind { get; private set; }
        public EntityIdentityV2 Entity { get; private set; }
        public string PrefabKey { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float Angle { get; private set; }
        public byte Length { get; private set; }
        public int ConstructionCost { get; private set; }
        private BuildingIntentV2() { }
        public static BuildingIntentV2 Create(string prefabKey, float x, float y, float z, float angle, byte length) { return Create(prefabKey, x, y, z, angle, length, 0); }
        public static BuildingIntentV2 Create(string prefabKey, float x, float y, float z, float angle, byte length, int constructionCost)
        {
            ValidatePrefab(prefabKey); ValidateFloat(x); ValidateFloat(y); ValidateFloat(z); ValidateFloat(angle);
            if (length == 0) throw new ArgumentOutOfRangeException("length");
            if (constructionCost < 0 || constructionCost > 1000000000) throw new ArgumentOutOfRangeException("constructionCost");
            return new BuildingIntentV2 { Kind = BuildingIntentKindV2.Create, PrefabKey = prefabKey, X = x, Y = y, Z = z,
                Angle = angle, Length = length, ConstructionCost = constructionCost };
        }
        public static BuildingIntentV2 Delete(EntityIdentityV2 entity)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid building entity.", "entity");
            return new BuildingIntentV2 { Kind = BuildingIntentKindV2.Delete, Entity = entity };
        }
        internal static void ValidatePrefab(string value)
        {
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 192) throw new ArgumentException("Invalid building prefab identity.", "value");
        }
        internal static void ValidateFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Building coordinates must be finite.");
        }
    }

    public sealed class BuildingStateV2
    {
        public EntityIdentityV2 Entity { get; private set; }
        public string PrefabKey { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float Angle { get; private set; }
        public byte Length { get; private set; }
        public uint BuildIndex { get; private set; }
        public int ConstructionCost { get; private set; }
        public BuildingStateV2(EntityIdentityV2 entity, string prefabKey, float x, float y, float z, float angle, byte length, uint buildIndex)
            : this(entity, prefabKey, x, y, z, angle, length, buildIndex, 0) { }
        public BuildingStateV2(EntityIdentityV2 entity, string prefabKey, float x, float y, float z, float angle, byte length, uint buildIndex, int constructionCost)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid building entity.", "entity");
            BuildingIntentV2.ValidatePrefab(prefabKey); BuildingIntentV2.ValidateFloat(x); BuildingIntentV2.ValidateFloat(y);
            BuildingIntentV2.ValidateFloat(z); BuildingIntentV2.ValidateFloat(angle);
            if (length == 0) throw new ArgumentOutOfRangeException("length");
            if (constructionCost < 0 || constructionCost > 1000000000) throw new ArgumentOutOfRangeException("constructionCost");
            Entity = entity; PrefabKey = prefabKey; X = x; Y = y; Z = z; Angle = angle; Length = length;
            BuildIndex = buildIndex; ConstructionCost = constructionCost;
        }
    }

    public sealed class BuildingResultV2
    {
        public BuildingResultKindV2 Kind { get; private set; }
        public EntityIdentityV2 Entity { get; private set; }
        public BuildingStateV2 State { get; private set; }
        public int RefundAmount { get; private set; }
        private BuildingResultV2() { }
        public static BuildingResultV2 Created(BuildingStateV2 state)
        {
            if (state == null) throw new ArgumentNullException("state");
            return new BuildingResultV2 { Kind = BuildingResultKindV2.Created, Entity = state.Entity, State = state };
        }
        public static BuildingResultV2 Deleted(EntityIdentityV2 entity) { return Deleted(entity, 0); }
        public static BuildingResultV2 Deleted(EntityIdentityV2 entity, int refundAmount)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid building entity.", "entity");
            if (refundAmount < 0 || refundAmount > 1000000000) throw new ArgumentOutOfRangeException("refundAmount");
            return new BuildingResultV2 { Kind = BuildingResultKindV2.Deleted, Entity = entity, RefundAmount = refundAmount };
        }
    }

    public sealed class BuildingStateIndexV2
    {
        private readonly SortedDictionary<ulong, BuildingStateV2> states = new SortedDictionary<ulong, BuildingStateV2>();
        public int Count { get { return states.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }
        public void Seed(BuildingStateV2 state)
        {
            if (state == null) throw new ArgumentNullException("state"); BuildingStateV2 current;
            if (states.TryGetValue(state.Entity.EntityId, out current))
            { if (!current.Entity.Equals(state.Entity)) throw new InvalidOperationException("Entity generation conflict."); throw new InvalidOperationException("Building entity already exists."); }
            states.Add(state.Entity.EntityId, state);
        }
        public void Apply(BuildingResultV2 result)
        {
            if (result == null) throw new ArgumentNullException("result"); if (result.Kind == BuildingResultKindV2.Created) { Seed(result.State); return; }
            BuildingStateV2 current; if (!states.TryGetValue(result.Entity.EntityId, out current) || !current.Entity.Equals(result.Entity))
                throw new InvalidOperationException("Cannot delete an unknown or stale building entity."); states.Remove(result.Entity.EntityId);
        }
        public bool TryGet(EntityIdentityV2 entity, out BuildingStateV2 state)
        {
            state = null; if (!entity.IsValid) return false; BuildingStateV2 value;
            if (!states.TryGetValue(entity.EntityId, out value) || !value.Entity.Equals(entity)) return false; state = value; return true;
        }
        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(0x32424746u); writer.Write((uint)states.Count);
                foreach (BuildingStateV2 state in states.Values)
                { writer.Write(state.Entity.EntityId); writer.Write(state.Entity.Generation); WriteString(writer, state.PrefabKey);
                  writer.Write(state.X); writer.Write(state.Y); writer.Write(state.Z); writer.Write(state.Angle); writer.Write(state.Length); }
                writer.Flush(); return stream.ToArray();
            }
        }
        private static void WriteString(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    }

    public static class BuildingDomainCodecV2
    {
        public static byte[] EncodeIntent(BuildingIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value"); using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.Kind);
                if (value.Kind == BuildingIntentKindV2.Create)
                { WriteString(writer, value.PrefabKey); writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); writer.Write(value.Angle); writer.Write(value.Length); writer.Write(value.ConstructionCost); }
                else { writer.Write(value.Entity.EntityId); writer.Write(value.Entity.Generation); }
                writer.Flush(); return stream.ToArray();
            }
        }
        public static BuildingIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 260) throw new InvalidDataException("Invalid building intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                BuildingIntentKindV2 kind = (BuildingIntentKindV2)reader.ReadByte(); BuildingIntentV2 result;
                if (kind == BuildingIntentKindV2.Create) result = BuildingIntentV2.Create(ReadString(reader), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadInt32());
                else if (kind == BuildingIntentKindV2.Delete) result = BuildingIntentV2.Delete(new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()));
                else throw new InvalidDataException("Unknown building intent kind."); EnsureEnd(reader); return result;
            }
        }
        public static byte[] EncodeResult(BuildingResultV2 value)
        {
            if (value == null) throw new ArgumentNullException("value"); using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.Kind); writer.Write(value.Entity.EntityId); writer.Write(value.Entity.Generation);
                if (value.Kind == BuildingResultKindV2.Created)
                { WriteString(writer, value.State.PrefabKey); writer.Write(value.State.X); writer.Write(value.State.Y); writer.Write(value.State.Z); writer.Write(value.State.Angle); writer.Write(value.State.Length); writer.Write(value.State.BuildIndex); writer.Write(value.State.ConstructionCost); }
                else writer.Write(value.RefundAmount);
                writer.Flush(); return stream.ToArray();
            }
        }
        public static BuildingResultV2 DecodeResult(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 17 || bytes.Length > 264) throw new InvalidDataException("Invalid building result length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                BuildingResultKindV2 kind = (BuildingResultKindV2)reader.ReadByte(); EntityIdentityV2 entity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()); BuildingResultV2 result;
                if (kind == BuildingResultKindV2.Created)
                { string prefab = ReadString(reader); result = BuildingResultV2.Created(new BuildingStateV2(entity, prefab, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadInt32())); }
                else if (kind == BuildingResultKindV2.Deleted) result = BuildingResultV2.Deleted(entity, reader.ReadInt32());
                else throw new InvalidDataException("Unknown building result kind."); EnsureEnd(reader); return result;
            }
        }
        private static void WriteString(BinaryWriter writer, string value)
        { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length > 192) throw new InvalidDataException("Building prefab identity is too long."); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadString(BinaryReader reader)
        { ushort length = reader.ReadUInt16(); if (length == 0 || length > 192) throw new InvalidDataException("Invalid building prefab identity length."); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
        private static void EnsureEnd(BinaryReader reader) { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing building bytes."); }
    }
}
