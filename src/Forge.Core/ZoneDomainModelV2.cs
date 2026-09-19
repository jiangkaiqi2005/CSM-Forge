using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public struct ZoneBlockKeyV2 : IEquatable<ZoneBlockKeyV2>, IComparable<ZoneBlockKeyV2>
    {
        public readonly EntityIdentityV2 Segment;
        public readonly int PositionX16;
        public readonly int PositionZ16;
        public readonly int Angle4096;

        public ZoneBlockKeyV2(EntityIdentityV2 segment, int positionX16, int positionZ16, int angle4096)
        {
            Check.Condition(!segment.IsValid, "segment", "Invalid zoning segment identity.");
            Segment = segment;
            PositionX16 = positionX16;
            PositionZ16 = positionZ16;
            Angle4096 = angle4096;
        }

        public bool IsValid { get { return Segment.IsValid; } }
        public bool Equals(ZoneBlockKeyV2 other)
        {
            return Segment.Equals(other.Segment) && PositionX16 == other.PositionX16 &&
                PositionZ16 == other.PositionZ16 && Angle4096 == other.Angle4096;
        }
        public override bool Equals(object obj) { return obj is ZoneBlockKeyV2 && Equals((ZoneBlockKeyV2)obj); }
        public override int GetHashCode()
        {
            return Segment.GetHashCode() ^ PositionX16 ^ (PositionZ16 * 397) ^ (Angle4096 * 7919);
        }
        public int CompareTo(ZoneBlockKeyV2 other)
        {
            int value = Segment.EntityId.CompareTo(other.Segment.EntityId); if (value != 0) return value;
            value = Segment.Generation.CompareTo(other.Segment.Generation); if (value != 0) return value;
            value = PositionX16.CompareTo(other.PositionX16); if (value != 0) return value;
            value = PositionZ16.CompareTo(other.PositionZ16); if (value != 0) return value;
            return Angle4096.CompareTo(other.Angle4096);
        }
        public override string ToString()
        {
            return Segment + "@" + PositionX16 + "," + PositionZ16 + "," + Angle4096;
        }
    }

    public sealed class ZoneStateV2
    {
        public ZoneBlockKeyV2 Key { get; private set; }
        public ulong Zone1 { get; private set; }
        public ulong Zone2 { get; private set; }
        public bool IsEmpty { get { return Zone1 == 0 && Zone2 == 0; } }

        public ZoneStateV2(ZoneBlockKeyV2 key, ulong zone1, ulong zone2)
        {
            Check.Condition(!key.IsValid, "key", "Invalid zoning key.");
            Key = key; Zone1 = zone1; Zone2 = zone2;
        }
    }

    public sealed class ZoneIntentV2
    {
        public ZoneStateV2 Requested { get; private set; }
        public ZoneIntentV2(ZoneStateV2 requested)
        {
            Check.NotNull(requested, "requested");
            Requested = requested;
        }
    }

    public sealed class ZoneMutationV2
    {
        public ZoneStateV2[] Upserts { get; private set; }
        public ZoneBlockKeyV2[] Deletes { get; private set; }
        public int Count { get { return Upserts.Length + Deletes.Length; } }

        public ZoneMutationV2(ZoneStateV2[] upserts, ZoneBlockKeyV2[] deletes)
        {
            if (upserts == null || deletes == null) throw new ArgumentNullException("zone mutation arrays");
            if (upserts.Length + deletes.Length > 1024) throw new ArgumentException("Zone mutation is too large.");
            Upserts = (ZoneStateV2[])upserts.Clone();
            Deletes = (ZoneBlockKeyV2[])deletes.Clone();
            for (int i = 0; i < Upserts.Length; i++)
                if (Upserts[i] == null || Upserts[i].IsEmpty) throw new ArgumentException("Zone upserts must be non-empty.");
            for (int i = 0; i < Deletes.Length; i++)
                if (!Deletes[i].IsValid) throw new ArgumentException("Zone delete key is invalid.");
        }
    }

    public sealed class ZoneStateIndexV2
    {
        private readonly SortedDictionary<ZoneBlockKeyV2, ZoneStateV2> states =
            new SortedDictionary<ZoneBlockKeyV2, ZoneStateV2>();

        public int Count { get { return states.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }

        public void Seed(ZoneStateV2 value)
        {
            Check.Condition(value == null || value.IsEmpty, "value", "Only non-empty zoning belongs in the overlay.");
            if (states.ContainsKey(value.Key)) throw new InvalidOperationException("Duplicate zoning key.");
            states.Add(value.Key, value);
        }

        public void Apply(ZoneMutationV2 mutation)
        {
            Check.NotNull(mutation, "mutation");
            for (int i = 0; i < mutation.Deletes.Length; i++) states.Remove(mutation.Deletes[i]);
            for (int i = 0; i < mutation.Upserts.Length; i++) states[mutation.Upserts[i].Key] = mutation.Upserts[i];
        }

        public bool TryGet(ZoneBlockKeyV2 key, out ZoneStateV2 value)
        {
            return states.TryGetValue(key, out value);
        }

        public ZoneStateV2[] Snapshot()
        {
            ZoneStateV2[] result = new ZoneStateV2[states.Count]; int index = 0;
            foreach (ZoneStateV2 value in states.Values) result[index++] = value;
            return result;
        }

        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x325A4746u); // FGZ2
                writer.Write((uint)states.Count);
                foreach (ZoneStateV2 value in states.Values)
                {
                    ZoneDomainCodecV2.WriteKey(writer, value.Key);
                    writer.Write(value.Zone1); writer.Write(value.Zone2);
                }
                writer.Flush(); return stream.ToArray();
            }
        }
    }

    public static class ZoneDomainCodecV2
    {
        private const uint MutationMagic = 0x324D5A46u; // FZM2
        private const int KeyBytes = 24;
        private const int StateBytes = 40;

        public static byte[] EncodeIntent(ZoneIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                WriteKey(writer, value.Requested.Key);
                writer.Write(value.Requested.Zone1); writer.Write(value.Requested.Zone2);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static ZoneIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length != StateBytes) throw new InvalidDataException("Invalid zoning intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
                return new ZoneIntentV2(new ZoneStateV2(ReadKey(reader), reader.ReadUInt64(), reader.ReadUInt64()));
        }

        public static byte[] EncodeMutation(ZoneMutationV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(MutationMagic);
                writer.Write((ushort)value.Upserts.Length);
                writer.Write((ushort)value.Deletes.Length);
                for (int i = 0; i < value.Upserts.Length; i++)
                {
                    WriteKey(writer, value.Upserts[i].Key);
                    writer.Write(value.Upserts[i].Zone1); writer.Write(value.Upserts[i].Zone2);
                }
                for (int i = 0; i < value.Deletes.Length; i++) WriteKey(writer, value.Deletes[i]);
                writer.Flush();
                byte[] result = stream.ToArray();
                if (result.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Zone mutation exceeds frame payload budget.");
                return result;
            }
        }

        public static ZoneMutationV2 DecodeMutation(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid zoning mutation length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != MutationMagic) throw new InvalidDataException("Unknown zoning mutation magic.");
                ushort upsertCount = reader.ReadUInt16(); ushort deleteCount = reader.ReadUInt16();
                if (upsertCount + deleteCount > 1024) throw new InvalidDataException("Zoning mutation is too large.");
                long expected = 8L + upsertCount * StateBytes + deleteCount * KeyBytes;
                if (expected != bytes.Length) throw new InvalidDataException("Zoning mutation length does not match counts.");
                ZoneStateV2[] upserts = new ZoneStateV2[upsertCount];
                ZoneBlockKeyV2[] deletes = new ZoneBlockKeyV2[deleteCount];
                for (int i = 0; i < upserts.Length; i++)
                    upserts[i] = new ZoneStateV2(ReadKey(reader), reader.ReadUInt64(), reader.ReadUInt64());
                for (int i = 0; i < deletes.Length; i++) deletes[i] = ReadKey(reader);
                return new ZoneMutationV2(upserts, deletes);
            }
        }

        internal static void WriteKey(BinaryWriter writer, ZoneBlockKeyV2 value)
        {
            if (!value.IsValid) throw new InvalidDataException("Invalid zoning key.");
            writer.Write(value.Segment.EntityId); writer.Write(value.Segment.Generation);
            writer.Write(value.PositionX16); writer.Write(value.PositionZ16); writer.Write(value.Angle4096);
        }

        internal static ZoneBlockKeyV2 ReadKey(BinaryReader reader)
        {
            return new ZoneBlockKeyV2(new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }
    }
}
