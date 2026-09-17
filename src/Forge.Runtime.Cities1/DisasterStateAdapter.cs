using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Host-authoritative Natural Disasters state. A disaster is identified by EntityIdentityV2;
    /// local DisasterManager ushort slots are allocated independently on every process.
    /// </summary>
    internal sealed class DisasterStateAdapter : IForgeStateAdapterV2
    {
        private const uint Magic = 0x31534446u; // FDS1
        internal const string Adapter = "builtin.disasters";

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            if (context == null) throw new ArgumentNullException("context");
            DisasterManager manager = DisasterManager.instance;
            if (manager == null) throw new InvalidOperationException("DisasterManager is unavailable.");
            List<DisasterState> values = new List<DisasterState>();
            HashSet<ulong> liveEntities = new HashSet<ulong>();
            int limit = manager.m_disasters.m_buffer.Length;
            if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                ushort native = (ushort)i;
                if (!Live(manager, native)) continue;
                EntityIdentityV2 identity;
                if (!context.TryGetIdentity(native, out identity))
                {
                    if (!context.IsAuthoritative)
                        throw new InvalidOperationException("Replica disaster has no Host-issued stable identity.");
                    identity = context.GetOrAllocateIdentity(native);
                }
                liveEntities.Add(identity.EntityId);
                values.Add(CaptureOne(identity, native));
            }
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
                if (!liveEntities.Contains(mappings[i].Identity.EntityId) && !context.RetireIdentity(mappings[i].Identity))
                    throw new InvalidOperationException("Could not retire a stale Disaster identity.");
            values.Sort(delegate(DisasterState a, DisasterState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(values);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            DisasterManager manager = DisasterManager.instance;
            if (manager == null) throw new InvalidOperationException("DisasterManager is unavailable.");
            DisasterState[] desired = Decode(state);
            HashSet<ulong> wanted = new HashSet<ulong>();
            for (int i = 0; i < desired.Length; i++)
            {
                DisasterState value = desired[i];
                wanted.Add(value.Identity.EntityId);
                uint nativeValue;
                ushort native;
                if (context.TryGetNative(value.Identity, out nativeValue))
                {
                    if (nativeValue == 0 || nativeValue > ushort.MaxValue)
                        throw new InvalidOperationException("Disaster stable mapping exceeds ushort range.");
                    native = (ushort)nativeValue;
                    if (!Live(manager, native))
                        throw new InvalidOperationException("Disaster stable identity points to an empty local slot.");
                }
                else
                {
                    if (context.IsAuthoritative) throw new InvalidOperationException("Host lost a Disaster stable identity mapping.");
                    DisasterInfo info = PrefabCollection<DisasterInfo>.FindLoaded(value.PrefabKey);
                    if (info == null) throw new InvalidOperationException("Disaster prefab is not loaded: " + value.PrefabKey);
                    if (!manager.CreateDisaster(out native, info) || native == 0)
                        throw new InvalidOperationException("CS1 could not materialize a replica Disaster slot.");
                    context.BindKnownIdentity(value.Identity, native);
                }
                Install(native, value);
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if (wanted.Contains(mapping.Identity.EntityId)) continue;
                if (mapping.NativeId == 0 || mapping.NativeId > ushort.MaxValue)
                    throw new InvalidOperationException("Disaster mapping exceeds ushort range.");
                ushort native = (ushort)mapping.NativeId;
                if (Live(manager, native)) manager.ReleaseDisaster(native);
                if (!context.RetireIdentity(mapping.Identity))
                    throw new InvalidOperationException("Replica Disaster identity retirement failed.");
            }
        }

        private static DisasterState CaptureOne(EntityIdentityV2 identity, ushort native)
        {
            DisasterData data = DisasterManager.instance.m_disasters.m_buffer[native];
            DisasterInfo info = data.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Live disaster has no prefab identity.");
            return new DisasterState
            {
                Identity = identity,
                PrefabKey = info.name,
                Flags = Convert.ToUInt64(data.m_flags),
                Intensity = data.m_intensity,
                RandomSeed = data.m_randomSeed,
                Target = data.m_targetPosition,
                Angle = data.m_angle,
                ActivationFrame = data.m_activationFrame,
                StartFrame = data.m_startFrame,
                WaveIndex = data.m_waveIndex,
                Casualties = data.m_casualtiesCount,
                BuildingFires = data.m_buildingFireCount,
                TreeFires = data.m_treeFireCount,
                Collapsed = data.m_collapsedCount,
                Upgraded = data.m_upgradedCount
            };
        }

        private static void Install(ushort native, DisasterState value)
        {
            DisasterManager manager = DisasterManager.instance;
            DisasterData data = manager.m_disasters.m_buffer[native];
            DisasterInfo info = data.Info;
            if (info == null || info.name != value.PrefabKey)
                throw new InvalidOperationException("Disaster stable identity prefab validation failed.");
            data.m_flags = (DisasterData.Flags)value.Flags;
            data.m_intensity = value.Intensity;
            data.m_randomSeed = value.RandomSeed;
            data.m_targetPosition = value.Target;
            data.m_angle = value.Angle;
            data.m_activationFrame = value.ActivationFrame;
            data.m_startFrame = value.StartFrame;
            data.m_waveIndex = value.WaveIndex;
            data.m_casualtiesCount = value.Casualties;
            data.m_buildingFireCount = value.BuildingFires;
            data.m_treeFireCount = value.TreeFires;
            data.m_collapsedCount = value.Collapsed;
            data.m_upgradedCount = value.Upgraded;
            manager.m_disasters.m_buffer[native] = data;
        }

        private static bool Live(DisasterManager manager, ushort native)
        {
            if (manager == null || native == 0 || native >= manager.m_disasters.m_buffer.Length) return false;
            DisasterData data = manager.m_disasters.m_buffer[native];
            return data.m_flags != DisasterData.Flags.None && data.Info != null;
        }

        private static byte[] Encode(List<DisasterState> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write(values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    DisasterState value = values[i];
                    writer.Write(value.Identity.EntityId); writer.Write(value.Identity.Generation);
                    WriteString(writer, value.PrefabKey); writer.Write(value.Flags); writer.Write(value.Intensity);
                    writer.Write(value.RandomSeed); writer.Write(value.Target.x); writer.Write(value.Target.y); writer.Write(value.Target.z);
                    writer.Write(value.Angle); writer.Write(value.ActivationFrame); writer.Write(value.StartFrame); writer.Write(value.WaveIndex);
                    writer.Write(value.Casualties); writer.Write(value.BuildingFires); writer.Write(value.TreeFires);
                    writer.Write(value.Collapsed); writer.Write(value.Upgraded);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Disaster absolute state exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static DisasterState[] Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid disaster state size.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid disaster state magic.");
                int count = reader.ReadInt32();
                if (count < 0 || count > 4096) throw new InvalidDataException("Invalid disaster state count.");
                DisasterState[] result = new DisasterState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("Disaster state is not canonically ordered.");
                    previous = identity.EntityId;
                    result[i] = new DisasterState
                    {
                        Identity = identity,
                        PrefabKey = ReadString(reader),
                        Flags = reader.ReadUInt64(),
                        Intensity = reader.ReadByte(),
                        RandomSeed = reader.ReadUInt64(),
                        Target = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        Angle = reader.ReadSingle(),
                        ActivationFrame = reader.ReadUInt32(),
                        StartFrame = reader.ReadUInt32(),
                        WaveIndex = reader.ReadUInt16(),
                        Casualties = reader.ReadUInt32(),
                        BuildingFires = reader.ReadUInt16(),
                        TreeFires = reader.ReadUInt32(),
                        Collapsed = reader.ReadUInt16(),
                        Upgraded = reader.ReadUInt16()
                    };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing disaster state bytes.");
                return result;
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length == 0 || bytes.Length > 255) throw new InvalidDataException("Invalid disaster prefab key.");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadByte();
            if (length == 0) throw new InvalidDataException("Invalid disaster prefab key length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class DisasterState
        {
            public EntityIdentityV2 Identity;
            public string PrefabKey;
            public ulong Flags;
            public byte Intensity;
            public ulong RandomSeed;
            public Vector3 Target;
            public float Angle;
            public uint ActivationFrame;
            public uint StartFrame;
            public ushort WaveIndex;
            public uint Casualties;
            public ushort BuildingFires;
            public uint TreeFires;
            public ushort Collapsed;
            public ushort Upgraded;
        }
    }
}
