using System;
using System.Collections.Generic;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Absolute projection of every Building field written by the audited Infinite Goods 6.1 target
    /// AI material-buffer paths. It is activated with Infinite Goods so the Host can run the original
    /// mod against its local Building slots while clients receive results keyed by Forge Building
    /// EntityIdentityV2 rather than ushort building IDs.
    /// </summary>
    internal sealed class InfiniteGoodsBuildingBufferAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x32424749u; // IGB2
        private const int Shards = 128;
        internal const string Adapter = "bridge.infinitegoods-buildingbuffers";
        private readonly Dictionary<EntityIdentityV2, ushort> nativeByIdentity = new Dictionary<EntityIdentityV2, ushort>();
        private int capturesUntilRefresh;
        private bool lastHostSide;

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 2; } }
        public int ShardCount { get { return Shards; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            Check.NotNull(context, "context");
            ValidateShard(shardIndex);
            EnsureCache(context.IsAuthoritative, false);
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            List<State> values = new List<State>();
            foreach (KeyValuePair<EntityIdentityV2, ushort> pair in nativeByIdentity)
            {
                if ((int)((pair.Key.EntityId - 1UL) % Shards) != shardIndex) continue;
                ushort native = pair.Value;
                if (native == 0 || native >= manager.m_buildings.m_buffer.Length) continue;
                Building data = manager.m_buildings.m_buffer[native];
                if (data.m_flags == Building.Flags.None) continue;
                values.Add(new State
                {
                    Identity = pair.Key,
                    Buffer1 = data.m_customBuffer1,
                    Buffer2 = data.m_customBuffer2,
                    CashBuffer = data.m_cashBuffer,
                    OutgoingProblemTimer = data.m_outgoingProblemTimer,
                    Youngs = data.m_youngs,
                    Teens = data.m_teens,
                    Adults = data.m_adults,
                    Seniors = data.m_seniors,
                    Education1 = data.m_education1,
                    Education2 = data.m_education2
                });
            }
            values.Sort(delegate(State a, State b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return Encode(shardIndex, values);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            ValidateShard(shardIndex);
            State[] values = Decode(shardIndex, state);
            EnsureCache(false, false);
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            for (int i = 0; i < values.Length; i++)
            {
                ushort native;
                if (!nativeByIdentity.TryGetValue(values[i].Identity, out native))
                {
                    EnsureCache(false, true);
                    if (!nativeByIdentity.TryGetValue(values[i].Identity, out native))
                        throw new InvalidOperationException("Infinite Goods buffer state references an unknown Building stable identity.");
                }
                Building data = manager.m_buildings.m_buffer[native];
                if (data.m_flags == Building.Flags.None) throw new InvalidOperationException("Infinite Goods buffer projection target is not live.");
                data.m_customBuffer1 = values[i].Buffer1;
                data.m_customBuffer2 = values[i].Buffer2;
                data.m_cashBuffer = values[i].CashBuffer;
                data.m_outgoingProblemTimer = values[i].OutgoingProblemTimer;
                data.m_youngs = values[i].Youngs;
                data.m_teens = values[i].Teens;
                data.m_adults = values[i].Adults;
                data.m_seniors = values[i].Seniors;
                data.m_education1 = values[i].Education1;
                data.m_education2 = values[i].Education2;
                manager.m_buildings.m_buffer[native] = data;
            }
        }

        private void EnsureCache(bool hostSide, bool force)
        {
            if (!force && nativeByIdentity.Count != 0 && lastHostSide == hostSide && capturesUntilRefresh-- > 0) return;
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            nativeByIdentity.Clear();
            int limit = manager.m_buildings.m_buffer.Length;
            if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                ushort native = (ushort)i;
                if (manager.m_buildings.m_buffer[native].m_flags == Building.Flags.None) continue;
                EntityIdentityV2 identity;
                if (!RuntimeServices.Multiplayer.TryResolveStableNameIdentity(StableNameTargetKindV2.Building, native, hostSide, out identity))
                    continue;
                nativeByIdentity[identity] = native;
            }
            lastHostSide = hostSide;
            capturesUntilRefresh = Shards;
        }

        private static byte[] Encode(int shardIndex, List<State> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)shardIndex); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    writer.Write(values[i].Identity.EntityId); writer.Write(values[i].Identity.Generation);
                    writer.Write(values[i].Buffer1); writer.Write(values[i].Buffer2);
                    writer.Write(values[i].CashBuffer); writer.Write(values[i].OutgoingProblemTimer);
                    writer.Write(values[i].Youngs); writer.Write(values[i].Teens);
                    writer.Write(values[i].Adults); writer.Write(values[i].Seniors);
                    writer.Write(values[i].Education1); writer.Write(values[i].Education2);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Infinite Goods Building buffer shard exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static State[] Decode(int shardIndex, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid Infinite Goods Building buffer shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != (byte)shardIndex)
                    throw new InvalidDataException("Invalid Infinite Goods Building buffer shard header.");
                int count = reader.ReadUInt16();
                State[] result = new State[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (!identity.IsValid || identity.EntityId <= previous)
                        throw new InvalidDataException("Infinite Goods Building buffers are not canonically ordered.");
                    if ((int)((identity.EntityId - 1UL) % Shards) != shardIndex)
                        throw new InvalidDataException("Infinite Goods Building buffer entity is in the wrong shard.");
                    previous = identity.EntityId;
                    result[i] = new State
                    {
                        Identity = identity,
                        Buffer1 = reader.ReadUInt16(),
                        Buffer2 = reader.ReadUInt16(),
                        CashBuffer = reader.ReadInt32(),
                        OutgoingProblemTimer = reader.ReadByte(),
                        Youngs = reader.ReadByte(),
                        Teens = reader.ReadByte(),
                        Adults = reader.ReadByte(),
                        Seniors = reader.ReadByte(),
                        Education1 = reader.ReadByte(),
                        Education2 = reader.ReadByte()
                    };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Infinite Goods Building buffer bytes.");
                return result;
            }
        }

        private static void ValidateShard(int shardIndex)
        {
            if (shardIndex < 0 || shardIndex >= Shards) throw new ArgumentOutOfRangeException("shardIndex");
        }

        private sealed class State
        {
            public EntityIdentityV2 Identity;
            public ushort Buffer1;
            public ushort Buffer2;
            public int CashBuffer;
            public byte OutgoingProblemTimer;
            public byte Youngs;
            public byte Teens;
            public byte Adults;
            public byte Seniors;
            public byte Education1;
            public byte Education2;
        }
    }
}
