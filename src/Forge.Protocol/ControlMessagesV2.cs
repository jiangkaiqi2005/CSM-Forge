using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public sealed class IntentReceiptV2
    {
        public ulong OperationCounter { get; private set; }
        public AuthoritySubmitDecisionV2 Decision { get; private set; }
        public ulong Revision { get; private set; }

        public IntentReceiptV2(ulong operationCounter, AuthoritySubmitDecisionV2 decision, ulong revision)
        {
            if (operationCounter == 0) throw new ArgumentOutOfRangeException("operationCounter");
            if (!Enum.IsDefined(typeof(AuthoritySubmitDecisionV2), decision)) throw new ArgumentOutOfRangeException("decision");
            if (decision == AuthoritySubmitDecisionV2.Committed && revision == 0)
                throw new ArgumentException("Committed receipt requires a revision.");
            OperationCounter = operationCounter;
            Decision = decision;
            Revision = revision;
        }
    }

    public sealed class ActivationStateV2
    {
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }

        public ActivationStateV2(ulong revision, Hash256 root)
        {
            if (root == null) throw new ArgumentNullException("root");
            Revision = revision;
            Root = root;
        }
    }

    public sealed class GapRequestV2
    {
        public ulong AfterRevision { get; private set; }
        public GapRequestV2(ulong afterRevision) { AfterRevision = afterRevision; }
    }

    public static class ControlMessagesV2
    {
        public static byte[] EncodeReceipt(IntentReceiptV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.OperationCounter);
                writer.Write((ushort)value.Decision);
                writer.Write(value.Revision);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static IntentReceiptV2 DecodeReceipt(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 18) throw new InvalidDataException("Intent receipt length is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
                return new IntentReceiptV2(reader.ReadUInt64(), (AuthoritySubmitDecisionV2)reader.ReadUInt16(), reader.ReadUInt64());
        }

        public static byte[] EncodeActivation(ActivationStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            byte[] bytes = new byte[40];
            Buffer.BlockCopy(BitConverter.GetBytes(value.Revision), 0, bytes, 0, 8);
            Buffer.BlockCopy(value.Root.ToArray(), 0, bytes, 8, 32);
            return bytes;
        }

        public static ActivationStateV2 DecodeActivation(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 40) throw new InvalidDataException("Activation payload length is invalid.");
            byte[] root = new byte[32];
            Buffer.BlockCopy(bytes, 8, root, 0, 32);
            return new ActivationStateV2(BitConverter.ToUInt64(bytes, 0), new Hash256(root));
        }

        public static byte[] EncodeGapRequest(GapRequestV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return BitConverter.GetBytes(value.AfterRevision);
        }

        public static GapRequestV2 DecodeGapRequest(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 8) throw new InvalidDataException("Gap request length is invalid.");
            return new GapRequestV2(BitConverter.ToUInt64(bytes, 0));
        }
    }

    /// <summary>Exact per-lane sequence validation. Reliable delivery is transport, not identity.</summary>
    public sealed class LaneSequenceTracker
    {
        private readonly ulong[] inbound = new ulong[4];
        private readonly ulong[] outbound = new ulong[4];

        public bool Accept(SessionLane lane, ulong sequence)
        {
            int index = (int)lane;
            if (index < 0 || index >= inbound.Length || sequence == 0 || inbound[index] == ulong.MaxValue)
                return false;
            if (sequence != inbound[index] + 1) return false;
            inbound[index] = sequence;
            return true;
        }

        public ulong Next(SessionLane lane)
        {
            int index = (int)lane;
            if (index < 0 || index >= outbound.Length || outbound[index] == ulong.MaxValue)
                throw new InvalidOperationException("Lane sequence exhausted.");
            outbound[index]++;
            return outbound[index];
        }
    }
}
