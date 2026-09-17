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
        Color = 3
    }

    /// <summary>
    /// Absolute control/result state for existing CS1 events. Event native ushort slots are local;
    /// the wire carries only Forge EntityIdentityV2 plus a prefab validation key.
    /// </summary>
    internal sealed class EventStateAdapter : IForgeInteractiveStateAdapterV1
    {
        private const uint StateMagic = 0x31564546u; // FEV1
        private const uint IntentMagic = 0x31494546u; // FEI1
        internal const string Adapter = "builtin.events";

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            if (context == null) throw new ArgumentNullException("context");
            EventManager manager = EventManager.instance;
            if (manager == null) throw new InvalidOperationException("EventManager is unavailable.");
            if (context.IsAuthoritative) SeedHostMappings(context, manager);
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            List<EventState> values = new List<EventState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                if (mappings[i].NativeId == 0 || mappings[i].NativeId > ushort.MaxValue) continue;
                ushort native = (ushort)mappings[i].NativeId;
                if (!Live(manager, native)) continue;
                values.Add(CaptureOne(mappings[i].Identity, native));
            }
            values.Sort(delegate(EventState a, EventState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return EncodeState(values);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            EventManager manager = EventManager.instance;
            if (manager == null) throw new InvalidOperationException("EventManager is unavailable.");
            EventState[] values = DecodeState(state);
            for (int i = 0; i < values.Length; i++)
            {
                EventState value = values[i];
                uint nativeValue;
                ushort native;
                if (!context.TryGetNative(value.Identity, out nativeValue))
                {
                    if (context.IsAuthoritative) throw new InvalidOperationException("Host event identity map changed outside the authority path.");
                    native = FindUniqueUnmappedEvent(context, value.PrefabKey);
                    if (native == 0) throw new InvalidOperationException("Replica could not resolve a unique existing event for Host stable identity.");
                    context.BindKnownIdentity(value.Identity, native);
                }
                else
                {
                    if (nativeValue == 0 || nativeValue > ushort.MaxValue) throw new InvalidOperationException("Event stable mapping exceeds ushort range.");
                    native = (ushort)nativeValue;
                }
                if (!Live(manager, native)) throw new InvalidOperationException("Event projection target is not live.");
                EventData data = manager.m_events.m_buffer[native];
                EventInfo info = data.Info;
                if (info == null || info.name != value.PrefabKey) throw new InvalidOperationException("Event stable identity prefab validation failed.");
                EventAI ai = info.m_eventAI;
                if (ai == null) throw new InvalidOperationException("Event AI is unavailable.");
                ai.SetSecurityBudget(native, ref data, value.SecurityBudget);
                ai.SetTicketPrice(native, ref data, value.TicketPrice);
                ai.SetColor(native, ref data, value.Color);
                data.m_flags = (EventData.Flags)value.Flags;
                manager.m_events.m_buffer[native] = data;
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

        private static void SeedHostMappings(IForgeAdapterContextV1 context, EventManager manager)
        {
            int limit = manager.m_events.m_buffer.Length;
            if (limit > ushort.MaxValue + 1) limit = ushort.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                ushort native = (ushort)i;
                if (!Live(manager, native)) continue;
                EntityIdentityV2 identity;
                if (!context.TryGetIdentity(native, out identity)) context.GetOrAllocateIdentity(native);
            }
        }

        private static EventState CaptureOne(EntityIdentityV2 identity, ushort native)
        {
            EventData data = EventManager.instance.m_events.m_buffer[native];
            EventInfo info = data.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Live event has no prefab identity.");
            EventAI ai = info.m_eventAI;
            if (ai == null) throw new InvalidOperationException("Live event has no AI.");
            return new EventState
            {
                Identity = identity,
                PrefabKey = info.name,
                Flags = Convert.ToUInt64(data.m_flags),
                Color = data.m_color,
                SecurityBudget = ai.GetSecurityBudget(native, ref data),
                TicketPrice = ai.GetTicketPrice(native, ref data)
            };
        }

        private static ushort FindUniqueUnmappedEvent(IForgeAdapterContextV1 context, string prefabKey)
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
                if (data.Info == null || data.Info.name != prefabKey) continue;
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
                    WriteString(writer, value.PrefabKey);
                    writer.Write(value.Flags);
                    writer.Write(value.Color.r); writer.Write(value.Color.g); writer.Write(value.Color.b); writer.Write(value.Color.a);
                    writer.Write(value.SecurityBudget); writer.Write(value.TicketPrice);
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
                    result[i] = new EventState
                    {
                        Identity = identity,
                        PrefabKey = ReadString(reader),
                        Flags = reader.ReadUInt64(),
                        Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                        SecurityBudget = reader.ReadInt32(),
                        TicketPrice = reader.ReadInt32()
                    };
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
                if (kind < EventControlKind.SecurityBudget || kind > EventControlKind.Color) throw new InvalidDataException("Invalid event intent kind.");
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
            public string PrefabKey;
            public ulong Flags;
            public Color32 Color;
            public int SecurityBudget;
            public int TicketPrice;
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
