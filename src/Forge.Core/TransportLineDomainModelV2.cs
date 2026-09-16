using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public enum TransportLineIntentKindV2 : byte
    {
        SetProperties = 1,
        Release = 2,
        AddStop = 3,
        RemoveStop = 4,
        MoveStop = 5
    }

    public sealed class TransportStopV2
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public bool FixedPlatform { get; private set; }
        public TransportStopV2(float x, float y, float z, bool fixedPlatform)
        {
            Check(x); Check(y); Check(z); X = x; Y = y; Z = z; FixedPlatform = fixedPlatform;
        }
        private static void Check(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Transport stop position must be finite.");
        }
    }

    public sealed class TransportLineStateV2
    {
        public EntityIdentityV2 Entity { get; private set; }
        public string PrefabKey { get; private set; }
        public byte Red { get; private set; }
        public byte Green { get; private set; }
        public byte Blue { get; private set; }
        public byte Alpha { get; private set; }
        public ushort Budget { get; private set; }
        public ushort TicketPrice { get; private set; }
        public bool Day { get; private set; }
        public bool Night { get; private set; }
        public bool Complete { get; private set; }
        public TransportStopV2[] Stops { get; private set; }

        public TransportLineStateV2(EntityIdentityV2 entity, string prefabKey,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night)
            : this(entity, prefabKey, red, green, blue, alpha, budget, ticketPrice, day, night, false, new TransportStopV2[0]) { }

        public TransportLineStateV2(EntityIdentityV2 entity, string prefabKey,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night,
            bool complete, TransportStopV2[] stops)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid transport line identity.", "entity");
            if (string.IsNullOrEmpty(prefabKey) || prefabKey.Length > 160) throw new ArgumentException("Invalid transport prefab key.", "prefabKey");
            if (stops == null || stops.Length > 512) throw new ArgumentException("Transport stop list exceeds supported bounds.", "stops");
            Entity = entity; PrefabKey = prefabKey;
            Red = red; Green = green; Blue = blue; Alpha = alpha;
            Budget = budget; TicketPrice = ticketPrice; Day = day; Night = night; Complete = complete;
            Stops = (TransportStopV2[])stops.Clone();
            for (int i = 0; i < Stops.Length; i++) if (Stops[i] == null) throw new ArgumentException("Null transport stop.", "stops");
        }
    }

    public sealed class TransportLineIntentV2
    {
        public TransportLineIntentKindV2 Kind { get; private set; }
        public EntityIdentityV2 Target { get; private set; }
        public byte Red { get; private set; }
        public byte Green { get; private set; }
        public byte Blue { get; private set; }
        public byte Alpha { get; private set; }
        public ushort Budget { get; private set; }
        public ushort TicketPrice { get; private set; }
        public bool Day { get; private set; }
        public bool Night { get; private set; }
        public int StopIndex { get; private set; }
        public TransportStopV2 Stop { get; private set; }

        public TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night)
            : this(kind, target, red, green, blue, alpha, budget, ticketPrice, day, night, -1, null) { }

        public TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target, int stopIndex, TransportStopV2 stop)
            : this(kind, target, 0, 0, 0, 0, 0, 0, false, false, stopIndex, stop) { }

        private TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night,
            int stopIndex, TransportStopV2 stop)
        {
            if (kind < TransportLineIntentKindV2.SetProperties || kind > TransportLineIntentKindV2.MoveStop)
                throw new ArgumentOutOfRangeException("kind");
            if (!target.IsValid) throw new ArgumentException("Invalid transport line target.", "target");
            bool route = kind == TransportLineIntentKindV2.AddStop || kind == TransportLineIntentKindV2.RemoveStop || kind == TransportLineIntentKindV2.MoveStop;
            if (route && stopIndex < -1) throw new ArgumentOutOfRangeException("stopIndex");
            if ((kind == TransportLineIntentKindV2.AddStop || kind == TransportLineIntentKindV2.MoveStop) && stop == null)
                throw new ArgumentNullException("stop");
            Kind = kind; Target = target;
            Red = red; Green = green; Blue = blue; Alpha = alpha;
            Budget = budget; TicketPrice = ticketPrice; Day = day; Night = night;
            StopIndex = stopIndex; Stop = stop;
        }
    }

    public sealed class TransportLineMutationV2
    {
        public TransportLineStateV2[] Upserts { get; private set; }
        public EntityIdentityV2[] Deletes { get; private set; }
        public int Count { get { return Upserts.Length + Deletes.Length; } }
        public TransportLineMutationV2(TransportLineStateV2[] upserts, EntityIdentityV2[] deletes)
        {
            if (upserts == null || deletes == null) throw new ArgumentNullException("transport mutation arrays");
            if (upserts.Length > 1024 || deletes.Length > 1024) throw new ArgumentException("Transport mutation exceeds supported bounds.");
            Upserts = (TransportLineStateV2[])upserts.Clone(); Deletes = (EntityIdentityV2[])deletes.Clone();
            for (int i = 0; i < Upserts.Length; i++) if (Upserts[i] == null) throw new ArgumentException("Null transport line state.");
            for (int i = 0; i < Deletes.Length; i++) if (!Deletes[i].IsValid) throw new ArgumentException("Invalid transport deletion identity.");
        }
    }

    public sealed class TransportLineStateIndexV2
    {
        private readonly SortedDictionary<ulong, TransportLineStateV2> values = new SortedDictionary<ulong, TransportLineStateV2>();
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }
        public int Count { get { return values.Count; } }

        public void Seed(TransportLineStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (values.ContainsKey(value.Entity.EntityId)) throw new InvalidOperationException("Duplicate transport line identity.");
            values.Add(value.Entity.EntityId, value);
        }

        public void Apply(TransportLineMutationV2 mutation)
        {
            if (mutation == null) throw new ArgumentNullException("mutation");
            for (int i = 0; i < mutation.Upserts.Length; i++)
            {
                TransportLineStateV2 value = mutation.Upserts[i]; TransportLineStateV2 current;
                if (values.TryGetValue(value.Entity.EntityId, out current) && !current.Entity.Equals(value.Entity))
                    throw new InvalidOperationException("Transport line generation conflict.");
                values[value.Entity.EntityId] = value;
            }
            for (int i = 0; i < mutation.Deletes.Length; i++)
            {
                EntityIdentityV2 id = mutation.Deletes[i]; TransportLineStateV2 current;
                if (!values.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id))
                    throw new InvalidOperationException("Cannot delete unknown transport line.");
                values.Remove(id.EntityId);
            }
        }

        public bool TryGet(EntityIdentityV2 id, out TransportLineStateV2 value)
        {
            value = null; TransportLineStateV2 current;
            if (!id.IsValid || !values.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id)) return false;
            value = current; return true;
        }

        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(0x32544C46u); // FLT2
                writer.Write((uint)values.Count);
                foreach (TransportLineStateV2 value in values.Values) WriteState(writer, value);
                writer.Flush(); return stream.ToArray();
            }
        }

        internal static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            if (bytes.Length == 0 || bytes.Length > 255) throw new InvalidDataException("Transport prefab key is outside wire bounds.");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }

        internal static void WriteState(BinaryWriter writer, TransportLineStateV2 value)
        {
            writer.Write(value.Entity.EntityId); writer.Write(value.Entity.Generation); WriteString(writer, value.PrefabKey);
            writer.Write(value.Red); writer.Write(value.Green); writer.Write(value.Blue); writer.Write(value.Alpha);
            writer.Write(value.Budget); writer.Write(value.TicketPrice); writer.Write((byte)(value.Day ? 1 : 0)); writer.Write((byte)(value.Night ? 1 : 0));
            writer.Write((byte)(value.Complete ? 1 : 0)); writer.Write((ushort)value.Stops.Length);
            for (int i = 0; i < value.Stops.Length; i++)
            {
                TransportStopV2 stop = value.Stops[i]; writer.Write(stop.X); writer.Write(stop.Y); writer.Write(stop.Z); writer.Write((byte)(stop.FixedPlatform ? 1 : 0));
            }
        }
    }

    public static class TransportLineDomainCodecV2
    {
        private const uint MutationMagic = 0x324D5446u; // FTM2

        public static byte[] EncodeIntent(TransportLineIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.Kind);
                writer.Write(value.Target.EntityId); writer.Write(value.Target.Generation);
                if (value.Kind == TransportLineIntentKindV2.SetProperties)
                {
                    writer.Write(value.Red); writer.Write(value.Green); writer.Write(value.Blue); writer.Write(value.Alpha);
                    writer.Write(value.Budget); writer.Write(value.TicketPrice);
                    writer.Write((byte)(value.Day ? 1 : 0)); writer.Write((byte)(value.Night ? 1 : 0));
                }
                else if (value.Kind == TransportLineIntentKindV2.AddStop || value.Kind == TransportLineIntentKindV2.MoveStop)
                {
                    writer.Write(value.StopIndex); writer.Write(value.Stop.X); writer.Write(value.Stop.Y); writer.Write(value.Stop.Z);
                    writer.Write((byte)(value.Stop.FixedPlatform ? 1 : 0));
                }
                else if (value.Kind == TransportLineIntentKindV2.RemoveStop) writer.Write(value.StopIndex);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static TransportLineIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 13 || bytes.Length > 34) throw new InvalidDataException("Invalid transport intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                TransportLineIntentKindV2 kind = (TransportLineIntentKindV2)reader.ReadByte();
                EntityIdentityV2 target = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                TransportLineIntentV2 result;
                if (kind == TransportLineIntentKindV2.SetProperties)
                {
                    byte r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte(), a = reader.ReadByte();
                    ushort budget = reader.ReadUInt16(), ticket = reader.ReadUInt16(); byte day = reader.ReadByte(), night = reader.ReadByte();
                    if (day > 1 || night > 1) throw new InvalidDataException("Invalid transport active flag.");
                    result = new TransportLineIntentV2(kind, target, r, g, b, a, budget, ticket, day == 1, night == 1);
                }
                else if (kind == TransportLineIntentKindV2.Release) result = new TransportLineIntentV2(kind, target, 0, 0, 0, 0, 0, 0, false, false);
                else if (kind == TransportLineIntentKindV2.RemoveStop) result = new TransportLineIntentV2(kind, target, reader.ReadInt32(), null);
                else if (kind == TransportLineIntentKindV2.AddStop || kind == TransportLineIntentKindV2.MoveStop)
                {
                    int index = reader.ReadInt32(); float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle(); byte fixedPlatform = reader.ReadByte();
                    if (fixedPlatform > 1) throw new InvalidDataException("Invalid fixed-platform flag.");
                    result = new TransportLineIntentV2(kind, target, index, new TransportStopV2(x, y, z, fixedPlatform == 1));
                }
                else throw new InvalidDataException("Unknown transport intent kind.");
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing transport intent bytes.");
                return result;
            }
        }

        public static byte[] EncodeMutation(TransportLineMutationV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(MutationMagic);
                writer.Write((ushort)value.Upserts.Length); writer.Write((ushort)value.Deletes.Length);
                for (int i = 0; i < value.Upserts.Length; i++) TransportLineStateIndexV2.WriteState(writer, value.Upserts[i]);
                for (int i = 0; i < value.Deletes.Length; i++) { writer.Write(value.Deletes[i].EntityId); writer.Write(value.Deletes[i].Generation); }
                writer.Flush(); byte[] result = stream.ToArray();
                if (result.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Transport mutation exceeds frame payload budget.");
                return result;
            }
        }

        public static TransportLineMutationV2 DecodeMutation(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid transport mutation length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != MutationMagic) throw new InvalidDataException("Unknown transport mutation magic.");
                ushort upsertCount = reader.ReadUInt16(), deleteCount = reader.ReadUInt16();
                if (upsertCount > 1024 || deleteCount > 1024) throw new InvalidDataException("Transport mutation exceeds supported bounds.");
                TransportLineStateV2[] upserts = new TransportLineStateV2[upsertCount];
                EntityIdentityV2[] deletes = new EntityIdentityV2[deleteCount];
                for (int i = 0; i < upserts.Length; i++) upserts[i] = ReadState(reader);
                for (int i = 0; i < deletes.Length; i++) deletes[i] = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing transport bytes.");
                return new TransportLineMutationV2(upserts, deletes);
            }
        }

        private static TransportLineStateV2 ReadState(BinaryReader reader)
        {
            EntityIdentityV2 entity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
            int length = reader.ReadByte(); byte[] text = reader.ReadBytes(length);
            if (text.Length != length || length == 0) throw new InvalidDataException("Truncated transport prefab key.");
            string prefab = System.Text.Encoding.UTF8.GetString(text);
            byte r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte(), a = reader.ReadByte();
            ushort budget = reader.ReadUInt16(), ticket = reader.ReadUInt16(); byte day = reader.ReadByte(), night = reader.ReadByte(), complete = reader.ReadByte();
            if (day > 1 || night > 1 || complete > 1) throw new InvalidDataException("Invalid transport flags.");
            ushort count = reader.ReadUInt16(); if (count > 512) throw new InvalidDataException("Transport stop list exceeds supported bounds.");
            TransportStopV2[] stops = new TransportStopV2[count];
            for (int i = 0; i < stops.Length; i++)
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle(); byte fixedPlatform = reader.ReadByte();
                if (fixedPlatform > 1) throw new InvalidDataException("Invalid transport stop fixed-platform flag.");
                stops[i] = new TransportStopV2(x, y, z, fixedPlatform == 1);
            }
            return new TransportLineStateV2(entity, prefab, r, g, b, a, budget, ticket, day == 1, night == 1, complete == 1, stops);
        }
    }
}
