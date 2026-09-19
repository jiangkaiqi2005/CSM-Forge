using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public struct TaxKeyV2 : IEquatable<TaxKeyV2>, IComparable<TaxKeyV2>
    {
        public readonly int Service;
        public readonly int SubService;
        public readonly int Level;

        public TaxKeyV2(int service, int subService, int level)
        {
            Service = service; SubService = subService; Level = level;
        }

        public bool Equals(TaxKeyV2 other)
        {
            return Service == other.Service && SubService == other.SubService && Level == other.Level;
        }
        public override bool Equals(object obj) { return obj is TaxKeyV2 && Equals((TaxKeyV2)obj); }
        public override int GetHashCode() { return Service ^ (SubService * 397) ^ (Level * 7919); }
        public int CompareTo(TaxKeyV2 other)
        {
            int value = Service.CompareTo(other.Service); if (value != 0) return value;
            value = SubService.CompareTo(other.SubService); if (value != 0) return value;
            return Level.CompareTo(other.Level);
        }
    }

    public sealed class TaxStateV2
    {
        public TaxKeyV2 Key { get; private set; }
        public int Rate { get; private set; }
        public TaxStateV2(TaxKeyV2 key, int rate)
        {
            Check.OutOfRange(rate < 0 || rate > 29, "rate");
            Key = key; Rate = rate;
        }
    }

    public sealed class TaxIntentV2
    {
        public TaxStateV2 Requested { get; private set; }
        public TaxIntentV2(TaxStateV2 requested)
        {
            Check.NotNull(requested, "requested");
            Requested = requested;
        }
    }

    public sealed class TaxStateIndexV2
    {
        private readonly SortedDictionary<TaxKeyV2, TaxStateV2> values = new SortedDictionary<TaxKeyV2, TaxStateV2>();
        public int Count { get { return values.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }

        public void Upsert(TaxStateV2 value)
        {
            Check.NotNull(value, "value");
            values[value.Key] = value;
        }

        public bool TryGet(TaxKeyV2 key, out TaxStateV2 value) { return values.TryGetValue(key, out value); }

        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x32544746u); // FGT2
                writer.Write((uint)values.Count);
                foreach (TaxStateV2 value in values.Values)
                {
                    writer.Write(value.Key.Service); writer.Write(value.Key.SubService); writer.Write(value.Key.Level);
                    writer.Write((byte)value.Rate);
                }
                writer.Flush(); return stream.ToArray();
            }
        }
    }

    public static class TaxDomainCodecV2
    {
        private const int Bytes = 13;

        public static byte[] Encode(TaxStateV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Key.Service); writer.Write(value.Key.SubService); writer.Write(value.Key.Level);
                writer.Write((byte)value.Rate); writer.Flush(); return stream.ToArray();
            }
        }

        public static TaxStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != Bytes) throw new InvalidDataException("Invalid tax payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
                return new TaxStateV2(new TaxKeyV2(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()), reader.ReadByte());
        }

        public static byte[] EncodeIntent(TaxIntentV2 value)
        {
            Check.NotNull(value, "value");
            return Encode(value.Requested);
        }
        public static TaxIntentV2 DecodeIntent(byte[] bytes) { return new TaxIntentV2(Decode(bytes)); }
    }
}
