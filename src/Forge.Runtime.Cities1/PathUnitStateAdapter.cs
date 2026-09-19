using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Math;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Host path results keyed by Forge identities and Stable Net segment identities.</summary>
    internal sealed class PathUnitStateAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31555046u; // FPU1
        private const int Shards = 256;
        internal const string Adapter = "builtin.path-results";
        private static PathUnitStateAdapter current;
        private readonly Dictionary<EntityIdentityV2, State> replicaStates = new Dictionary<EntityIdentityV2, State>();
        private readonly byte[][] replicaShards = new byte[Shards][];
        private readonly StableMappingShardIndex mappingShards = new StableMappingShardIndex(Shards);
        private int capturesUntilReconcile;

        public PathUnitStateAdapter() { current = this; }

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        internal static void ObserveHostCreated(uint native)
        {
            if (native == 0 || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityIdentityV2 ignored;
            if (!ids.TryGetIdentity(native, out ignored)) ids.Allocate(native);
            if (current != null) current.mappingShards.Invalidate();
        }

        internal static EntityIdentityV2 ResolveHostIdentity(uint native)
        {
            if (PathManager.instance == null || !Live(PathManager.instance, native)) throw new InvalidOperationException("Stable Path target is not live.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityIdentityV2 identity;
            return ids.TryGetIdentity(native, out identity) ? identity : ids.Allocate(native);
        }

        internal static EntityIdentityV2[] PrepareHostRelease(uint first, bool chain)
        {
            List<EntityIdentityV2> result = new List<EntityIdentityV2>(); EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter);
            PathManager manager = PathManager.instance; uint current = first; int guard = 0;
            while (manager != null && Live(manager, current) && guard++ < manager.m_pathUnits.m_buffer.Length)
            {
                EntityIdentityV2 identity; if (ids.TryGetIdentity(current, out identity)) result.Add(identity);
                if (!chain) break; current = manager.m_pathUnits.m_buffer[current].m_nextPathUnit;
            }
            return result.ToArray();
        }

        internal static void CompleteHostRelease(EntityIdentityV2[] identities)
        {
            if (identities == null || identities.Length == 0) return; EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter);
            PathManager manager = PathManager.instance;
            for (int i = 0; i < identities.Length; i++)
            {
                uint native; if (ids.TryGetNative(identities[i], out native) && !Live(manager, native) && !ids.Retire(identities[i]))
                    throw new InvalidOperationException("Released Host Path identity could not be retired.");
            }
            if (current != null) current.mappingShards.Invalidate();
        }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            if (context == null) throw new ArgumentNullException("context"); ValidateShard(shardIndex);
            if (!context.IsAuthoritative && replicaShards[shardIndex] != null) return (byte[])replicaShards[shardIndex].Clone();
            PathManager manager = PathManager.instance; if (manager == null) throw new InvalidOperationException("PathManager is unavailable.");
            if (context.IsAuthoritative && capturesUntilReconcile-- <= 0) { ReconcileHostMappings(context, manager); mappingShards.Invalidate(); capturesUntilReconcile = Shards; }
            if (!mappingShards.IsValid) mappingShards.Rebuild(context);
            EntityMapEntryV2[] mappings = mappingShards.Get(shardIndex); List<State> values = new List<State>();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if (!Live(manager, mapping.NativeId)) continue;
                values.Add(CaptureOne(context, manager, mapping));
            }
            values.Sort(delegate(State a, State b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(shardIndex, values);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context"); ValidateShard(shardIndex);
            State[] requested = Decode(shardIndex, state); HashSet<EntityIdentityV2> present = new HashSet<EntityIdentityV2>();
            for (int i = 0; i < requested.Length; i++) { present.Add(requested[i].Identity); replicaStates[requested[i].Identity] = requested[i]; }
            EntityMapEntryV2[] mappings = context.SnapshotMappings(); PathManager manager = PathManager.instance;
            if (manager == null) throw new InvalidOperationException("PathManager is unavailable.");
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || present.Contains(mapping.Identity)) continue;
                replicaStates.Remove(mapping.Identity); if (Live(manager, mapping.NativeId)) manager.m_pathUnits.ReleaseItem(mapping.NativeId);
                if (!context.RetireIdentity(mapping.Identity)) throw new InvalidOperationException("Path identity retirement failed.");
            }
            for (int i = 0; i < requested.Length; i++) EnsureReplicaUnit(context, manager, requested[i].Identity);
            ProjectReplicaStates(context, manager);
            VehiclePresentationStateAdapter.RestoreClientPathReferences();
            CitizenInstancePresentationStateAdapter.RestoreClientPathReferences();
            replicaShards[shardIndex] = (byte[])state.Clone();
        }

        private static State CaptureOne(IForgeAdapterContextV1 context, PathManager manager, EntityMapEntryV2 mapping)
        {
            PathUnit unit = manager.m_pathUnits.m_buffer[mapping.NativeId]; int count = unit.m_positionCount & 15;
            if (count > 12) throw new InvalidOperationException("PathUnit position count is outside the audited range.");
            Position[] positions = new Position[count];
            for (int i = 0; i < count; i++)
            {
                PathUnit.Position native = unit.GetPosition(i); EntityIdentityV2 segment = default(EntityIdentityV2);
                if (native.m_segment != 0 && !RuntimeServices.Multiplayer.TryResolveStableNameIdentity(
                    StableNameTargetKindV2.NetSegment, native.m_segment, context.IsAuthoritative, out segment))
                    throw new InvalidOperationException("PathUnit references an unmapped Net segment.");
                positions[i] = new Position { Segment = segment, Lane = native.m_lane, Offset = native.m_offset };
            }
            EntityIdentityV2 next = default(EntityIdentityV2);
            if (unit.m_nextPathUnit != 0 && !context.TryGetIdentity(unit.m_nextPathUnit, out next))
                throw new InvalidOperationException("PathUnit chain references an unmapped path identity.");
            return new State
            {
                Identity = mapping.Identity, BuildIndex = unit.m_buildIndex, Next = next, Length = unit.m_length,
                LaneTypes = unit.m_laneTypes, VehicleTypes = unit.m_vehicleTypes, VehicleCategories = unit.m_vehicleCategories,
                PathFindFlags = unit.m_pathFindFlags, SimulationFlags = (byte)unit.m_simulationFlags,
                ReferenceCount = unit.m_referenceCount, Speed = unit.m_speed, PositionCount = unit.m_positionCount, Positions = positions
            };
        }

        private static void ReconcileHostMappings(IForgeAdapterContextV1 context, PathManager manager)
        {
            EntityMapEntryV2[] existing = context.SnapshotMappings();
            for (int i = 0; i < existing.Length; i++) if (!Live(manager, existing[i].NativeId)) context.RetireIdentity(existing[i].Identity);
            int limit = manager.m_pathUnits.m_buffer.Length;
            for (uint native = 1; native < (uint)limit; native++) if (Live(manager, native)) context.GetOrAllocateIdentity(native);
        }

        private static bool Live(PathManager manager, uint native)
        { return native != 0 && native < manager.m_pathUnits.m_buffer.Length && manager.m_pathUnits.m_buffer[native].m_referenceCount != 0; }

        private static void EnsureReplicaUnit(IForgeAdapterContextV1 context, PathManager manager, EntityIdentityV2 identity)
        {
            uint native; if (context.TryGetNative(identity, out native) && Live(manager, native)) return;
            if (native != 0) throw new InvalidOperationException("Replica Path identity mapping targets an empty native slot.");
            Randomizer randomizer = SimulationManager.instance.m_randomizer;
            if (!manager.m_pathUnits.CreateItem(out native, ref randomizer) || native == 0) throw new InvalidOperationException("CS1 could not allocate a replica PathUnit.");
            context.BindKnownIdentity(identity, native);
        }

        private void ProjectReplicaStates(IForgeAdapterContextV1 context, PathManager manager)
        {
            foreach (KeyValuePair<EntityIdentityV2, State> pair in replicaStates)
            {
                uint native; if (!context.TryGetNative(pair.Key, out native) || !Live(manager, native)) continue;
                State value = pair.Value; PathUnit unit = manager.m_pathUnits.m_buffer[native];
                unit.m_buildIndex = value.BuildIndex; unit.m_length = value.Length; unit.m_laneTypes = value.LaneTypes;
                unit.m_vehicleTypes = value.VehicleTypes; unit.m_vehicleCategories = value.VehicleCategories;
                unit.m_pathFindFlags = value.PathFindFlags; unit.m_simulationFlags = (PathUnit.SimulationFlags)value.SimulationFlags;
                unit.m_referenceCount = value.ReferenceCount == 0 ? (byte)1 : value.ReferenceCount; unit.m_speed = value.Speed;
                unit.m_positionCount = value.PositionCount;
                for (int i = 0; i < 12; i++) unit.SetPosition(i, default(PathUnit.Position));
                for (int i = 0; i < value.Positions.Length; i++)
                {
                    ushort segment = 0;
                    if (value.Positions[i].Segment.IsValid)
                    {
                        uint resolved;
                        if (!RuntimeServices.Multiplayer.TryResolveStableNameNative(StableNameTargetKindV2.NetSegment,
                            value.Positions[i].Segment, false, out resolved) || resolved == 0 || resolved > ushort.MaxValue)
                            throw new InvalidOperationException("Replica PathUnit references an unavailable Stable Net segment.");
                        segment = (ushort)resolved;
                    }
                    unit.SetPosition(i, new PathUnit.Position { m_segment = segment, m_lane = value.Positions[i].Lane, m_offset = value.Positions[i].Offset });
                }
                uint next = 0; if (value.Next.IsValid) context.TryGetNative(value.Next, out next); unit.m_nextPathUnit = next;
                manager.m_pathUnits.m_buffer[native] = unit;
            }
        }

        private static byte[] Encode(int shardIndex, IList<State> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shardIndex); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    State value = values[i]; writer.Write(value.Identity.EntityId); writer.Write(value.Identity.Generation);
                    writer.Write(value.BuildIndex); WriteIdentity(writer, value.Next); writer.Write(value.Length); writer.Write(value.LaneTypes);
                    writer.Write(value.VehicleTypes); writer.Write(value.VehicleCategories); writer.Write(value.PathFindFlags);
                    writer.Write(value.SimulationFlags); writer.Write(value.ReferenceCount); writer.Write(value.Speed); writer.Write(value.PositionCount);
                    for (int p = 0; p < value.Positions.Length; p++)
                    { WriteIdentity(writer, value.Positions[p].Segment); writer.Write(value.Positions[p].Lane); writer.Write(value.Positions[p].Offset); }
                }
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Path result shard exceeds one Forge frame."); return stream.ToArray();
            }
        }

        private static State[] Decode(int shardIndex, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid Path result shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shardIndex) throw new InvalidDataException("Invalid Path result shard header.");
                int count = reader.ReadUInt16(); State[] result = new State[count]; ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = ReadIdentity(reader, true);
                    if (identity.EntityId <= previous || (int)((identity.EntityId - 1UL) % Shards) != shardIndex) throw new InvalidDataException("Path result identities are not canonical.");
                    previous = identity.EntityId; State value = new State { Identity = identity, BuildIndex = reader.ReadUInt32(), Next = ReadIdentity(reader, false), Length = reader.ReadSingle(), LaneTypes = reader.ReadByte(), VehicleTypes = reader.ReadUInt32(), VehicleCategories = reader.ReadInt64(), PathFindFlags = reader.ReadByte(), SimulationFlags = reader.ReadByte(), ReferenceCount = reader.ReadByte(), Speed = reader.ReadByte() };
                    value.PositionCount = reader.ReadByte(); int positions = value.PositionCount & 15; if (positions > 12) throw new InvalidDataException("Too many PathUnit positions."); value.Positions = new Position[positions];
                    for (int p = 0; p < positions; p++) value.Positions[p] = new Position { Segment = ReadIdentity(reader, false), Lane = reader.ReadByte(), Offset = reader.ReadByte() };
                    result[i] = value;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Path result bytes."); return result;
            }
        }

        private static void WriteIdentity(BinaryWriter writer, EntityIdentityV2 value) { writer.Write(value.EntityId); writer.Write(value.Generation); }
        private static EntityIdentityV2 ReadIdentity(BinaryReader reader, bool required)
        {
            ulong entityId = reader.ReadUInt64(); uint generation = reader.ReadUInt32();
            if (entityId == 0 && generation == 0) { if (required) throw new InvalidDataException("Required stable identity is empty."); return default(EntityIdentityV2); }
            if (entityId == 0 || generation == 0) throw new InvalidDataException("Invalid stable identity."); return new EntityIdentityV2(entityId, generation);
        }
        private static void ValidateShard(int value) { if (value < 0 || value >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }
        private sealed class State
        {
            public EntityIdentityV2 Identity, Next; public uint BuildIndex, VehicleTypes; public long VehicleCategories; public float Length;
            public byte LaneTypes, PathFindFlags, SimulationFlags, ReferenceCount, Speed, PositionCount; public Position[] Positions;
        }
        private struct Position { public EntityIdentityV2 Segment; public byte Lane, Offset; }
    }
}
