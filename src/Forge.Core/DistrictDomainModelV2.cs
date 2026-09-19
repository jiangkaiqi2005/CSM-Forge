using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public enum DistrictPaintTargetKindV2 : byte { Erase = 0, Existing = 1, CreateNew = 2 }

    public sealed class DistrictPaintIntentV2
    {
        public DistrictPaintTargetKindV2 TargetKind { get; private set; }
        public EntityIdentityV2 Target { get; private set; }
        public float BrushRadius { get; private set; }
        public float StartX { get; private set; }
        public float StartY { get; private set; }
        public float StartZ { get; private set; }
        public float EndX { get; private set; }
        public float EndY { get; private set; }
        public float EndZ { get; private set; }

        public DistrictPaintIntentV2(DistrictPaintTargetKindV2 targetKind, EntityIdentityV2 target,
            float brushRadius, float startX, float startY, float startZ, float endX, float endY, float endZ)
        {
            if (targetKind == DistrictPaintTargetKindV2.Existing && !target.IsValid)
                throw new ArgumentException("Existing district paint requires a stable identity.", "target");
            if (targetKind != DistrictPaintTargetKindV2.Existing && target.IsValid)
                throw new ArgumentException("Erase/new district paint must not carry an existing identity.", "target");
            if (brushRadius <= 0f || float.IsNaN(brushRadius) || float.IsInfinity(brushRadius))
                throw new ArgumentOutOfRangeException("brushRadius");
            CheckFinite(startX); CheckFinite(startY); CheckFinite(startZ);
            CheckFinite(endX); CheckFinite(endY); CheckFinite(endZ);
            TargetKind = targetKind; Target = target; BrushRadius = brushRadius;
            StartX = startX; StartY = startY; StartZ = startZ; EndX = endX; EndY = endY; EndZ = endZ;
        }

        private static void CheckFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("District brush coordinates must be finite.");
        }
    }

    public sealed class DistrictEntityStateV2
    {
        public EntityIdentityV2 Entity { get; private set; }
        public ulong RandomSeed { get; private set; }
        public ushort Style { get; private set; }
        public DistrictEntityStateV2(EntityIdentityV2 entity, ulong randomSeed, ushort style)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid district identity.", "entity");
            Entity = entity; RandomSeed = randomSeed; Style = style;
        }
    }

    public sealed class DistrictCellStateV2
    {
        public uint Index { get; private set; }
        public EntityIdentityV2 District1 { get; private set; }
        public byte Alpha1 { get; private set; }
        public EntityIdentityV2 District2 { get; private set; }
        public byte Alpha2 { get; private set; }
        public EntityIdentityV2 District3 { get; private set; }
        public byte Alpha3 { get; private set; }
        public EntityIdentityV2 District4 { get; private set; }
        public byte Alpha4 { get; private set; }
        public bool IsEmpty { get { return Alpha1 == 0 && Alpha2 == 0 && Alpha3 == 0 && Alpha4 == 0; } }

        public DistrictCellStateV2(uint index,
            EntityIdentityV2 district1, byte alpha1, EntityIdentityV2 district2, byte alpha2,
            EntityIdentityV2 district3, byte alpha3, EntityIdentityV2 district4, byte alpha4)
        {
            ValidateSlot(district1, alpha1); ValidateSlot(district2, alpha2);
            ValidateSlot(district3, alpha3); ValidateSlot(district4, alpha4);
            Index = index; District1 = district1; Alpha1 = alpha1; District2 = district2; Alpha2 = alpha2;
            District3 = district3; Alpha3 = alpha3; District4 = district4; Alpha4 = alpha4;
        }

        private static void ValidateSlot(EntityIdentityV2 district, byte alpha)
        {
            if (alpha == 0 && district.IsValid) throw new ArgumentException("Empty district cell slot must not carry an identity.");
        }

        public EntityIdentityV2 IdentityAt(int slot)
        {
            if (slot == 0) return District1; if (slot == 1) return District2;
            if (slot == 2) return District3; if (slot == 3) return District4;
            throw new ArgumentOutOfRangeException("slot");
        }
        public byte AlphaAt(int slot)
        {
            if (slot == 0) return Alpha1; if (slot == 1) return Alpha2;
            if (slot == 2) return Alpha3; if (slot == 3) return Alpha4;
            throw new ArgumentOutOfRangeException("slot");
        }
    }

    public sealed class DistrictMutationV2
    {
        public DistrictEntityStateV2[] UpsertEntities { get; private set; }
        public EntityIdentityV2[] DeleteEntities { get; private set; }
        public DistrictCellStateV2[] Cells { get; private set; }
        public int Count { get { return UpsertEntities.Length + DeleteEntities.Length + Cells.Length; } }

        public DistrictMutationV2(DistrictEntityStateV2[] upsertEntities, EntityIdentityV2[] deleteEntities,
            DistrictCellStateV2[] cells)
        {
            if (upsertEntities == null || deleteEntities == null || cells == null) throw new ArgumentNullException("district mutation arrays");
            if (upsertEntities.Length > 127 || deleteEntities.Length > 127 || cells.Length > 4096)
                throw new ArgumentException("District mutation exceeds supported bounds.");
            UpsertEntities = (DistrictEntityStateV2[])upsertEntities.Clone();
            DeleteEntities = (EntityIdentityV2[])deleteEntities.Clone();
            Cells = (DistrictCellStateV2[])cells.Clone();
            for (int i = 0; i < UpsertEntities.Length; i++) if (UpsertEntities[i] == null) throw new ArgumentException("Null district entity state.");
            for (int i = 0; i < DeleteEntities.Length; i++) if (!DeleteEntities[i].IsValid) throw new ArgumentException("Invalid district deletion identity.");
            for (int i = 0; i < Cells.Length; i++) if (Cells[i] == null) throw new ArgumentException("Null district cell state.");
        }
    }

    public sealed class DistrictStateIndexV2
    {
        private readonly SortedDictionary<ulong, DistrictEntityStateV2> entities = new SortedDictionary<ulong, DistrictEntityStateV2>();
        private readonly SortedDictionary<uint, DistrictCellStateV2> cells = new SortedDictionary<uint, DistrictCellStateV2>();
        public int EntityCount { get { return entities.Count; } }
        public int CellCount { get { return cells.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }

        public void SeedEntity(DistrictEntityStateV2 value) { UpsertEntity(value, false); }
        public void SeedCell(DistrictCellStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            ValidateReferences(value);
            if (value.IsEmpty) return;
            if (cells.ContainsKey(value.Index)) throw new InvalidOperationException("Duplicate district cell.");
            cells.Add(value.Index, value);
        }

        public void Apply(DistrictMutationV2 mutation)
        {
            if (mutation == null) throw new ArgumentNullException("mutation");
            for (int i = 0; i < mutation.UpsertEntities.Length; i++) UpsertEntity(mutation.UpsertEntities[i], true);
            for (int i = 0; i < mutation.Cells.Length; i++)
            {
                DistrictCellStateV2 cell = mutation.Cells[i]; ValidateReferences(cell);
                if (cell.IsEmpty) cells.Remove(cell.Index); else cells[cell.Index] = cell;
            }
            for (int i = 0; i < mutation.DeleteEntities.Length; i++)
            {
                EntityIdentityV2 id = mutation.DeleteEntities[i]; DistrictEntityStateV2 current;
                if (!entities.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id))
                    throw new InvalidOperationException("Cannot delete unknown or stale district entity.");
                foreach (DistrictCellStateV2 cell in cells.Values)
                    for (int slot = 0; slot < 4; slot++)
                        if (cell.AlphaAt(slot) != 0 && cell.IdentityAt(slot).Equals(id))
                            throw new InvalidOperationException("Cannot delete a district still referenced by the grid.");
                entities.Remove(id.EntityId);
            }
        }

        public bool TryGetEntity(EntityIdentityV2 id, out DistrictEntityStateV2 value)
        {
            value = null; DistrictEntityStateV2 current;
            if (!id.IsValid || !entities.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id)) return false;
            value = current; return true;
        }
        public bool TryGetCell(uint index, out DistrictCellStateV2 value) { return cells.TryGetValue(index, out value); }

        private void UpsertEntity(DistrictEntityStateV2 value, bool allowReplace)
        {
            if (value == null) throw new ArgumentNullException("value");
            DistrictEntityStateV2 current;
            if (entities.TryGetValue(value.Entity.EntityId, out current))
            {
                if (!current.Entity.Equals(value.Entity)) throw new InvalidOperationException("District generation conflict.");
                if (!allowReplace) throw new InvalidOperationException("District entity already exists.");
                entities[value.Entity.EntityId] = value; return;
            }
            entities.Add(value.Entity.EntityId, value);
        }

        private void ValidateReferences(DistrictCellStateV2 cell)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                if (cell.AlphaAt(slot) == 0) continue;
                EntityIdentityV2 id = cell.IdentityAt(slot); DistrictEntityStateV2 target;
                if (!id.IsValid) continue; // Token zero is CS1's unassigned/background district.
                if (!entities.TryGetValue(id.EntityId, out target) || !target.Entity.Equals(id))
                    throw new InvalidOperationException("District grid references an unknown entity.");
            }
        }

        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x32444746u); // FGD2
                writer.Write((ushort)entities.Count);
                foreach (DistrictEntityStateV2 entity in entities.Values)
                {
                    WriteIdentity(writer, entity.Entity); writer.Write(entity.RandomSeed); writer.Write(entity.Style);
                }
                writer.Write((uint)cells.Count);
                foreach (DistrictCellStateV2 cell in cells.Values)
                {
                    writer.Write(cell.Index);
                    for (int slot = 0; slot < 4; slot++)
                    {
                        writer.Write(cell.AlphaAt(slot)); WriteIdentity(writer, cell.IdentityAt(slot));
                    }
                }
                writer.Flush(); return stream.ToArray();
            }
        }

        internal static void WriteIdentity(BinaryWriter writer, EntityIdentityV2 value)
        {
            writer.Write(value.EntityId); writer.Write(value.Generation);
        }
    }

    public static class DistrictDomainCodecV2
    {
        private const uint MutationMagic = 0x324D4446u; // FDM2
        private const int IntentBytes = 41;

        public static byte[] EncodeIntent(DistrictPaintIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.TargetKind);
                DistrictStateIndexV2.WriteIdentity(writer, value.Target);
                writer.Write(value.BrushRadius); writer.Write(value.StartX); writer.Write(value.StartY); writer.Write(value.StartZ);
                writer.Write(value.EndX); writer.Write(value.EndY); writer.Write(value.EndZ);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static DistrictPaintIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length != IntentBytes) throw new InvalidDataException("Invalid district intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                DistrictPaintTargetKindV2 kind = (DistrictPaintTargetKindV2)reader.ReadByte();
                if (kind != DistrictPaintTargetKindV2.Erase && kind != DistrictPaintTargetKindV2.Existing && kind != DistrictPaintTargetKindV2.CreateNew)
                    throw new InvalidDataException("Unknown district paint target kind.");
                EntityIdentityV2 target = ReadOptionalIdentity(reader);
                return new DistrictPaintIntentV2(kind, target, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
        }

        public static byte[] EncodeMutation(DistrictMutationV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            List<EntityIdentityV2> identities = GatherIdentities(value);
            if (identities.Count > 255) throw new InvalidDataException("District mutation identity dictionary is too large.");
            Dictionary<ulong, byte> tokens = new Dictionary<ulong, byte>();
            for (int i = 0; i < identities.Count; i++) tokens.Add(identities[i].EntityId, (byte)(i + 1));
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(MutationMagic);
                writer.Write((byte)identities.Count); writer.Write((byte)value.UpsertEntities.Length);
                writer.Write((byte)value.DeleteEntities.Length); writer.Write((byte)0); writer.Write((ushort)value.Cells.Length);
                for (int i = 0; i < identities.Count; i++) DistrictStateIndexV2.WriteIdentity(writer, identities[i]);
                for (int i = 0; i < value.UpsertEntities.Length; i++)
                {
                    DistrictEntityStateV2 entity = value.UpsertEntities[i]; writer.Write(Token(tokens, entity.Entity));
                    writer.Write(entity.RandomSeed); writer.Write(entity.Style);
                }
                for (int i = 0; i < value.DeleteEntities.Length; i++) writer.Write(Token(tokens, value.DeleteEntities[i]));
                for (int i = 0; i < value.Cells.Length; i++)
                {
                    DistrictCellStateV2 cell = value.Cells[i]; writer.Write(cell.Index);
                    for (int slot = 0; slot < 4; slot++)
                    {
                        byte alpha = cell.AlphaAt(slot); writer.Write(alpha);
                        EntityIdentityV2 identity = cell.IdentityAt(slot);
                        writer.Write(alpha == 0 || !identity.IsValid ? (byte)0 : Token(tokens, identity));
                    }
                }
                writer.Flush(); byte[] result = stream.ToArray();
                if (result.Length > Limits.FramePayloadBytes) throw new InvalidDataException("District mutation exceeds frame payload budget.");
                return result;
            }
        }

        public static DistrictMutationV2 DecodeMutation(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 10 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid district mutation length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != MutationMagic) throw new InvalidDataException("Unknown district mutation magic.");
                byte identityCount = reader.ReadByte(); byte upsertCount = reader.ReadByte(); byte deleteCount = reader.ReadByte();
                if (reader.ReadByte() != 0) throw new InvalidDataException("District mutation reserved byte is nonzero.");
                ushort cellCount = reader.ReadUInt16(); if (upsertCount > 127 || deleteCount > 127 || cellCount > 4096)
                    throw new InvalidDataException("District mutation exceeds supported bounds.");
                EntityIdentityV2[] identities = new EntityIdentityV2[identityCount + 1];
                ulong previous = 0;
                for (int i = 1; i < identities.Length; i++)
                {
                    EntityIdentityV2 id = ReadRequiredIdentity(reader);
                    if (id.EntityId <= previous) throw new InvalidDataException("District identity dictionary is not canonical.");
                    previous = id.EntityId; identities[i] = id;
                }
                DistrictEntityStateV2[] upserts = new DistrictEntityStateV2[upsertCount];
                for (int i = 0; i < upserts.Length; i++)
                {
                    EntityIdentityV2 id = ResolveToken(reader.ReadByte(), identities);
                    upserts[i] = new DistrictEntityStateV2(id, reader.ReadUInt64(), reader.ReadUInt16());
                }
                EntityIdentityV2[] deletes = new EntityIdentityV2[deleteCount];
                for (int i = 0; i < deletes.Length; i++) deletes[i] = ResolveToken(reader.ReadByte(), identities);
                DistrictCellStateV2[] cells = new DistrictCellStateV2[cellCount];
                for (int i = 0; i < cells.Length; i++)
                {
                    uint index = reader.ReadUInt32(); EntityIdentityV2[] ids = new EntityIdentityV2[4]; byte[] alphas = new byte[4];
                    for (int slot = 0; slot < 4; slot++)
                    {
                        alphas[slot] = reader.ReadByte(); byte token = reader.ReadByte();
                        if (alphas[slot] == 0)
                        {
                            if (token != 0) throw new InvalidDataException("Empty district slot has a token.");
                        }
                        else if (token != 0) ids[slot] = ResolveToken(token, identities);
                    }
                    cells[i] = new DistrictCellStateV2(index, ids[0], alphas[0], ids[1], alphas[1], ids[2], alphas[2], ids[3], alphas[3]);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing district bytes.");
                return new DistrictMutationV2(upserts, deletes, cells);
            }
        }

        private static List<EntityIdentityV2> GatherIdentities(DistrictMutationV2 value)
        {
            Dictionary<ulong, EntityIdentityV2> unique = new Dictionary<ulong, EntityIdentityV2>();
            Action<EntityIdentityV2> add = delegate(EntityIdentityV2 id)
            {
                if (!id.IsValid) return; EntityIdentityV2 existing;
                if (unique.TryGetValue(id.EntityId, out existing) && !existing.Equals(id))
                    throw new InvalidDataException("District mutation mixes generations for one entity id.");
                unique[id.EntityId] = id;
            };
            for (int i = 0; i < value.UpsertEntities.Length; i++) add(value.UpsertEntities[i].Entity);
            for (int i = 0; i < value.DeleteEntities.Length; i++) add(value.DeleteEntities[i]);
            for (int i = 0; i < value.Cells.Length; i++)
                for (int slot = 0; slot < 4; slot++) if (value.Cells[i].AlphaAt(slot) != 0) add(value.Cells[i].IdentityAt(slot));
            List<EntityIdentityV2> result = new List<EntityIdentityV2>(unique.Values);
            result.Sort(delegate(EntityIdentityV2 a, EntityIdentityV2 b) { return a.EntityId.CompareTo(b.EntityId); });
            return result;
        }

        private static byte Token(Dictionary<ulong, byte> tokens, EntityIdentityV2 id)
        {
            byte token; if (!id.IsValid || !tokens.TryGetValue(id.EntityId, out token)) throw new InvalidDataException("District identity token missing.");
            return token;
        }
        private static EntityIdentityV2 ResolveToken(byte token, EntityIdentityV2[] identities)
        {
            if (token == 0 || token >= identities.Length) throw new InvalidDataException("Invalid district identity token.");
            return identities[token];
        }
        private static EntityIdentityV2 ReadRequiredIdentity(BinaryReader reader)
        {
            return new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
        }
        private static EntityIdentityV2 ReadOptionalIdentity(BinaryReader reader)
        {
            ulong entity = reader.ReadUInt64(); uint generation = reader.ReadUInt32();
            if (entity == 0 && generation == 0) return default(EntityIdentityV2);
            return new EntityIdentityV2(entity, generation);
        }
    }
}
