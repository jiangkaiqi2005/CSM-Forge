using System;
using System.IO;

namespace CsmForge.Core
{
    public enum AuthorityOriginKind : byte
    {
        PlayerIntent = 1,
        Simulation = 2,
        System = 3
    }

    /// <summary>
    /// Versioned host outcome for v2 replication. Unlike the legacy Commit type, simulation
    /// and system batches do not require a fake player request identity.
    /// </summary>
    public sealed class AuthorityBatch
    {
        private readonly byte[] payload;

        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public AuthorityOriginKind OriginKind { get; private set; }
        public Guid MemberId { get; private set; }
        public uint MemberGeneration { get; private set; }
        public ulong OperationCounter { get; private set; }
        public ushort DomainId { get; private set; }
        public Hash256 BeforeRoot { get; private set; }
        public Hash256 AfterRoot { get; private set; }
        public Hash256 Fingerprint { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public AuthorityBatch(SessionStamp stamp, ulong revision, AuthorityOriginKind originKind,
            Guid memberId, uint memberGeneration, ulong operationCounter, ushort domainId,
            Hash256 beforeRoot, Hash256 afterRoot, byte[] bytes)
        {
            Check.Stamp(stamp);
            Check.OutOfRange(revision == 0, "revision");
            Check.OutOfRange(!Enum.IsDefined(typeof(AuthorityOriginKind), originKind), "originKind");
            Check.OutOfRange(domainId == 0, "domainId");
            Check.NotNull(beforeRoot, "beforeRoot"); Check.NotNull(afterRoot, "afterRoot"); // WP-2: per-argument reporting
            if (originKind == AuthorityOriginKind.PlayerIntent)
            {
                if (memberId == Guid.Empty || memberGeneration == 0 || operationCounter == 0)
                    throw new ArgumentException("Player-origin batches need member and operation identity.");
            }
            else if (memberId != Guid.Empty || memberGeneration != 0 || operationCounter != 0)
                throw new ArgumentException("Simulation/system batches must not impersonate a player operation.");

            Stamp = stamp;
            Revision = revision;
            OriginKind = originKind;
            MemberId = memberId;
            MemberGeneration = memberGeneration;
            OperationCounter = operationCounter;
            DomainId = domainId;
            BeforeRoot = beforeRoot;
            AfterRoot = afterRoot;
            payload = Check.Copy(bytes, Limits.FramePayloadBytes, false);
            Fingerprint = ComputeFingerprint();
        }

        private Hash256 ComputeFingerprint()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Stamp.WorldId.ToByteArray());
                writer.Write(Stamp.Epoch);
                writer.Write(Revision);
                writer.Write((byte)OriginKind);
                writer.Write(MemberId.ToByteArray());
                writer.Write(MemberGeneration);
                writer.Write(OperationCounter);
                writer.Write(DomainId);
                writer.Write(BeforeRoot.ToArray());
                writer.Write(AfterRoot.ToArray());
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                return Hash256.Compute(stream.ToArray());
            }
        }
    }

    /// <summary>Replica acknowledgement means the game projection is published, not merely received.</summary>
    public sealed class AppliedAck
    {
        public SessionStamp Stamp { get; private set; }
        public Guid ConnectionBinding { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }
        public int PendingBatches { get; private set; }

        public AppliedAck(SessionStamp stamp, Guid connectionBinding, ulong revision, Hash256 root, int pendingBatches)
        {
            Check.Stamp(stamp);
            Check.Condition(connectionBinding == Guid.Empty, "connectionBinding", "Missing connection binding.");
            Check.NotNull(root, "root");
            Check.OutOfRange(pendingBatches < 0 || pendingBatches > 4096, "pendingBatches");
            Stamp = stamp;
            ConnectionBinding = connectionBinding;
            Revision = revision;
            Root = root;
            PendingBatches = pendingBatches;
        }
    }
}
