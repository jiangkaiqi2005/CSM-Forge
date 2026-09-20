using System;
using System.Collections.Generic;
using System.IO;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal enum ParkBrushIntentKind : byte
    {
        Erase = 0,
        Existing = 1,
        CreateNew = 2
    }

    /// <summary>
    /// Absolute 8-row shards of DistrictManager.m_parkGrid. Each payload has a local one-byte
    /// dictionary mapping cell slots to Forge EntityIdentityV2 values; native ParkId never crosses
    /// the wire. ParkType/Level are included so grid projection can materialize a missing replica
    /// slot before the richer builtin.districtpark metadata delta arrives.
    /// </summary>
    internal sealed class ParkGridStateAdapter : IForgeInteractiveShardedStateAdapterV1
    {
        private const uint Magic = 0x31475046u; // FPG1
        private const uint BrushMagic = 0x31425046u; // FPB1
        private const int RowsPerShard = 8;
        private const string ParkIdentityNamespace = "builtin.districtpark";

        /// <summary>
        /// Live park-grid width. Vanilla CS1 allocates 512x512, but grid-expanding mods replace
        /// the array (81 Tiles 2: 900x900 = 810,000), and the district analogue of this mistake
        /// aborted host creation outright. Width is derived from the array length on every
        /// access; shard count and per-shard extent follow from it.
        /// </summary>
        private static int ResolveResolution()
        {
            DistrictManager manager = DistrictManager.instance;
            if (manager == null || manager.m_parkGrid == null)
                throw new InvalidOperationException("CS1 park grid is unavailable or has an unexpected shape.");
            long length = manager.m_parkGrid.Length;
            int width = (int)Math.Sqrt(length);
            if (width < 1 || (long)width * width != length)
                throw new InvalidOperationException("CS1 park grid is unavailable or has an unexpected shape.");
            return width;
        }

        /// <summary>Rows in a shard, clamped so the tail shard never runs past the grid.</summary>
        private static int RowsInShard(int shardIndex, int resolution)
        {
            int remaining = resolution - shardIndex * RowsPerShard;
            return remaining >= RowsPerShard ? RowsPerShard : remaining;
        }

        public string AdapterId { get { return "builtin.parkgrid"; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return (ResolveResolution() + RowsPerShard - 1) / RowsPerShard; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            ValidateShard(shardIndex);
            DistrictManager manager = DistrictManager.instance;
            int resolution = ResolveResolution();
            if (manager.m_parkGrid.Length != resolution * resolution)
                throw new InvalidOperationException("CS1 park grid is unavailable or has an unexpected shape.");

            EntityIdMapV2 parkIds = ExtensionIdentityServices.Maps.GetOrAttach(ParkIdentityNamespace);
            SortedDictionary<ulong, ParkDescriptor> dictionary = new SortedDictionary<ulong, ParkDescriptor>();
            int start = shardIndex * RowsPerShard * resolution;
            int end = start + RowsInShard(shardIndex, resolution) * resolution;
            for (int index = start; index < end; index++)
            {
                DistrictManager.Cell cell = manager.m_parkGrid[index];
                Collect(cell.m_district1, cell.m_alpha1, context.IsAuthoritative, parkIds, dictionary);
                Collect(cell.m_district2, cell.m_alpha2, context.IsAuthoritative, parkIds, dictionary);
                Collect(cell.m_district3, cell.m_alpha3, context.IsAuthoritative, parkIds, dictionary);
                Collect(cell.m_district4, cell.m_alpha4, context.IsAuthoritative, parkIds, dictionary);
            }
            if (dictionary.Count > byte.MaxValue)
                throw new InvalidOperationException("A park-grid shard references more than 255 stable park entities.");

            Dictionary<ulong, byte> codeByEntity = new Dictionary<ulong, byte>();
            byte code = 1;
            foreach (KeyValuePair<ulong, ParkDescriptor> pair in dictionary)
                codeByEntity.Add(pair.Key, code++);

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((byte)shardIndex);
                writer.Write((byte)dictionary.Count);
                foreach (KeyValuePair<ulong, ParkDescriptor> pair in dictionary)
                {
                    ParkDescriptor value = pair.Value;
                    writer.Write(value.Identity.EntityId);
                    writer.Write(value.Identity.Generation);
                    writer.Write(value.ParkType);
                    writer.Write(value.ParkLevel);
                }
                for (int index = start; index < end; index++)
                {
                    DistrictManager.Cell cell = manager.m_parkGrid[index];
                    WriteSlot(writer, cell.m_district1, cell.m_alpha1, parkIds, codeByEntity);
                    WriteSlot(writer, cell.m_district2, cell.m_alpha2, parkIds, codeByEntity);
                    WriteSlot(writer, cell.m_district3, cell.m_alpha3, parkIds, codeByEntity);
                    WriteSlot(writer, cell.m_district4, cell.m_alpha4, parkIds, codeByEntity);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes)
                    throw new InvalidOperationException("Park-grid shard exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            Check.NotNull(context, "context"); Check.NotNull(state, "state"); // WP-2: per-argument reporting
            ValidateShard(shardIndex);
            if (state.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Park-grid shard is too large.");
            DistrictManager manager = DistrictManager.instance;
            int resolution = ResolveResolution();
            if (manager.m_parkGrid.Length != resolution * resolution)
                throw new InvalidOperationException("CS1 park grid is unavailable or has an unexpected shape.");
            EntityIdMapV2 parkIds = ExtensionIdentityServices.Maps.GetOrAttach(ParkIdentityNamespace);

            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shardIndex)
                    throw new InvalidDataException("Park-grid shard header mismatch.");
                int count = reader.ReadByte();
                byte[] nativeByCode = new byte[count + 1];
                ulong previous = 0;
                for (int i = 1; i <= count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("Park-grid dictionary is not canonically ordered.");
                    previous = identity.EntityId;
                    int parkType = reader.ReadInt32();
                    int parkLevel = reader.ReadInt32();
                    uint nativeValue;
                    byte native;
                    if (parkIds.TryGetNative(identity, out nativeValue))
                    {
                        if (nativeValue == 0 || nativeValue > byte.MaxValue || !ParkLive((byte)nativeValue))
                            throw new InvalidOperationException("Park-grid stable identity points to a missing replica slot.");
                        native = (byte)nativeValue;
                    }
                    else
                    {
                        if (context.IsAuthoritative)
                            throw new InvalidOperationException("Host park-grid lost a stable DistrictPark identity mapping.");
                        if (!manager.CreatePark(out native, (DistrictPark.ParkType)parkType, (DistrictPark.ParkLevel)parkLevel) || native == 0)
                            throw new InvalidOperationException("CS1 could not materialize a park-grid replica entity.");
                        parkIds.BindKnown(identity, native);
                    }
                    if ((int)manager.m_parks.m_buffer[native].m_parkType != parkType ||
                        (int)manager.m_parks.m_buffer[native].m_parkLevel != parkLevel)
                        manager.SetParkTypeLevel(native, (DistrictPark.ParkType)parkType, (DistrictPark.ParkLevel)parkLevel);
                    nativeByCode[i] = native;
                }

                int start = shardIndex * RowsPerShard * resolution;
                int end = start + RowsInShard(shardIndex, resolution) * resolution;
                for (int index = start; index < end; index++)
                {
                    DistrictManager.Cell cell = manager.m_parkGrid[index];
                    ReadSlot(reader, nativeByCode, out cell.m_district1, out cell.m_alpha1);
                    ReadSlot(reader, nativeByCode, out cell.m_district2, out cell.m_alpha2);
                    ReadSlot(reader, nativeByCode, out cell.m_district3, out cell.m_alpha3);
                    ReadSlot(reader, nativeByCode, out cell.m_district4, out cell.m_alpha4);
                    manager.m_parkGrid[index] = cell;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing park-grid shard bytes.");
                int minZ = shardIndex * RowsPerShard;
                int maxZ = minZ + RowsPerShard - 1;
                manager.AreaModified(0, minZ, resolution - 1, maxZ, false);
                manager.NamesModified();
            }
        }

        public bool ExecuteIntent(IForgeAdapterContextV1 context, byte[] intent)
        {
            if (context == null || !context.IsAuthoritative || intent == null) return false;
            ParkBrushIntent decoded;
            try { decoded = DecodeBrush(intent); }
            catch { return false; }
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) return false;
            EntityIdMapV2 parkIds = ExtensionIdentityServices.Maps.GetOrAttach(ParkIdentityNamespace);
            byte native = 0;
            EntityIdentityV2 allocated = default(EntityIdentityV2);
            bool created = false;
            if (decoded.Kind == ParkBrushIntentKind.Existing)
            {
                uint nativeValue;
                if (!parkIds.TryGetNative(decoded.Target, out nativeValue) || nativeValue == 0 || nativeValue > byte.MaxValue || !ParkLive((byte)nativeValue))
                    return false;
                native = (byte)nativeValue;
            }
            else if (decoded.Kind == ParkBrushIntentKind.CreateNew)
            {
                if (!manager.CreatePark(out native, (DistrictPark.ParkType)decoded.ParkType, (DistrictPark.ParkLevel)decoded.ParkLevel) || native == 0)
                    return false;
                allocated = parkIds.Allocate(native);
                created = true;
            }

            DistrictTool.ApplyBrush(DistrictTool.Layer.Parks, native, decoded.BrushRadius,
                decoded.Start, decoded.End, decoded.Force);
            manager.NamesModified();
            if (created && !GridReferences(native))
            {
                manager.ReleasePark(native);
                parkIds.Retire(allocated);
                return false;
            }
            return true;
        }

        internal static byte[] EncodeBrush(ParkBrushIntentKind kind, EntityIdentityV2 target,
            int parkType, int parkLevel, float brushRadius, Vector3 start, Vector3 end, bool force)
        {
            if (kind == ParkBrushIntentKind.Existing && !target.IsValid) throw new ArgumentException("Existing park brush target is missing.");
            if (kind != ParkBrushIntentKind.Existing && target.IsValid) throw new ArgumentException("Non-existing park brush target must not carry an identity.");
            ValidateBrush(brushRadius, start, end);
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(BrushMagic);
                writer.Write((byte)kind);
                writer.Write(target.IsValid ? target.EntityId : 0UL);
                writer.Write(target.IsValid ? target.Generation : 0U);
                writer.Write(parkType);
                writer.Write(parkLevel);
                writer.Write(brushRadius);
                writer.Write(start.x); writer.Write(start.y); writer.Write(start.z);
                writer.Write(end.x); writer.Write(end.y); writer.Write(end.z);
                writer.Write((byte)(force ? 1 : 0));
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static ParkBrushIntent DecodeBrush(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != BrushMagic) throw new InvalidDataException("Invalid park brush intent magic.");
                ParkBrushIntentKind kind = (ParkBrushIntentKind)reader.ReadByte();
                if (kind < ParkBrushIntentKind.Erase || kind > ParkBrushIntentKind.CreateNew)
                    throw new InvalidDataException("Invalid park brush intent kind.");
                ulong entity = reader.ReadUInt64();
                uint generation = reader.ReadUInt32();
                EntityIdentityV2 target = entity == 0 && generation == 0 ? default(EntityIdentityV2) : new EntityIdentityV2(entity, generation);
                int parkType = reader.ReadInt32();
                int parkLevel = reader.ReadInt32();
                float radius = reader.ReadSingle();
                Vector3 start = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 end = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                byte force = reader.ReadByte();
                if (force > 1 || stream.Position != stream.Length) throw new InvalidDataException("Invalid park brush intent trailer.");
                if (kind == ParkBrushIntentKind.Existing && !target.IsValid) throw new InvalidDataException("Existing park brush target is missing.");
                if (kind != ParkBrushIntentKind.Existing && target.IsValid) throw new InvalidDataException("Park brush target kind is inconsistent.");
                ValidateBrush(radius, start, end);
                return new ParkBrushIntent { Kind = kind, Target = target, ParkType = parkType, ParkLevel = parkLevel,
                    BrushRadius = radius, Start = start, End = end, Force = force == 1 };
            }
        }

        private static void ValidateBrush(float radius, Vector3 start, Vector3 end)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f || radius > 2000f ||
                !Finite(start.x) || !Finite(start.y) || !Finite(start.z) || !Finite(end.x) || !Finite(end.y) || !Finite(end.z))
                throw new ArgumentException("Invalid park brush geometry.");
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private static void Collect(byte native, byte alpha, bool authoritative, EntityIdMapV2 parkIds,
            SortedDictionary<ulong, ParkDescriptor> dictionary)
        {
            if (alpha == 0) return;
            if (native == 0) return; // CS1 unassigned/background park area.
            if (!ParkLive(native)) throw new InvalidOperationException("Non-empty park-grid cell references an unavailable park.");
            EntityIdentityV2 identity;
            if (!parkIds.TryGetIdentity(native, out identity))
            {
                if (!authoritative) throw new InvalidOperationException("Replica park-grid references an unmapped park identity.");
                identity = parkIds.Allocate(native);
            }
            if (dictionary.ContainsKey(identity.EntityId)) return;
            DistrictPark park = DistrictManager.instance.m_parks.m_buffer[native];
            dictionary.Add(identity.EntityId, new ParkDescriptor
            {
                Identity = identity,
                ParkType = (int)park.m_parkType,
                ParkLevel = (int)park.m_parkLevel
            });
        }

        private static void WriteSlot(BinaryWriter writer, byte native, byte alpha, EntityIdMapV2 parkIds,
            Dictionary<ulong, byte> codeByEntity)
        {
            if (alpha == 0) { writer.Write((byte)0); writer.Write((byte)0); return; }
            if (native == 0) { writer.Write((byte)0); writer.Write(alpha); return; }
            EntityIdentityV2 identity;
            byte code;
            if (!parkIds.TryGetIdentity(native, out identity) || !codeByEntity.TryGetValue(identity.EntityId, out code))
                throw new InvalidOperationException("Park-grid slot is not represented in the shard dictionary.");
            writer.Write(code); writer.Write(alpha);
        }

        private static void ReadSlot(BinaryReader reader, byte[] nativeByCode, out byte native, out byte alpha)
        {
            byte code = reader.ReadByte(); alpha = reader.ReadByte();
            if (alpha == 0)
            {
                if (code != 0) throw new InvalidDataException("Empty park-grid slot has a non-zero dictionary code.");
                native = 0; return;
            }
            if (code == 0) { native = 0; return; }
            if (code >= nativeByCode.Length || nativeByCode[code] == 0)
                throw new InvalidDataException("Park-grid slot references an invalid dictionary code.");
            native = nativeByCode[code];
        }

        private static bool ParkLive(byte native)
        {
            return native != 0 && DistrictManager.instance != null &&
                (DistrictManager.instance.m_parks.m_buffer[native].m_flags & DistrictPark.Flags.Created) != DistrictPark.Flags.None;
        }

        private static bool GridReferences(byte native)
        {
            DistrictManager manager = DistrictManager.instance;
            for (int i = 0; i < manager.m_parkGrid.Length; i++)
            {
                DistrictManager.Cell cell = manager.m_parkGrid[i];
                if ((cell.m_alpha1 != 0 && cell.m_district1 == native) ||
                    (cell.m_alpha2 != 0 && cell.m_district2 == native) ||
                    (cell.m_alpha3 != 0 && cell.m_district3 == native) ||
                    (cell.m_alpha4 != 0 && cell.m_district4 == native)) return true;
            }
            return false;
        }

        private static void ValidateShard(int shardIndex)
        {
            int shardCount = (ResolveResolution() + RowsPerShard - 1) / RowsPerShard;
            Check.OutOfRange(shardIndex < 0 || shardIndex >= shardCount, "shardIndex");
        }

        private sealed class ParkDescriptor
        {
            public EntityIdentityV2 Identity;
            public int ParkType;
            public int ParkLevel;
        }

        private sealed class ParkBrushIntent
        {
            public ParkBrushIntentKind Kind;
            public EntityIdentityV2 Target;
            public int ParkType;
            public int ParkLevel;
            public float BrushRadius;
            public Vector3 Start;
            public Vector3 End;
            public bool Force;
        }
    }
}
