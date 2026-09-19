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
        MoveStop = 5,
        Create = 6
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
            Check.Condition(!entity.IsValid, "entity", "Invalid transport line identity.");
            ValidateDraft(prefabKey, stops);
            Entity = entity; PrefabKey = prefabKey;
            Red = red; Green = green; Blue = blue; Alpha = alpha;
            Budget = budget; TicketPrice = ticketPrice; Day = day; Night = night; Complete = complete;
            Stops = (TransportStopV2[])stops.Clone();
        }

        internal static void ValidateDraft(string prefabKey, TransportStopV2[] stops)
        {
            Check.Condition(string.IsNullOrEmpty(prefabKey) || prefabKey.Length > 160, "prefabKey", "Invalid transport prefab key.");
            Check.Condition(stops == null || stops.Length > 512, "stops", "Transport stop list exceeds supported bounds.");
            for (int i = 0; i < stops.Length; i++) if (stops[i] == null) throw new ArgumentException("Null transport stop.", "stops");
        }
    }

    public sealed class TransportLineIntentV2
    {
        public TransportLineIntentKindV2 Kind { get; private set; }
        public EntityIdentityV2 Target { get; private set; }
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
        public int StopIndex { get; private set; }
        public TransportStopV2 Stop { get; private set; }

        public TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night)
            : this(kind, target, null, red, green, blue, alpha, budget, ticketPrice, day, night, false,
                new TransportStopV2[0], -1, null) { }

        public TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target, int stopIndex, TransportStopV2 stop)
            : this(kind, target, null, 0, 0, 0, 0, 0, 0, false, false, false,
                new TransportStopV2[0], stopIndex, stop) { }

        public static TransportLineIntentV2 CreateLine(string prefabKey,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice,
            bool day, bool night, bool complete, TransportStopV2[] stops)
        {
            TransportLineStateV2.ValidateDraft(prefabKey, stops);
            return new TransportLineIntentV2(TransportLineIntentKindV2.Create, default(EntityIdentityV2), prefabKey,
                red, green, blue, alpha, budget, ticketPrice, day, night, complete,
                (TransportStopV2[])stops.Clone(), -1, null);
        }

        private TransportLineIntentV2(TransportLineIntentKindV2 kind, EntityIdentityV2 target, string prefabKey,
            byte red, byte green, byte blue, byte alpha, ushort budget, ushort ticketPrice, bool day, bool night,
            bool complete, TransportStopV2[] stops, int stopIndex, TransportStopV2 stop)
        {
            if (kind < TransportLineIntentKindV2.SetProperties || kind > TransportLineIntentKindV2.Create)
                throw new ArgumentOutOfRangeException("kind");
            if (kind == TransportLineIntentKindV2.Create)
            {
                Check.Condition(target.IsValid, "target", "Create intent must not carry a target identity.");
                TransportLineStateV2.ValidateDraft(prefabKey, stops);
            }
            else if (!target.IsValid) throw new ArgumentException("Invalid transport line target.", "target");
            bool route = kind == TransportLineIntentKindV2.AddStop || kind == TransportLineIntentKindV2.RemoveStop || kind == TransportLineIntentKindV2.MoveStop;
            Check.OutOfRange(route && stopIndex < -1, "stopIndex");
            if ((kind == TransportLineIntentKindV2.AddStop || kind == TransportLineIntentKindV2.MoveStop) && stop == null)
                throw new ArgumentNullException("stop");
            Kind = kind; Target = target; PrefabKey = prefabKey;
            Red = red; Green = green; Blue = blue; Alpha = alpha;
            Budget = budget; TicketPrice = ticketPrice; Day = day; Night = night; Complete = complete;
            Stops = stops == null ? new TransportStopV2[0] : (TransportStopV2[])stops.Clone();
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
            Check.NotNull(value, "value");
            if (values.ContainsKey(value.Entity.EntityId)) throw new InvalidOperationException("Duplicate transport line identity.");
            values.Add(value.Entity.EntityId, value);
        }

        public void Apply(TransportLineMutationV2 mutation)
        {
            Check.NotNull(mutation, "mutation");
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

        internal static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadByte(); byte[] bytes = reader.ReadBytes(length);
            if (length == 0 || bytes.Length != length) throw new InvalidDataException("Truncated transport string.");
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        internal static void WriteStops(BinaryWriter writer, bool complete, TransportStopV2[] stops)
        {
            writer.Write((byte)(complete ? 1 : 0)); writer.Write((ushort)stops.Length);
            for (int i = 0; i < stops.Length; i++)
            {
                TransportStopV2 stop = stops[i]; writer.Write(stop.X); writer.Write(stop.Y); writer.Write(stop.Z); writer.Write((byte)(stop.FixedPlatform ? 1 : 0));
            }
        }

        internal static TransportStopV2[] ReadStops(BinaryReader reader, out bool complete)
        {
            byte completeByte = reader.ReadByte(); if (completeByte > 1) throw new InvalidDataException("Invalid transport complete flag.");
            complete = completeByte == 1; ushort count = reader.ReadUInt16();
            if (count > 512) throw new InvalidDataException("Transport stop list exceeds supported bounds.");
            TransportStopV2[] stops = new TransportStopV2[count];
            for (int i = 0; i < stops.Length; i++)
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle(); byte fixedPlatform = reader.ReadByte();
                if (fixedPlatform > 1) throw new InvalidDataException("Invalid transport stop fixed-platform flag.");
                stops[i] = new TransportStopV2(x, y, z, fixedPlatform == 1);
            }
            return stops;
        }

        internal static void WriteState(BinaryWriter writer, TransportLineStateV2 value)
        {
            writer.Write(value.Entity.EntityId); writer.Write(value.Entity.Generation); WriteString(writer, value.PrefabKey);
            writer.Write(value.Red); writer.Write(value.Green); writer.Write(value.Blue); writer.Write(value.Alpha);
            writer.Write(value.Budget); writer.Write(value.TicketPrice); writer.Write((byte)(value.Day ? 1 : 0)); writer.Write((byte)(value.Night ? 1 : 0));
            WriteStops(writer, value.Complete, value.Stops);
        }
    }

    public static class TransportLineDomainCodecV2
    {
        private const uint MutationMagic = 0x324D5446u; // FTM2

        public static byte[] EncodeIntent(TransportLineIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.Kind);
                if (value.Kind != TransportLineIntentKindV2.Create)
                {
                    writer.Write(value.Target.EntityId); writer.Write(value.Target.Generation);
                }
                if (value.Kind == TransportLineIntentKindV2.SetProperties)
                {
                    WriteProperties(writer, value.Red, value.Green, value.Blue, value.Alpha, value.Budget, value.TicketPrice, value.Day, value.Night);
                }
                else if (value.Kind == TransportLineIntentKindV2.AddStop || value.Kind == TransportLineIntentKindV2.MoveStop)
                {
                    writer.Write(value.StopIndex); writer.Write(value.Stop.X); writer.Write(value.Stop.Y); writer.Write(value.Stop.Z);
                    writer.Write((byte)(value.Stop.FixedPlatform ? 1 : 0));
                }
                else if (value.Kind == TransportLineIntentKindV2.RemoveStop) writer.Write(value.StopIndex);
                else if (value.Kind == TransportLineIntentKindV2.Create)
                {
                    TransportLineStateIndexV2.WriteString(writer, value.PrefabKey);
                    WriteProperties(writer, value.Red, value.Green, value.Blue, value.Alpha, value.Budget, value.TicketPrice, value.Day, value.Night);
                    TransportLineStateIndexV2.WriteStops(writer, value.Complete, value.Stops);
                }
                writer.Flush(); byte[] result = stream.ToArray();
                if (result.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Transport intent exceeds frame payload budget.");
                return result;
            }
        }

        public static TransportLineIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid transport intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                TransportLineIntentKindV2 kind = (TransportLineIntentKindV2)reader.ReadByte();
                if (kind == TransportLineIntentKindV2.Create)
                {
                    string prefab = TransportLineStateIndexV2.ReadString(reader);
                    byte r, g, b, a, day, night; ushort budget, ticket;
                    ReadProperties(reader, out r, out g, out b, out a, out budget, out ticket, out day, out night);
                    bool complete; TransportStopV2[] stops = TransportLineStateIndexV2.ReadStops(reader, out complete);
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing transport create bytes.");
                    return TransportLineIntentV2.CreateLine(prefab, r, g, b, a, budget, ticket, day == 1, night == 1, complete, stops);
                }
                EntityIdentityV2 target = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                TransportLineIntentV2 result;
                if (kind == TransportLineIntentKindV2.SetProperties)
                {
                    byte r, g, b, a, day, night; ushort budget, ticket;
                    ReadProperties(reader, out r, out g, out b, out a, out budget, out ticket, out day, out night);
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
            Check.NotNull(value, "value");
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

        private static void WriteProperties(BinaryWriter writer, byte r, byte g, byte b, byte a, ushort budget, ushort ticket, bool day, bool night)
        {
            writer.Write(r); writer.Write(g); writer.Write(b); writer.Write(a); writer.Write(budget); writer.Write(ticket);
            writer.Write((byte)(day ? 1 : 0)); writer.Write((byte)(night ? 1 : 0));
        }

        private static void ReadProperties(BinaryReader reader, out byte r, out byte g, out byte b, out byte a,
            out ushort budget, out ushort ticket, out byte day, out byte night)
        {
            r = reader.ReadByte(); g = reader.ReadByte(); b = reader.ReadByte(); a = reader.ReadByte();
            budget = reader.ReadUInt16(); ticket = reader.ReadUInt16(); day = reader.ReadByte(); night = reader.ReadByte();
            if (day > 1 || night > 1) throw new InvalidDataException("Invalid transport active flag.");
        }

        private static TransportLineStateV2 ReadState(BinaryReader reader)
        {
            EntityIdentityV2 entity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
            string prefab = TransportLineStateIndexV2.ReadString(reader);
            byte r, g, b, a, day, night; ushort budget, ticket;
            ReadProperties(reader, out r, out g, out b, out a, out budget, out ticket, out day, out night);
            bool complete; TransportStopV2[] stops = TransportLineStateIndexV2.ReadStops(reader, out complete);
            return new TransportLineStateV2(entity, prefab, r, g, b, a, budget, ticket, day == 1, night == 1, complete, stops);
        }
    }
}
