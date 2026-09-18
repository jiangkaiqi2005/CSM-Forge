using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Math;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Coarse CitizenInstance lifecycle/presentation. Citizen records remain snapshot baseline Host state.</summary>
    internal sealed class CitizenInstancePresentationStateAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31494346u; // FCI1
        private const int Shards = 256;
        internal const string Adapter = "builtin.citizeninstance-presentation";
        private static CitizenInstancePresentationStateAdapter current;
        private readonly byte[][] replicaShards = new byte[Shards][];
        private readonly Dictionary<EntityIdentityV2, uint> replicaOwners = new Dictionary<EntityIdentityV2, uint>();
        private readonly Dictionary<ushort, EntityIdentityV2> replicaPathByNative = new Dictionary<ushort, EntityIdentityV2>();
        private int capturesUntilReconcile;

        public CitizenInstancePresentationStateAdapter() { current = this; }

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        internal static void ObserveHostCreated(ushort native)
        {
            if (native == 0 || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityIdentityV2 ignored;
            if (!ids.TryGetIdentity(native, out ignored)) ids.Allocate(native);
        }

        internal static void ReconcileReleasedHostInstances()
        {
            if (RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive || CitizenManager.instance == null) return;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(Adapter); EntityMapEntryV2[] entries = ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++) if (!Live(entries[i].NativeId)) ids.Retire(entries[i].Identity);
        }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            if (context == null) throw new ArgumentNullException("context"); ValidateShard(shardIndex);
            if (!context.IsAuthoritative && replicaShards[shardIndex] != null) return (byte[])replicaShards[shardIndex].Clone();
            CitizenManager manager = CitizenManager.instance; if (manager == null) throw new InvalidOperationException("CitizenManager is unavailable.");
            if (context.IsAuthoritative && capturesUntilReconcile-- <= 0) { ReconcileHost(context, manager); capturesUntilReconcile = Shards; }
            EntityMapEntryV2[] mappings = context.SnapshotMappings(); List<State> values = new List<State>();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || !Live(mapping.NativeId)) continue;
                CitizenInstance data = manager.m_instances.m_buffer[(ushort)mapping.NativeId]; CitizenInfo info = data.Info;
                if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("CitizenInstance prefab is unavailable.");
                State value = new State
                {
                    Identity = mapping.Identity, Prefab = info.name, Flags = (int)data.m_flags,
                    Frame0 = data.m_frame0, Frame1 = data.m_frame1, Frame2 = data.m_frame2, Frame3 = data.m_frame3,
                    Target = data.m_targetPos, TargetDirection = data.m_targetDir, Color = data.m_color,
                    LastFrame = data.m_lastFrame, LastPathOffset = data.m_lastPathOffset, PathPositionIndex = data.m_pathPositionIndex,
                    WaitCounter = data.m_waitCounter, TargetSeed = data.m_targetSeed, PerformerIndex = data.m_performerIndex, RacerIndex = data.m_racerIndex
                };
                value.SourceBuilding = StableBuilding(data.m_sourceBuilding, context.IsAuthoritative); value.TargetBuilding = StableBuilding(data.m_targetBuilding, context.IsAuthoritative);
                if (data.m_path != 0)
                {
                    if (context.IsAuthoritative) value.Path = PathUnitStateAdapter.ResolveHostIdentity(data.m_path);
                    else if (!ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter).TryGetIdentity(data.m_path, out value.Path))
                        throw new InvalidOperationException("Replica CitizenInstance references an unmapped Stable Path identity.");
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
            EntityMapEntryV2[] mappings = context.SnapshotMappings(); CitizenManager manager = CitizenManager.instance;
            if (manager == null) throw new InvalidOperationException("CitizenManager is unavailable.");
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || present.Contains(mapping.Identity)) continue;
                if (Live(mapping.NativeId)) manager.ReleaseCitizenInstance((ushort)mapping.NativeId); replicaPathByNative.Remove((ushort)mapping.NativeId);
                uint owner; if (replicaOwners.TryGetValue(mapping.Identity, out owner)) { replicaOwners.Remove(mapping.Identity); if (owner != 0) manager.ReleaseCitizen(owner); }
                if (!context.RetireIdentity(mapping.Identity)) throw new InvalidOperationException("CitizenInstance identity retirement failed.");
            }
            for (int i = 0; i < requested.Length; i++) ApplyOne(context, manager, requested[i]); replicaShards[shardIndex] = (byte[])state.Clone();
        }

        private void ApplyOne(IForgeAdapterContextV1 context, CitizenManager manager, State value)
        {
            uint nativeValue;
            if (!context.TryGetNative(value.Identity, out nativeValue))
            {
                CitizenInfo info = PrefabCollection<CitizenInfo>.FindLoaded(value.Prefab);
                if (info == null) throw new InvalidOperationException("Citizen prefab is not loaded: " + value.Prefab);
                Randomizer randomizer = SimulationManager.instance.m_randomizer; uint owner;
                if (!manager.CreateCitizen(out owner, 0, 0, ref randomizer) || owner == 0) throw new InvalidOperationException("CS1 could not create a replica Citizen owner.");
                ushort created;
                if (!manager.CreateCitizenInstance(out created, ref randomizer, info, owner) || created == 0)
                { manager.ReleaseCitizen(owner); throw new InvalidOperationException("CS1 could not create a replica CitizenInstance."); }
                nativeValue = created; context.BindKnownIdentity(value.Identity, nativeValue); replicaOwners[value.Identity] = owner;
            }
            if (!Live(nativeValue) || nativeValue > ushort.MaxValue) throw new InvalidOperationException("Replica CitizenInstance mapping is not live.");
            ushort native = (ushort)nativeValue; CitizenInstance data = manager.m_instances.m_buffer[native];
            if (data.Info == null || data.Info.name != value.Prefab) throw new InvalidOperationException("Replica CitizenInstance prefab differs from Host result.");
            data.m_flags = (CitizenInstance.Flags)value.Flags; data.m_frame0 = value.Frame0; data.m_frame1 = value.Frame1; data.m_frame2 = value.Frame2; data.m_frame3 = value.Frame3;
            data.m_targetPos = value.Target; data.m_targetDir = value.TargetDirection; data.m_color = value.Color; data.m_lastFrame = value.LastFrame;
            data.m_lastPathOffset = value.LastPathOffset; data.m_pathPositionIndex = value.PathPositionIndex; data.m_waitCounter = value.WaitCounter;
            data.m_targetSeed = value.TargetSeed; data.m_performerIndex = value.PerformerIndex; data.m_racerIndex = value.RacerIndex;
            data.m_sourceBuilding = ResolveBuilding(value.SourceBuilding); data.m_targetBuilding = ResolveBuilding(value.TargetBuilding);
            uint path = 0; if (value.Path.IsValid) ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter).TryGetNative(value.Path, out path); data.m_path = path;
            manager.m_instances.m_buffer[native] = data; replicaPathByNative[native] = value.Path;
        }

        internal static void RestoreClientPathReferences()
        {
            if (current == null || CitizenManager.instance == null) return; EntityIdMapV2 paths = ExtensionIdentityServices.Maps.GetOrAttach(PathUnitStateAdapter.Adapter);
            foreach (KeyValuePair<ushort, EntityIdentityV2> pair in current.replicaPathByNative)
            {
                if (!Live(pair.Key)) continue; uint path = 0; if (pair.Value.IsValid) paths.TryGetNative(pair.Value, out path);
                CitizenInstance data = CitizenManager.instance.m_instances.m_buffer[pair.Key]; data.m_path = path; CitizenManager.instance.m_instances.m_buffer[pair.Key] = data;
            }
        }

        private static void ReconcileHost(IForgeAdapterContextV1 context, CitizenManager manager)
        {
            EntityMapEntryV2[] entries = context.SnapshotMappings();
            for (int i = 0; i < entries.Length; i++) if (!Live(entries[i].NativeId)) context.RetireIdentity(entries[i].Identity);
            int limit = manager.m_instances.m_buffer.Length; if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (uint native = 1; native < (uint)limit; native++) if (Live(native)) context.GetOrAllocateIdentity(native);
        }

        private static bool Live(uint native)
        { return CitizenManager.instance != null && native != 0 && native < CitizenManager.instance.m_instances.m_buffer.Length && (int)CitizenManager.instance.m_instances.m_buffer[(ushort)native].m_flags != 0; }
        private static EntityIdentityV2 StableBuilding(ushort native, bool host) { if (native == 0) return default(EntityIdentityV2); EntityIdentityV2 value; if (!RuntimeServices.Multiplayer.TryResolveStableNameIdentity(StableNameTargetKindV2.Building, native, host, out value)) throw new InvalidOperationException("CitizenInstance references an unmapped Building identity."); return value; }
        private static ushort ResolveBuilding(EntityIdentityV2 identity) { if (!identity.IsValid) return 0; uint native; if (!RuntimeServices.Multiplayer.TryResolveStableNameNative(StableNameTargetKindV2.Building, identity, false, out native) || native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("CitizenInstance Building reference is unavailable."); return (ushort)native; }

        private static byte[] Encode(int shard, IList<State> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shard); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    State v = values[i]; WriteIdentity(writer, v.Identity); WriteString(writer, v.Prefab); writer.Write(v.Flags);
                    WriteFrame(writer, v.Frame0); WriteFrame(writer, v.Frame1); WriteFrame(writer, v.Frame2); WriteFrame(writer, v.Frame3);
                    WriteVector4(writer, v.Target); writer.Write(v.TargetDirection.x); writer.Write(v.TargetDirection.y); writer.Write(v.Color.r); writer.Write(v.Color.g); writer.Write(v.Color.b); writer.Write(v.Color.a);
                    WriteIdentity(writer, v.SourceBuilding); WriteIdentity(writer, v.TargetBuilding); WriteIdentity(writer, v.Path);
                    writer.Write(v.LastFrame); writer.Write(v.LastPathOffset); writer.Write(v.PathPositionIndex); writer.Write(v.WaitCounter); writer.Write(v.TargetSeed); writer.Write(v.PerformerIndex); writer.Write(v.RacerIndex);
                }
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("CitizenInstance presentation shard exceeds one Forge frame."); return stream.ToArray();
            }
        }

        private static State[] Decode(int shard, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid CitizenInstance shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shard) throw new InvalidDataException("Invalid CitizenInstance shard header.");
                int count = reader.ReadUInt16(); State[] result = new State[count]; ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    State v = new State(); v.Identity = ReadIdentity(reader, true);
                    if (v.Identity.EntityId <= previous || (int)((v.Identity.EntityId - 1UL) % Shards) != shard) throw new InvalidDataException("CitizenInstance identities are not canonical."); previous = v.Identity.EntityId;
                    v.Prefab = ReadString(reader); v.Flags = reader.ReadInt32(); v.Frame0 = ReadFrame(reader); v.Frame1 = ReadFrame(reader); v.Frame2 = ReadFrame(reader); v.Frame3 = ReadFrame(reader);
                    v.Target = ReadVector4(reader); v.TargetDirection = new Vector2(reader.ReadSingle(), reader.ReadSingle()); v.Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    v.SourceBuilding = ReadIdentity(reader, false); v.TargetBuilding = ReadIdentity(reader, false); v.Path = ReadIdentity(reader, false);
                    v.LastFrame = reader.ReadByte(); v.LastPathOffset = reader.ReadByte(); v.PathPositionIndex = reader.ReadByte(); v.WaitCounter = reader.ReadByte(); v.TargetSeed = reader.ReadByte(); v.PerformerIndex = reader.ReadByte(); v.RacerIndex = reader.ReadByte(); result[i] = v;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing CitizenInstance bytes."); return result;
            }
        }

        private static void WriteFrame(BinaryWriter w, CitizenInstance.Frame v) { WriteVector3(w, v.m_position); WriteQuaternion(w, v.m_rotation); WriteVector3(w, v.m_velocity); w.Write(v.m_underground); w.Write(v.m_transition); w.Write(v.m_insideBuilding); }
        private static CitizenInstance.Frame ReadFrame(BinaryReader r) { CitizenInstance.Frame v = new CitizenInstance.Frame(); v.m_position = ReadVector3(r); v.m_rotation = ReadQuaternion(r); v.m_velocity = ReadVector3(r); v.m_underground = r.ReadBoolean(); v.m_transition = r.ReadBoolean(); v.m_insideBuilding = r.ReadBoolean(); return v; }
        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector3(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteVector4(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        private static Vector4 ReadVector4(BinaryReader r) { return new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteQuaternion(BinaryWriter w, Quaternion v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        private static Quaternion ReadQuaternion(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        private static void WriteIdentity(BinaryWriter w, EntityIdentityV2 v) { w.Write(v.EntityId); w.Write(v.Generation); }
        private static EntityIdentityV2 ReadIdentity(BinaryReader r, bool required) { ulong id = r.ReadUInt64(); uint generation = r.ReadUInt32(); if (id == 0 && generation == 0) { if (required) throw new InvalidDataException("Required CitizenInstance identity is empty."); return default(EntityIdentityV2); } if (id == 0 || generation == 0) throw new InvalidDataException("Invalid CitizenInstance stable identity."); return new EntityIdentityV2(id, generation); }
        private static void WriteString(BinaryWriter w, string v) { byte[] b = System.Text.Encoding.UTF8.GetBytes(v); if (b.Length == 0 || b.Length > 192) throw new InvalidDataException("Invalid Citizen prefab identity."); w.Write((byte)b.Length); w.Write(b); }
        private static string ReadString(BinaryReader r) { int n = r.ReadByte(); if (n == 0 || n > 192) throw new InvalidDataException("Invalid Citizen prefab identity."); byte[] b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); return System.Text.Encoding.UTF8.GetString(b); }
        private static void ValidateShard(int value) { if (value < 0 || value >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }
        private sealed class State
        {
            public EntityIdentityV2 Identity, SourceBuilding, TargetBuilding, Path; public string Prefab; public int Flags;
            public CitizenInstance.Frame Frame0, Frame1, Frame2, Frame3; public Vector4 Target; public Vector2 TargetDirection; public Color32 Color;
            public byte LastFrame, LastPathOffset, PathPositionIndex, WaitCounter, TargetSeed, PerformerIndex, RacerIndex;
        }
    }
}
