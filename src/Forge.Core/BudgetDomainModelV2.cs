using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public struct BudgetKeyV2 : IEquatable<BudgetKeyV2>, IComparable<BudgetKeyV2>
    {
        public readonly int Service;
        public readonly int SubService;
        public readonly bool Night;
        public BudgetKeyV2(int service, int subService, bool night)
        {
            Service = service; SubService = subService; Night = night;
        }
        public bool Equals(BudgetKeyV2 other)
        {
            return Service == other.Service && SubService == other.SubService && Night == other.Night;
        }
        public override bool Equals(object obj) { return obj is BudgetKeyV2 && Equals((BudgetKeyV2)obj); }
        public override int GetHashCode() { return Service ^ (SubService * 397) ^ (Night ? 7919 : 0); }
        public int CompareTo(BudgetKeyV2 other)
        {
            int value = Service.CompareTo(other.Service); if (value != 0) return value;
            value = SubService.CompareTo(other.SubService); if (value != 0) return value;
            return Night.CompareTo(other.Night);
        }
    }

    public sealed class BudgetStateV2
    {
        public BudgetKeyV2 Key { get; private set; }
        public int Budget { get; private set; }
        public BudgetStateV2(BudgetKeyV2 key, int budget)
        {
            if (budget < 0 || budget > 255) throw new ArgumentOutOfRangeException("budget");
            Key = key; Budget = budget;
        }
    }

    public sealed class BudgetIntentV2
    {
        public BudgetStateV2 Requested { get; private set; }
        public BudgetIntentV2(BudgetStateV2 requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            Requested = requested;
        }
    }

    public sealed class BudgetStateIndexV2
    {
        private readonly SortedDictionary<BudgetKeyV2, BudgetStateV2> values =
            new SortedDictionary<BudgetKeyV2, BudgetStateV2>();
        public int Count { get { return values.Count; } }
        public Hash256 Root { get { return Hash256.Compute(EncodeCanonical()); } }
        public void Upsert(BudgetStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            values[value.Key] = value;
        }
        public bool TryGet(BudgetKeyV2 key, out BudgetStateV2 value) { return values.TryGetValue(key, out value); }
        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(0x32424746u); // FGB2
                writer.Write((uint)values.Count);
                foreach (BudgetStateV2 value in values.Values)
                {
                    writer.Write(value.Key.Service); writer.Write(value.Key.SubService);
                    writer.Write((byte)(value.Key.Night ? 1 : 0)); writer.Write((byte)value.Budget);
                }
                writer.Flush(); return stream.ToArray();
            }
        }
    }

    public static class BudgetDomainCodecV2
    {
        private const int Bytes = 10;
        public static byte[] Encode(BudgetStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write(value.Key.Service); writer.Write(value.Key.SubService);
                writer.Write((byte)(value.Key.Night ? 1 : 0)); writer.Write((byte)value.Budget); writer.Flush(); return stream.ToArray();
            }
        }
        public static BudgetStateV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != Bytes) throw new InvalidDataException("Invalid budget payload length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                int service = reader.ReadInt32(); int subService = reader.ReadInt32(); byte night = reader.ReadByte();
                if (night > 1) throw new InvalidDataException("Invalid budget night flag.");
                return new BudgetStateV2(new BudgetKeyV2(service, subService, night == 1), reader.ReadByte());
            }
        }
        public static byte[] EncodeIntent(BudgetIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value"); return Encode(value.Requested);
        }
        public static BudgetIntentV2 DecodeIntent(byte[] bytes) { return new BudgetIntentV2(Decode(bytes)); }
    }
}
