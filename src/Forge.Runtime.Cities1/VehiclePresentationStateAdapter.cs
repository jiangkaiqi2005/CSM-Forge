using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Math;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Coarse Host vehicle lifecycle and presentation results; no native entity id is serialized.</summary>
    internal sealed class VehiclePresentationStateAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31565646u; // FVV1
        private const int Shards = 256;
        internal const string Adapter = "builtin.vehicle-presentation";
        private static VehiclePresentationStateAdapter current;
        private readonly byte[][] replicaShards = new byte[Shards][];
        private readonly Dictionary<ushort, EntityIdentityV2> replicaPathByNative = new Dictionary<ushort, EntityIdentityV2>();
        private readonly StableMappingShardIndex mappingShards = new StableMappingShardIndex(Shards);
        private int capturesUntilReconcile;

        public VehiclePresentationStateAdapter() { current = this; }

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        internal static void ObserveHostCreated(ushort native)
        {
            if (native == 0 || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityIdentityV2 ignored;
            if (!ids.TryGetIdentity(native, out ignored)) ids.Allocate(native);
            if (current != null) current.mappingShards.Invalidate();
        }

        internal static void ReconcileReleasedHostVehicles()
        {
            if (RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive || VehicleManager.instance == null) return;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityMapEntryV2[] entries = ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++) if (!Live(entries[i].NativeId)) ids.Retire(entries[i].Identity);
            if (current != null) current.mappingShards.Invalidate();
        }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            if (context == null) throw new ArgumentNullException("context"); ValidateShard(shardIndex);
            if (!context.IsAuthoritative && replicaShards[shardIndex] != null) return (byte[])replicaShards[shardIndex].Clone();
            VehicleManager manager = VehicleManager.instance; if (manager == null) throw new InvalidOperationException("VehicleManager is unavailable.");
            if (context.IsAuthoritative && capturesUntilReconcile-- <= 0) { ReconcileHost(context, manager); mappingShards.Invalidate(); capturesUntilReconcile = Shards; }
            if (!mappingShards.IsValid) mappingShards.Rebuild(context);
            EntityMapEntryV2[] mappings = mappingShards.Get(shardIndex); List<State> values = new List<State>();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if (!Live(mapping.NativeId)) continue;
                Vehicle data = manager.m_vehicles.m_buffer[(ushort)mapping.NativeId]; VehicleInfo info = data.Info;
                if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Vehicle prefab is unavailable.");
                State value = new State
                {
                    Identity = mapping.Identity, Prefab = info.name, Flags = (int)data.m_flags, Flags2 = (int)data.m_flags2,
                    Frame0 = data.m_frame0, Frame1 = data.m_frame1, Frame2 = data.m_frame2, Frame3 = data.m_frame3,
                    SegmentA = data.m_segment.a, SegmentB = data.m_segment.b, Target0 = data.m_targetPos0, Target1 = data.m_targetPos1,
                    Target2 = data.m_targetPos2, Target3 = data.m_targetPos3, LastFrame = data.m_lastFrame,
                    BlockCounter = data.m_blockCounter, WaitCounter = data.m_waitCounter, LastPathOffset = data.m_lastPathOffset,
                    PathPositionIndex = data.m_pathPositionIndex, TransferSize = data.m_transferSize, TransferType = data.m_transferType,
                    Custom = data.m_custom, GateIndex = data.m_gateIndex, RacerIndex = data.m_racerIndex,
                    RaceTeammate = data.m_raceTeammate, TouristCount = data.m_touristCount
                };
                value.SourceBuilding = Stable(StableNameTargetKindV2.Building, data.m_sourceBuilding, context.IsAuthoritative);
                value.TargetBuilding = Stable(StableNameTargetKindV2.Building, data.m_targetBuilding, context.IsAuthoritative);
                value.TransportLine = Stable(StableNameTargetKindV2.TransportLine, data.m_transportLine, context.IsAuthoritative);
                if (data.m_path != 0)
                {
                    if (context.IsAuthoritative) value.Path = PathUnitStateAdapter.ResolveHostIdentity(data.m_path);
                    else if (!ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter).TryGetIdentity(data.m_path, out value.Path))
                        throw new InvalidOperationException("Replica Vehicle references an unmapped Stable Path identity.");
                }
                values.Add(value);
            }
            values.Sort(delegate(State a, State b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); }); return Encode(shardIndex, values);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context"); ValidateShard(shardIndex);
            State[] requested = Decode(shardIndex, state); HashSet<EntityIdentityV2> present = new HashSet<EntityIdentityV2>();
            for (int i = 0; i < requested.Length; i++) present.Add(requested[i].Identity);
            EntityMapEntryV2[] mappings = context.SnapshotMappings(); VehicleManager manager = VehicleManager.instance;
            if (manager == null) throw new InvalidOperationException("VehicleManager is unavailable.");
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || present.Contains(mapping.Identity)) continue;
                if (Live(mapping.NativeId)) manager.ReleaseVehicle((ushort)mapping.NativeId); replicaPathByNative.Remove((ushort)mapping.NativeId);
                if (!context.RetireIdentity(mapping.Identity)) throw new InvalidOperationException("Vehicle identity retirement failed.");
            }
            for (int i = 0; i < requested.Length; i++) ApplyOne(context, manager, requested[i]);
            replicaShards[shardIndex] = (byte[])state.Clone();
        }

        private void ApplyOne(IForgeAdapterContextV1 context, VehicleManager manager, State value)
        {
            uint nativeValue;
            if (!context.TryGetNative(value.Identity, out nativeValue))
            {
                VehicleInfo info = PrefabCollection<VehicleInfo>.FindLoaded(value.Prefab);
                if (info == null) throw new InvalidOperationException("Vehicle prefab is not loaded: " + value.Prefab);
                Randomizer randomizer = SimulationManager.instance.m_randomizer; ushort created;
                if (!manager.CreateVehicle(out created, ref randomizer, info, value.Frame0.m_position, TransferManager.TransferReason.None, false, false) || created == 0)
                    throw new InvalidOperationException("CS1 could not create a replica Vehicle.");
                nativeValue = created; context.BindKnownIdentity(value.Identity, nativeValue);
            }
            if (!Live(nativeValue) || nativeValue > ushort.MaxValue) throw new InvalidOperationException("Replica Vehicle mapping is not live.");
            ushort native = (ushort)nativeValue; Vehicle data = manager.m_vehicles.m_buffer[native];
            if (data.Info == null || data.Info.name != value.Prefab) throw new InvalidOperationException("Replica Vehicle prefab differs from Host result.");
            data.m_flags = (Vehicle.Flags)value.Flags; data.m_flags2 = (Vehicle.Flags2)value.Flags2;
            data.m_frame0 = value.Frame0; data.m_frame1 = value.Frame1; data.m_frame2 = value.Frame2; data.m_frame3 = value.Frame3;
            data.m_segment.a = value.SegmentA; data.m_segment.b = value.SegmentB; data.m_targetPos0 = value.Target0; data.m_targetPos1 = value.Target1;
            data.m_targetPos2 = value.Target2; data.m_targetPos3 = value.Target3; data.m_lastFrame = value.LastFrame;
            data.m_blockCounter = value.BlockCounter; data.m_waitCounter = value.WaitCounter; data.m_lastPathOffset = value.LastPathOffset;
            data.m_pathPositionIndex = value.PathPositionIndex; data.m_transferSize = value.TransferSize; data.m_transferType = value.TransferType;
            data.m_custom = value.Custom; data.m_gateIndex = value.GateIndex; data.m_racerIndex = value.RacerIndex;
            data.m_raceTeammate = value.RaceTeammate; data.m_touristCount = value.TouristCount;
            data.m_sourceBuilding = Resolve(StableNameTargetKindV2.Building, value.SourceBuilding);
            data.m_targetBuilding = Resolve(StableNameTargetKindV2.Building, value.TargetBuilding);
            data.m_transportLine = Resolve(StableNameTargetKindV2.TransportLine, value.TransportLine);
            uint path = 0; if (value.Path.IsValid) ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter).TryGetNative(value.Path, out path); data.m_path = path;
            manager.m_vehicles.m_buffer[native] = data; replicaPathByNative[native] = value.Path;
        }

        internal static void RestoreClientPathReferences()
        {
            if (current == null || VehicleManager.instance == null) return; EntityIdMapV2 paths = ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter);
            foreach (KeyValuePair<ushort, EntityIdentityV2> pair in current.replicaPathByNative)
            {
                if (!Live(pair.Key)) continue; uint path = 0; if (pair.Value.IsValid) paths.TryGetNative(pair.Value, out path);
                Vehicle data = VehicleManager.instance.m_vehicles.m_buffer[pair.Key]; data.m_path = path; VehicleManager.instance.m_vehicles.m_buffer[pair.Key] = data;
            }
        }

        private static void ReconcileHost(IForgeAdapterContextV1 context, VehicleManager manager)
        {
            EntityMapEntryV2[] entries = context.SnapshotMappings();
            for (int i = 0; i < entries.Length; i++) if (!Live(entries[i].NativeId)) context.RetireIdentity(entries[i].Identity);
            int limit = manager.m_vehicles.m_buffer.Length; if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (uint native = 1; native < (uint)limit; native++) if (Live(native)) context.GetOrAllocateIdentity(native);
        }

        private static bool Live(uint native)
        { return VehicleManager.instance != null && native != 0 && native < VehicleManager.instance.m_vehicles.m_buffer.Length && (int)VehicleManager.instance.m_vehicles.m_buffer[(ushort)native].m_flags != 0; }

        private static EntityIdentityV2 Stable(StableNameTargetKindV2 kind, uint native, bool host)
        {
            if (native == 0) return default(EntityIdentityV2);
            if (kind == StableNameTargetKindV2.Building)
            {
                BuildingManager manager = BuildingManager.instance;
                if (manager == null || native >= (uint)manager.m_buildings.m_buffer.Length ||
                    manager.m_buildings.m_buffer[(ushort)native].m_flags == Building.Flags.None)
                    return default(EntityIdentityV2);
            }
            EntityIdentityV2 identity;
            if (!RuntimeServices.Multiplayer.TryResolveStableNameIdentity(kind, native, host, out identity))
                throw new InvalidOperationException("Vehicle references a live unmapped stable entity.");
            return identity;
        }

        private static ushort Resolve(StableNameTargetKindV2 kind, EntityIdentityV2 identity)
        {
            if (!identity.IsValid) return 0; uint native;
            if (!RuntimeServices.Multiplayer.TryResolveStableNameNative(kind, identity, false, out native) || native == 0 || native > ushort.MaxValue)
                throw new InvalidOperationException("Vehicle stable reference is unavailable on replica."); return (ushort)native;
        }

        private static byte[] Encode(int shard, IList<State> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shard); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    State v = values[i]; WriteIdentity(writer, v.Identity); WriteString(writer, v.Prefab); writer.Write(v.Flags); writer.Write(v.Flags2);
                    WriteFrame(writer, v.Frame0); WriteFrame(writer, v.Frame1); WriteFrame(writer, v.Frame2); WriteFrame(writer, v.Frame3);
                    WriteVector3(writer, v.SegmentA); WriteVector3(writer, v.SegmentB); WriteVector4(writer, v.Target0); WriteVector4(writer, v.Target1); WriteVector4(writer, v.Target2); WriteVector4(writer, v.Target3);
                    WriteIdentity(writer, v.SourceBuilding); WriteIdentity(writer, v.TargetBuilding); WriteIdentity(writer, v.TransportLine); WriteIdentity(writer, v.Path);
                    writer.Write(v.LastFrame); writer.Write(v.BlockCounter); writer.Write(v.WaitCounter); writer.Write(v.LastPathOffset); writer.Write(v.PathPositionIndex);
                    writer.Write(v.TransferSize); writer.Write(v.TransferType); writer.Write(v.Custom); writer.Write(v.GateIndex); writer.Write(v.RacerIndex); writer.Write(v.RaceTeammate); writer.Write(v.TouristCount);
                }
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Vehicle presentation shard exceeds one Forge frame."); return stream.ToArray();
            }
        }

        private static State[] Decode(int shard, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid Vehicle presentation shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shard) throw new InvalidDataException("Invalid Vehicle presentation shard header.");
                int count = reader.ReadUInt16(); State[] result = new State[count]; ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    State v = new State(); v.Identity = ReadIdentity(reader, true);
                    if (v.Identity.EntityId <= previous || (int)((v.Identity.EntityId - 1UL) % Shards) != shard) throw new InvalidDataException("Vehicle identities are not canonical."); previous = v.Identity.EntityId;
                    v.Prefab = ReadString(reader); v.Flags = reader.ReadInt32(); v.Flags2 = reader.ReadInt32();
                    v.Frame0 = ReadFrame(reader); v.Frame1 = ReadFrame(reader); v.Frame2 = ReadFrame(reader); v.Frame3 = ReadFrame(reader);
                    v.SegmentA = ReadVector3(reader); v.SegmentB = ReadVector3(reader); v.Target0 = ReadVector4(reader); v.Target1 = ReadVector4(reader); v.Target2 = ReadVector4(reader); v.Target3 = ReadVector4(reader);
                    v.SourceBuilding = ReadIdentity(reader, false); v.TargetBuilding = ReadIdentity(reader, false); v.TransportLine = ReadIdentity(reader, false); v.Path = ReadIdentity(reader, false);
                    v.LastFrame = reader.ReadByte(); v.BlockCounter = reader.ReadByte(); v.WaitCounter = reader.ReadByte(); v.LastPathOffset = reader.ReadByte(); v.PathPositionIndex = reader.ReadByte();
                    v.TransferSize = reader.ReadUInt16(); v.TransferType = reader.ReadByte(); v.Custom = reader.ReadUInt16(); v.GateIndex = reader.ReadByte(); v.RacerIndex = reader.ReadByte(); v.RaceTeammate = reader.ReadByte(); v.TouristCount = reader.ReadUInt16(); result[i] = v;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Vehicle presentation bytes."); return result;
            }
        }

        private static void WriteFrame(BinaryWriter w, Vehicle.Frame v) { WriteVector3(w, v.m_position); WriteQuaternion(w, v.m_rotation); WriteVector3(w, v.m_velocity); WriteVector3(w, v.m_swayPosition); WriteVector3(w, v.m_swayVelocity); w.Write(v.m_steerAngle); w.Write(v.m_travelDistance); w.Write(v.m_lightIntensity.x); w.Write(v.m_lightIntensity.y); w.Write(v.m_lightIntensity.z); w.Write(v.m_lightIntensity.w); w.Write(v.m_blinkState); w.Write(v.m_underground); w.Write(v.m_transition); w.Write(v.m_insideBuilding); w.Write(v.m_angleVelocity); }
        private static Vehicle.Frame ReadFrame(BinaryReader r) { Vehicle.Frame v = new Vehicle.Frame(); v.m_position = ReadVector3(r); v.m_rotation = ReadQuaternion(r); v.m_velocity = ReadVector3(r); v.m_swayPosition = ReadVector3(r); v.m_swayVelocity = ReadVector3(r); v.m_steerAngle = r.ReadSingle(); v.m_travelDistance = r.ReadSingle(); v.m_lightIntensity = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); v.m_blinkState = r.ReadSingle(); v.m_underground = r.ReadBoolean(); v.m_transition = r.ReadBoolean(); v.m_insideBuilding = r.ReadBoolean(); v.m_angleVelocity = r.ReadSingle(); return v; }
        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector3(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteVector4(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        private static Vector4 ReadVector4(BinaryReader r) { return new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteQuaternion(BinaryWriter w, Quaternion v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        private static Quaternion ReadQuaternion(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteIdentity(BinaryWriter w, EntityIdentityV2 v) { w.Write(v.EntityId); w.Write(v.Generation); }
        private static EntityIdentityV2 ReadIdentity(BinaryReader r, bool required) { ulong id = r.ReadUInt64(); uint generation = r.ReadUInt32(); if (id == 0 && generation == 0) { if (required) throw new InvalidDataException("Required Vehicle identity is empty."); return default(EntityIdentityV2); } if (id == 0 || generation == 0) throw new InvalidDataException("Invalid Vehicle stable identity."); return new EntityIdentityV2(id, generation); }
        private static void WriteString(BinaryWriter w, string v) { byte[] b = System.Text.Encoding.UTF8.GetBytes(v); if (b.Length == 0 || b.Length > 192) throw new InvalidDataException("Invalid Vehicle prefab identity."); w.Write((byte)b.Length); w.Write(b); }
        private static string ReadString(BinaryReader r) { int n = r.ReadByte(); if (n == 0 || n > 192) throw new InvalidDataException("Invalid Vehicle prefab identity."); byte[] b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); return System.Text.Encoding.UTF8.GetString(b); }
        private static void ValidateShard(int value) { if (value < 0 || value >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }

        private sealed class State
        {
            public EntityIdentityV2 Identity, SourceBuilding, TargetBuilding, TransportLine, Path; public string Prefab; public int Flags, Flags2;
            public Vehicle.Frame Frame0, Frame1, Frame2, Frame3; public Vector3 SegmentA, SegmentB; public Vector4 Target0, Target1, Target2, Target3;
            public byte LastFrame, BlockCounter, WaitCounter, LastPathOffset, PathPositionIndex, TransferType, GateIndex, RacerIndex, RaceTeammate;
            public ushort TransferSize, Custom, TouristCount;
        }
    }
}
