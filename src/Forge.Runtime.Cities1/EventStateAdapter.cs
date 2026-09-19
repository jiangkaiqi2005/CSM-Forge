using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal enum EventControlKind : byte
    {
        SecurityBudget = 1,
        TicketPrice = 2,
        Color = 3,
        Activate = 4
    }

    /// <summary>
    /// Absolute EventManager state with Forge-stable event and building identities. Replica event
    /// slots are materialized with EventManager.CreateEvent when necessary; native ushort IDs never
    /// cross the wire and stale Host events are released by stable identity.
    /// </summary>
    internal sealed class EventStateAdapter : IForgeInteractiveStateAdapterV1
    {
        private const uint StateMagic = 0x32564546u; // FEV2
        private const uint IntentMagic = 0x32494546u; // FEI2
        internal const string Adapter = "builtin.events";

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 2; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            Check.NotNull(context, "context");
            EventManager manager = EventManager.instance;
            if (manager == null) throw new InvalidOperationException("EventManager is unavailable.");
            CoreEntityReferenceSnapshot core = CoreEntityReferenceSnapshot.Capture();
            List<EventState> values = new List<EventState>();
            HashSet<ulong> liveEntities = new HashSet<ulong>();
            int limit = manager.m_events.m_buffer.Length;
            if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                ushort native = (ushort)i;
                if (!Live(manager, native)) continue;
                EntityIdentityV2 identity;
                if (!context.TryGetIdentity(native, out identity))
                {
                    if (!context.IsAuthoritative)
                        throw new InvalidOperationException("Replica event has no Host-issued Forge identity.");
                    identity = context.GetOrAllocateIdentity(native);
                }
                liveEntities.Add(identity.EntityId);
                values.Add(CaptureOne(identity, native, core));
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
                if (!liveEntities.Contains(mappings[i].Identity.EntityId) && !context.RetireIdentity(mappings[i].Identity))
                    throw new InvalidOperationException("Could not retire a stale Event identity.");

            values.Sort(delegate(EventState a, EventState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return EncodeState(values);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            Check.NotNull(context, "context"); Check.NotNull(state, "state"); // WP-2: per-argument reporting
            EventManager manager = EventManager.instance;
            if (manager == null) throw new InvalidOperationException("EventManager is unavailable.");
            CoreEntityReferenceSnapshot core = CoreEntityReferenceSnapshot.Capture();
            EventState[] desired = DecodeState(state);
            HashSet<ulong> wanted = new HashSet<ulong>();

            for (int i = 0; i < desired.Length; i++)
            {
                EventState value = desired[i];
                wanted.Add(value.Identity.EntityId);
                ushort building = ResolveBuildingNative(core, value.Building);
                uint nativeValue;
                ushort native;
                if (context.TryGetNative(value.Identity, out nativeValue))
                {
                    if (nativeValue == 0 || nativeValue > ushort.MaxValue)
                        throw new InvalidOperationException("Event stable mapping exceeds ushort range.");
                    native = (ushort)nativeValue;
                    if (!Live(manager, native))
                        throw new InvalidOperationException("Event stable identity points to an empty local slot.");
                }
                else
                {
                    if (context.IsAuthoritative)
                        throw new InvalidOperationException("Host lost an Event stable identity mapping.");
                    native = FindUniqueUnmappedEvent(context, value.PrefabKey, building);
                    if (native == 0)
                    {
                        EventInfo info = PrefabCollection<EventInfo>.FindLoaded(value.PrefabKey);
                        if (info == null) throw new InvalidOperationException("Event prefab is not loaded: " + value.PrefabKey);
                        if (!manager.CreateEvent(out native, building, info) || native == 0)
                            throw new InvalidOperationException("CS1 could not materialize a replica Event slot.");
                    }
                    context.BindKnownIdentity(value.Identity, native);
                }
                Install(native, value, building);
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if (wanted.Contains(mapping.Identity.EntityId)) continue;
                if (mapping.NativeId == 0 || mapping.NativeId > ushort.MaxValue)
                    throw new InvalidOperationException("Event mapping exceeds ushort range.");
                ushort native = (ushort)mapping.NativeId;
                if (Live(manager, native)) manager.ReleaseEvent(native);
                if (!context.RetireIdentity(mapping.Identity))
                    throw new InvalidOperationException("Replica Event identity retirement failed.");
            }
        }

        public bool ExecuteIntent(IForgeAdapterContextV1 context, byte[] intent)
        {
            if (context == null || !context.IsAuthoritative || intent == null) return false;
            EventIntent request;
            try { request = DecodeIntent(intent); }
            catch { return false; }
            uint nativeValue;
            if (!context.TryGetNative(request.Target, out nativeValue) || nativeValue == 0 || nativeValue > ushort.MaxValue)
                return false;
            ushort native = (ushort)nativeValue;
            EventManager manager = EventManager.instance;
            if (!Live(manager, native)) return false;
            EventData data = manager.m_events.m_buffer[native];
            EventAI ai = data.Info == null ? null : data.Info.m_eventAI;
            if (ai == null) return false;
            switch (request.Kind)
            {
                case EventControlKind.SecurityBudget:
                    if (request.Value < 0) return false;
                    ai.SetSecurityBudget(native, ref data, request.Value);
                    break;
                case EventControlKind.TicketPrice:
                    if (request.Value < 0) return false;
                    ai.SetTicketPrice(native, ref data, request.Value);
                    break;
                case EventControlKind.Color:
                    ai.SetColor(native, ref data, request.Color);
                    break;
                case EventControlKind.Activate:
                    ai.Activate(native, ref data);
                    break;
                default:
                    return false;
            }
            manager.m_events.m_buffer[native] = data;
            return true;
        }

        internal static byte[] EncodeIntIntent(EventControlKind kind, EntityIdentityV2 target, int value)
        {
            return EncodeIntent(new EventIntent { Kind = kind, Target = target, Value = value, Color = new Color32(0, 0, 0, 0) });
        }

        internal static byte[] EncodeColorIntent(EntityIdentityV2 target, Color32 color)
        {
            return EncodeIntent(new EventIntent { Kind = EventControlKind.Color, Target = target, Value = 0, Color = color });
        }

        internal static byte[] EncodeActivateIntent(EntityIdentityV2 target)
        {
            return EncodeIntent(new EventIntent { Kind = EventControlKind.Activate, Target = target, Value = 0, Color = new Color32(0, 0, 0, 0) });
        }

        private static EventState CaptureOne(EntityIdentityV2 identity, ushort native, CoreEntityReferenceSnapshot core)
        {
            EventData data = EventManager.instance.m_events.m_buffer[native];
            EventInfo info = data.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Live event has no prefab identity.");
            EventAI ai = info.m_eventAI;
            if (ai == null) throw new InvalidOperationException("Live event has no AI.");
            EntityIdentityV2 building = default(EntityIdentityV2);
            if (data.m_building != 0 && !core.TryGetIdentity(BuildingAuthorityDomain.Id, data.m_building, out building))
                throw new InvalidOperationException("Event references a Building without a Forge stable identity.");
            return new EventState
            {
                Identity = identity,
                Building = building,
                PrefabKey = info.name,
                Flags = Convert.ToUInt64(data.m_flags),
                Color = data.m_color,
                SecurityBudget = ai.GetSecurityBudget(native, ref data),
                TicketPrice = ai.GetTicketPrice(native, ref data),
                CreatedFrame = data.m_createdFrame,
                CustomSeed = data.m_customSeed,
                ExpireFrame = data.m_expireFrame,
                FailureCount = data.m_failureCount,
                PopularityDelta = data.m_popularityDelta,
                RewardMoney = data.m_rewardMoney,
                StartFrame = data.m_startFrame,
                SuccessCount = data.m_successCount,
                TicketMoney = data.m_ticketMoney,
                TotalReward = data.m_totalReward,
                TotalTicket = data.m_totalTicket
            };
        }

        private static void Install(ushort native, EventState value, ushort building)
        {
            EventManager manager = EventManager.instance;
            EventData data = manager.m_events.m_buffer[native];
            EventInfo info = data.Info;
            if (info == null || info.name != value.PrefabKey)
                throw new InvalidOperationException("Event stable identity prefab validation failed.");
            EventAI ai = info.m_eventAI;
            if (ai == null) throw new InvalidOperationException("Event AI is unavailable.");
            ai.SetSecurityBudget(native, ref data, value.SecurityBudget);
            ai.SetTicketPrice(native, ref data, value.TicketPrice);
            ai.SetColor(native, ref data, value.Color);
            data.m_building = building;
            data.m_createdFrame = value.CreatedFrame;
            data.m_customSeed = value.CustomSeed;
            data.m_expireFrame = value.ExpireFrame;
            data.m_failureCount = value.FailureCount;
            data.m_popularityDelta = value.PopularityDelta;
            data.m_rewardMoney = value.RewardMoney;
            data.m_startFrame = value.StartFrame;
            data.m_successCount = value.SuccessCount;
            data.m_ticketMoney = value.TicketMoney;
            data.m_ticketPrice = checked((ushort)value.TicketPrice);
            data.m_totalReward = value.TotalReward;
            data.m_totalTicket = value.TotalTicket;
            data.m_flags = (EventData.Flags)value.Flags;
            manager.m_events.m_buffer[native] = data;
        }

        private static ushort ResolveBuildingNative(CoreEntityReferenceSnapshot core, EntityIdentityV2 building)
        {
            if (!building.IsValid) return 0;
            uint native;
            if (!core.TryGetNative(BuildingAuthorityDomain.Id, building, out native) || native == 0 || native > ushort.MaxValue)
                throw new InvalidOperationException("Event Building stable identity is unavailable on this replica.");
            return (ushort)native;
        }

        private static ushort FindUniqueUnmappedEvent(IForgeAdapterContextV1 context, string prefabKey, ushort building)
        {
            EventManager manager = EventManager.instance;
            ushort found = 0;
            int limit = manager.m_events.m_buffer.Length;
            if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                ushort native = (ushort)i;
                if (!Live(manager, native)) continue;
                EventData data = manager.m_events.m_buffer[native];
                if (data.Info == null || data.Info.name != prefabKey || data.m_building != building) continue;
                EntityIdentityV2 existing;
                if (context.TryGetIdentity(native, out existing)) continue;
                if (found != 0) return 0;
                found = native;
            }
            return found;
        }

        private static bool Live(EventManager manager, ushort native)
        {
            if (manager == null || native == 0 || native >= manager.m_events.m_buffer.Length) return false;
            EventData data = manager.m_events.m_buffer[native];
            return data.m_flags != EventData.Flags.None && data.Info != null;
        }

        private static byte[] EncodeState(List<EventState> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(StateMagic); writer.Write(values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    EventState value = values[i];
                    writer.Write(value.Identity.EntityId); writer.Write(value.Identity.Generation);
                    writer.Write(value.Building.IsValid ? value.Building.EntityId : 0UL);
                    writer.Write(value.Building.IsValid ? value.Building.Generation : 0U);
                    WriteString(writer, value.PrefabKey);
                    writer.Write(value.Flags);
                    writer.Write(value.Color.r); writer.Write(value.Color.g); writer.Write(value.Color.b); writer.Write(value.Color.a);
                    writer.Write(value.SecurityBudget); writer.Write(value.TicketPrice);
                    writer.Write(value.CreatedFrame); writer.Write(value.CustomSeed); writer.Write(value.ExpireFrame);
                    writer.Write(value.FailureCount); writer.Write(value.PopularityDelta); writer.Write(value.RewardMoney);
                    writer.Write(value.StartFrame); writer.Write(value.SuccessCount); writer.Write(value.TicketMoney);
                    writer.Write(value.TotalReward); writer.Write(value.TotalTicket);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Event absolute state exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static EventState[] DecodeState(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid event state size.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != StateMagic) throw new InvalidDataException("Invalid event state magic.");
                int count = reader.ReadInt32();
                if (count < 0 || count > 4096) throw new InvalidDataException("Invalid event state count.");
                EventState[] result = new EventState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("Event state is not canonically ordered.");
                    previous = identity.EntityId;
                    ulong buildingId = reader.ReadUInt64();
                    uint buildingGeneration = reader.ReadUInt32();
                    EntityIdentityV2 building = buildingId == 0 && buildingGeneration == 0
                        ? default(EntityIdentityV2) : new EntityIdentityV2(buildingId, buildingGeneration);
                    EventState value = new EventState
                    {
                        Identity = identity,
                        Building = building,
                        PrefabKey = ReadString(reader),
                        Flags = reader.ReadUInt64(),
                        Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                        SecurityBudget = reader.ReadInt32(),
                        TicketPrice = reader.ReadInt32(),
                        CreatedFrame = reader.ReadUInt32(),
                        CustomSeed = reader.ReadUInt64(),
                        ExpireFrame = reader.ReadUInt32(),
                        FailureCount = reader.ReadUInt16(),
                        PopularityDelta = reader.ReadInt16(),
                        RewardMoney = reader.ReadUInt64(),
                        StartFrame = reader.ReadUInt32(),
                        SuccessCount = reader.ReadUInt16(),
                        TicketMoney = reader.ReadUInt64(),
                        TotalReward = reader.ReadUInt64(),
                        TotalTicket = reader.ReadUInt64()
                    };
                    result[i] = value;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing event state bytes.");
                return result;
            }
        }

        private static byte[] EncodeIntent(EventIntent request)
        {
            if (!request.Target.IsValid) throw new ArgumentException("Event control target is missing.");
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(IntentMagic); writer.Write((byte)request.Kind);
                writer.Write(request.Target.EntityId); writer.Write(request.Target.Generation); writer.Write(request.Value);
                writer.Write(request.Color.r); writer.Write(request.Color.g); writer.Write(request.Color.b); writer.Write(request.Color.a);
                writer.Flush(); return stream.ToArray();
            }
        }

        private static EventIntent DecodeIntent(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != IntentMagic) throw new InvalidDataException("Invalid event intent magic.");
                EventControlKind kind = (EventControlKind)reader.ReadByte();
                if (kind < EventControlKind.SecurityBudget || kind > EventControlKind.Activate) throw new InvalidDataException("Invalid event intent kind.");
                EventIntent result = new EventIntent
                {
                    Kind = kind,
                    Target = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()),
                    Value = reader.ReadInt32(),
                    Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte())
                };
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing event intent bytes.");
                return result;
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length == 0 || bytes.Length > 255) throw new InvalidDataException("Invalid event prefab key.");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadByte();
            if (length == 0) throw new InvalidDataException("Invalid event prefab key length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class EventState
        {
            public EntityIdentityV2 Identity;
            public EntityIdentityV2 Building;
            public string PrefabKey;
            public ulong Flags;
            public Color32 Color;
            public int SecurityBudget;
            public int TicketPrice;
            public uint CreatedFrame;
            public ulong CustomSeed;
            public uint ExpireFrame;
            public ushort FailureCount;
            public short PopularityDelta;
            public ulong RewardMoney;
            public uint StartFrame;
            public ushort SuccessCount;
            public ulong TicketMoney;
            public ulong TotalReward;
            public ulong TotalTicket;
        }

        private sealed class EventIntent
        {
            public EventControlKind Kind;
            public EntityIdentityV2 Target;
            public int Value;
            public Color32 Color;
        }
    }
}
