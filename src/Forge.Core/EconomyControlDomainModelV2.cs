using System;
using System.IO;

namespace CsmForge.Core
{
    public enum EconomyControlIntentKindV2 : byte
    {
        TakeLoan = 1,
        PayLoan = 2,
        AcceptBailout = 3,
        RejectBailout = 4
    }

    public sealed class EconomyControlIntentV2
    {
        public EconomyControlIntentKindV2 Kind { get; private set; }
        public int Index { get; private set; }
        public int Amount { get; private set; }
        public int Interest { get; private set; }
        public int Length { get; private set; }

        public EconomyControlIntentV2(EconomyControlIntentKindV2 kind, int index, int amount, int interest, int length)
        {
            if (kind < EconomyControlIntentKindV2.TakeLoan || kind > EconomyControlIntentKindV2.RejectBailout)
                throw new ArgumentOutOfRangeException("kind");
            if ((kind == EconomyControlIntentKindV2.TakeLoan || kind == EconomyControlIntentKindV2.PayLoan) && index < 0)
                throw new ArgumentOutOfRangeException("index");
            if (kind == EconomyControlIntentKindV2.TakeLoan && (amount <= 0 || interest < 0 || length <= 0))
                throw new ArgumentException("Loan parameters are invalid.");
            if (kind != EconomyControlIntentKindV2.TakeLoan && (amount != 0 || interest != 0 || length != 0))
                throw new ArgumentException("Only TakeLoan may carry amount, interest and length.");
            if ((kind == EconomyControlIntentKindV2.AcceptBailout || kind == EconomyControlIntentKindV2.RejectBailout) && index != 0)
                throw new ArgumentException("Bailout intents must not carry a loan index.");
            Kind = kind; Index = index; Amount = amount; Interest = interest; Length = length;
        }

        public static EconomyControlIntentV2 PayLoan(int index)
        {
            return new EconomyControlIntentV2(EconomyControlIntentKindV2.PayLoan, index, 0, 0, 0);
        }

        public static EconomyControlIntentV2 AcceptBailout()
        {
            return new EconomyControlIntentV2(EconomyControlIntentKindV2.AcceptBailout, 0, 0, 0, 0);
        }

        public static EconomyControlIntentV2 RejectBailout()
        {
            return new EconomyControlIntentV2(EconomyControlIntentKindV2.RejectBailout, 0, 0, 0, 0);
        }
    }

    public sealed class EconomyControlStateV2
    {
        private readonly byte[] snapshot;
        public byte[] Snapshot { get { return (byte[])snapshot.Clone(); } }
        public Hash256 Root { get { return Hash256.Compute(EconomyControlCodecV2.EncodeState(this)); } }

        public EconomyControlStateV2(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 32768)
                throw new ArgumentException("Invalid economy control snapshot.", "bytes");
            snapshot = (byte[])bytes.Clone();
        }
    }

    public static class EconomyControlCodecV2
    {
        private const uint IntentMagic = 0x32494545u; // EEI2
        private const uint StateMagic = 0x32534545u; // EES2
        public const int IntentBytes = 21;

        public static byte[] EncodeIntent(EconomyControlIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(IntentMagic); writer.Write((byte)value.Kind);
                writer.Write(value.Index); writer.Write(value.Amount); writer.Write(value.Interest); writer.Write(value.Length);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static EconomyControlIntentV2 DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length != IntentBytes) throw new InvalidDataException("Invalid economy control intent length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != IntentMagic) throw new InvalidDataException("Unknown economy control intent magic.");
                EconomyControlIntentKindV2 kind = (EconomyControlIntentKindV2)reader.ReadByte();
                return new EconomyControlIntentV2(kind, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            }
        }

        public static byte[] EncodeState(EconomyControlStateV2 value)
        {
            Check.NotNull(value, "value");
            byte[] payload = value.Snapshot;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(StateMagic); writer.Write((ushort)payload.Length); writer.Write(payload);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static EconomyControlStateV2 DecodeState(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > 32774) throw new InvalidDataException("Invalid economy control state length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != StateMagic) throw new InvalidDataException("Unknown economy control state magic.");
                ushort length = reader.ReadUInt16();
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length || reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Truncated or trailing economy control state bytes.");
                return new EconomyControlStateV2(payload);
            }
        }
    }
}
