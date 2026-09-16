using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public sealed class PlayerIntentV2
    {
        private readonly byte[] payload;
        public SessionStamp Stamp { get; private set; }
        public MemberIdentity Member { get; private set; }
        public ulong OperationCounter { get; private set; }
        public ulong PermissionVersion { get; private set; }
        public ushort DomainId { get; private set; }
        public Hash256 ExpectedDomainRoot { get; private set; }
        public Hash256 Fingerprint { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public PlayerIntentV2(SessionStamp stamp, MemberIdentity member, ulong operationCounter,
            ulong permissionVersion, ushort domainId, Hash256 expectedDomainRoot, byte[] bytes)
        {
            Check.Stamp(stamp);
            if (!member.IsValid) throw new ArgumentException("Invalid member identity.", "member");
            if (operationCounter == 0) throw new ArgumentOutOfRangeException("operationCounter");
            if (permissionVersion == 0) throw new ArgumentOutOfRangeException("permissionVersion");
            if (domainId == 0) throw new ArgumentOutOfRangeException("domainId");
            if (expectedDomainRoot == null) throw new ArgumentNullException("expectedDomainRoot");
            Stamp = stamp;
            Member = member;
            OperationCounter = operationCounter;
            PermissionVersion = permissionVersion;
            DomainId = domainId;
            ExpectedDomainRoot = expectedDomainRoot;
            payload = Check.Copy(bytes, Limits.CommandBytes, false);
            Fingerprint = ComputeFingerprint();
        }

        private Hash256 ComputeFingerprint()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Stamp.WorldId.ToByteArray());
                writer.Write(Stamp.Epoch);
                writer.Write(Member.MemberId.ToByteArray());
                writer.Write(Member.Generation);
                writer.Write(OperationCounter);
                writer.Write(PermissionVersion);
                writer.Write(DomainId);
                writer.Write(ExpectedDomainRoot.ToArray());
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                return Hash256.Compute(stream.ToArray());
            }
        }
    }

    public sealed class DomainExecutionV2
    {
        private readonly byte[] delta;
        public bool Applied { get; private set; }
        public Hash256 AfterRoot { get; private set; }
        public byte[] AbsoluteDelta { get { return delta == null ? null : (byte[])delta.Clone(); } }

        private DomainExecutionV2(bool applied, byte[] bytes, Hash256 afterRoot)
        {
            Applied = applied;
            delta = bytes;
            AfterRoot = afterRoot;
        }

        public static DomainExecutionV2 Rejected()
        {
            return new DomainExecutionV2(false, null, null);
        }

        public static DomainExecutionV2 Success(byte[] absoluteDelta, Hash256 afterRoot)
        {
            if (afterRoot == null) throw new ArgumentNullException("afterRoot");
            return new DomainExecutionV2(true, Check.Copy(absoluteDelta, Limits.FramePayloadBytes, false), afterRoot);
        }
    }

    public interface IAuthorityDomainV2
    {
        ushort DomainId { get; }
        Hash256 StateRoot { get; }
        DomainExecutionV2 ExecutePlayer(byte[] payload);
    }

    public interface IReplicaDomainV2
    {
        ushort DomainId { get; }
        Hash256 StateRoot { get; }
        void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot);
    }

    public enum AuthoritySubmitDecisionV2
    {
        Committed,
        Unauthenticated,
        WrongSession,
        WrongMember,
        NotLive,
        ReadOnly,
        PermissionChanged,
        OutOfOrder,
        ExpiredOperation,
        OperationIdentityConflict,
        UnknownDomain,
        ReadConflict,
        DomainRejected,
        Faulted
    }

    public sealed class AuthoritySubmitResultV2
    {
        public AuthoritySubmitDecisionV2 Decision { get; private set; }
        public AuthorityBatch Batch { get; private set; }
        public bool FromCache { get; private set; }

        internal AuthoritySubmitResultV2(AuthoritySubmitDecisionV2 decision, AuthorityBatch batch, bool fromCache)
        {
            Decision = decision;
            Batch = batch;
            FromCache = fromCache;
        }
    }

    public enum ReplicaDecisionV2
    {
        Applied,
        Duplicate,
        Gap,
        WrongSession,
        UnknownDomain,
        HashMismatch,
        ApplyFailed,
        NeedsSnapshot,
        Disconnected
    }

    /// <summary>
    /// Pure owner-thread v2 authority coordinator. It serializes commits but does not know
    /// about sockets, Unity or Cities: Skylines objects beyond the domain adapter contract.
    /// </summary>
    public sealed class AuthorityCoordinatorV2
    {
        private sealed class Receipt
        {
            public Hash256 Fingerprint;
            public AuthoritySubmitResultV2 Result;
        }

        private sealed class Connection
        {
            public MemberIdentity Member;
            public bool MayEdit;
            public bool Live;
            public ulong PermissionVersion;
            public ulong LastOperation;
            public ulong AppliedRevision;
            public readonly Dictionary<ulong, Receipt> Receipts = new Dictionary<ulong, Receipt>();
            public readonly Queue<ulong> ReceiptOrder = new Queue<ulong>();
        }

        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly Dictionary<Guid, Connection> connections = new Dictionary<Guid, Connection>();
        private readonly Dictionary<ushort, IAuthorityDomainV2> domains = new Dictionary<ushort, IAuthorityDomainV2>();
        private readonly Dictionary<ushort, Hash256> roots = new Dictionary<ushort, Hash256>();
        private readonly Queue<AuthorityBatch> journal = new Queue<AuthorityBatch>();
        private readonly Dictionary<ulong, Hash256> historicalRoots = new Dictionary<ulong, Hash256>();
        private readonly Queue<ulong> rootOrder = new Queue<ulong>();
        private bool busy;

        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 CurrentRoot { get; private set; }
        public bool IsFenced { get; private set; }

        public AuthorityCoordinatorV2(SessionStamp stamp, IEnumerable<IAuthorityDomainV2> domainAdapters)
        {
            Check.Stamp(stamp);
            if (domainAdapters == null) throw new ArgumentNullException("domainAdapters");
            Stamp = stamp;
            foreach (IAuthorityDomainV2 domain in domainAdapters)
            {
                if (domain == null || domain.DomainId == 0 || domain.StateRoot == null || domains.ContainsKey(domain.DomainId))
                    throw new ArgumentException("Invalid or duplicate authority domain.", "domainAdapters");
                domains.Add(domain.DomainId, domain);
                roots.Add(domain.DomainId, domain.StateRoot);
            }
            if (domains.Count == 0 || domains.Count > 256)
                throw new ArgumentException("Authority coordinator needs 1..256 domains.", "domainAdapters");
            CurrentRoot = AggregateRoot(roots);
            RememberRoot(0, CurrentRoot);
        }

        private void Enter()
        {
            owner.AssertCurrent();
            if (busy) throw new InvalidOperationException("Reentrant authority-v2 access is not permitted.");
        }

        public bool RegisterConnection(Guid binding, MemberIdentity member, bool mayEdit, ulong permissionVersion)
        {
            Enter();
            if (IsFenced || binding == Guid.Empty || !member.IsValid || permissionVersion == 0 ||
                connections.ContainsKey(binding) || connections.Count >= Limits.Peers) return false;
            connections.Add(binding, new Connection
            {
                Member = member,
                MayEdit = mayEdit,
                PermissionVersion = permissionVersion
            });
            return true;
        }

        public bool SetLive(Guid binding, bool live)
        {
            Enter();
            Connection connection;
            if (!connections.TryGetValue(binding, out connection) || IsFenced) return false;
            connection.Live = live;
            return true;
        }

        public bool ChangePermission(Guid binding, bool mayEdit, ulong permissionVersion)
        {
            Enter();
            Connection connection;
            if (!connections.TryGetValue(binding, out connection) || permissionVersion <= connection.PermissionVersion) return false;
            connection.MayEdit = mayEdit;
            connection.PermissionVersion = permissionVersion;
            if (!mayEdit) connection.Live = false;
            return true;
        }

        public void Disconnect(Guid binding)
        {
            Enter();
            connections.Remove(binding);
        }

        public AuthoritySubmitResultV2 Submit(Guid binding, PlayerIntentV2 intent)
        {
            Enter();
            if (intent == null) throw new ArgumentNullException("intent");
            busy = true;
            try { return SubmitCore(binding, intent); }
            finally { busy = false; }
        }

        private AuthoritySubmitResultV2 SubmitCore(Guid binding, PlayerIntentV2 intent)
        {
            if (IsFenced) return Result(AuthoritySubmitDecisionV2.Faulted);
            Connection connection;
            if (!connections.TryGetValue(binding, out connection)) return Result(AuthoritySubmitDecisionV2.Unauthenticated);
            if (!intent.Stamp.Equals(Stamp)) return Result(AuthoritySubmitDecisionV2.WrongSession);
            if (!intent.Member.Equals(connection.Member)) return Result(AuthoritySubmitDecisionV2.WrongMember);

            if (intent.OperationCounter <= connection.LastOperation)
            {
                Receipt receipt;
                if (!connection.Receipts.TryGetValue(intent.OperationCounter, out receipt))
                    return Result(AuthoritySubmitDecisionV2.ExpiredOperation);
                if (!receipt.Fingerprint.Equals(intent.Fingerprint))
                    return Result(AuthoritySubmitDecisionV2.OperationIdentityConflict);
                return new AuthoritySubmitResultV2(receipt.Result.Decision, receipt.Result.Batch, true);
            }
            if (connection.LastOperation == ulong.MaxValue || intent.OperationCounter != connection.LastOperation + 1)
                return Result(AuthoritySubmitDecisionV2.OutOfOrder);
            if (!connection.Live) return Result(AuthoritySubmitDecisionV2.NotLive);
            if (!connection.MayEdit) return Result(AuthoritySubmitDecisionV2.ReadOnly);
            if (intent.PermissionVersion != connection.PermissionVersion)
                return Remember(connection, intent, Result(AuthoritySubmitDecisionV2.PermissionChanged));

            IAuthorityDomainV2 domain;
            Hash256 knownRoot;
            if (!domains.TryGetValue(intent.DomainId, out domain) || !roots.TryGetValue(intent.DomainId, out knownRoot))
                return Remember(connection, intent, Result(AuthoritySubmitDecisionV2.UnknownDomain));
            if (!knownRoot.Equals(intent.ExpectedDomainRoot))
                return Remember(connection, intent, Result(AuthoritySubmitDecisionV2.ReadConflict));

            try
            {
                AssertDomain(domain, knownRoot);
                DomainExecutionV2 execution = domain.ExecutePlayer(intent.Payload);
                if (execution == null) throw new InvalidOperationException("Domain adapter returned no execution result.");
                Hash256 actualAfter = domain.StateRoot;
                if (!execution.Applied)
                {
                    if (!knownRoot.Equals(actualAfter))
                        throw new InvalidOperationException("Rejected operation changed the authority domain.");
                    return Remember(connection, intent, Result(AuthoritySubmitDecisionV2.DomainRejected));
                }
                if (execution.AfterRoot == null || actualAfter == null || !execution.AfterRoot.Equals(actualAfter))
                    throw new InvalidOperationException("Authority domain result root mismatch.");
                AuthorityBatch batch = PublishBatch(AuthorityOriginKind.PlayerIntent, connection.Member,
                    intent.OperationCounter, intent.DomainId, knownRoot, actualAfter, execution.AbsoluteDelta);
                return Remember(connection, intent,
                    new AuthoritySubmitResultV2(AuthoritySubmitDecisionV2.Committed, batch, false));
            }
            catch
            {
                Fence();
                return Remember(connection, intent, Result(AuthoritySubmitDecisionV2.Faulted));
            }
        }

        /// <summary>
        /// Publishes an already-observed Host simulation/system change. The caller captures
        /// the absolute delta at a safe game boundary; this method never reruns simulation.
        /// </summary>
        public AuthorityBatch PublishObserved(AuthorityOriginKind originKind, ushort domainId,
            Hash256 beforeRoot, Hash256 afterRoot, byte[] absoluteDelta)
        {
            Enter();
            if (originKind == AuthorityOriginKind.PlayerIntent)
                throw new ArgumentException("Use Submit for player-origin work.", "originKind");
            if (IsFenced) return null;
            IAuthorityDomainV2 domain;
            Hash256 known;
            if (!domains.TryGetValue(domainId, out domain) || !roots.TryGetValue(domainId, out known) ||
                beforeRoot == null || afterRoot == null || absoluteDelta == null) return null;
            busy = true;
            try
            {
                if (!known.Equals(beforeRoot) || !domain.StateRoot.Equals(afterRoot))
                    throw new InvalidOperationException("Observed Host change does not bridge the committed domain root.");
                if (beforeRoot.Equals(afterRoot)) return null;
                return PublishBatch(originKind, default(MemberIdentity), 0, domainId,
                    beforeRoot, afterRoot, absoluteDelta);
            }
            catch
            {
                Fence();
                return null;
            }
            finally { busy = false; }
        }

        public bool RecordApplied(AppliedAck ack)
        {
            Enter();
            if (ack == null || !ack.Stamp.Equals(Stamp) || ack.ConnectionBinding == Guid.Empty ||
                ack.Revision > Revision) return false;
            Connection connection;
            Hash256 root;
            if (!connections.TryGetValue(ack.ConnectionBinding, out connection) ||
                !TryGetRoot(ack.Revision, out root) || !root.Equals(ack.Root)) return false;
            if (ack.Revision < connection.AppliedRevision) return false;
            connection.AppliedRevision = ack.Revision;
            return true;
        }

        public bool TryReadJournal(ulong afterRevision, out AuthorityBatch[] batches)
        {
            Enter();
            batches = null;
            if (IsFenced || afterRevision > Revision) return false;
            if (afterRevision == Revision) { batches = new AuthorityBatch[0]; return true; }
            if (journal.Count == 0 || afterRevision < journal.Peek().Revision - 1) return false;
            List<AuthorityBatch> result = new List<AuthorityBatch>();
            foreach (AuthorityBatch batch in journal)
                if (batch.Revision > afterRevision) result.Add(batch);
            batches = result.ToArray();
            return true;
        }

        public bool TryGetRoot(ulong revision, out Hash256 root)
        {
            Enter();
            return historicalRoots.TryGetValue(revision, out root);
        }

        private AuthorityBatch PublishBatch(AuthorityOriginKind originKind, MemberIdentity member,
            ulong operationCounter, ushort domainId, Hash256 beforeRoot, Hash256 afterRoot, byte[] delta)
        {
            if (Revision == ulong.MaxValue) throw new InvalidOperationException("Authority revision exhausted.");
            AuthorityBatch batch = new AuthorityBatch(Stamp, Revision + 1, originKind,
                originKind == AuthorityOriginKind.PlayerIntent ? member.MemberId : Guid.Empty,
                originKind == AuthorityOriginKind.PlayerIntent ? member.Generation : 0,
                originKind == AuthorityOriginKind.PlayerIntent ? operationCounter : 0,
                domainId, beforeRoot, afterRoot, delta);
            roots[domainId] = afterRoot;
            Revision = batch.Revision;
            CurrentRoot = AggregateRoot(roots);
            journal.Enqueue(batch);
            if (journal.Count > Limits.JournalEntries) journal.Dequeue();
            RememberRoot(Revision, CurrentRoot);
            return batch;
        }

        private void RememberRoot(ulong revision, Hash256 root)
        {
            historicalRoots[revision] = root;
            rootOrder.Enqueue(revision);
            while (rootOrder.Count > Limits.JournalEntries + 1)
                historicalRoots.Remove(rootOrder.Dequeue());
        }

        private static AuthoritySubmitResultV2 Result(AuthoritySubmitDecisionV2 decision)
        {
            return new AuthoritySubmitResultV2(decision, null, false);
        }

        private static AuthoritySubmitResultV2 Remember(Connection connection, PlayerIntentV2 intent,
            AuthoritySubmitResultV2 result)
        {
            connection.LastOperation = intent.OperationCounter;
            connection.Receipts.Add(intent.OperationCounter, new Receipt { Fingerprint = intent.Fingerprint, Result = result });
            connection.ReceiptOrder.Enqueue(intent.OperationCounter);
            if (connection.ReceiptOrder.Count > Limits.ReceiptsPerPeer)
                connection.Receipts.Remove(connection.ReceiptOrder.Dequeue());
            return result;
        }

        private static void AssertDomain(IAuthorityDomainV2 domain, Hash256 expected)
        {
            if (domain.StateRoot == null || !domain.StateRoot.Equals(expected))
                throw new InvalidOperationException("Authority domain changed outside the committed path.");
        }

        private void Fence() { IsFenced = true; }

        internal static Hash256 AggregateRoot(Dictionary<ushort, Hash256> source)
        {
            List<ushort> ids = new List<ushort>(source.Keys);
            ids.Sort();
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write((ushort)ids.Count);
                foreach (ushort id in ids)
                {
                    writer.Write(id);
                    writer.Write(source[id].ToArray());
                }
                writer.Flush();
                return Hash256.Compute(stream.ToArray());
            }
        }
    }

    public sealed class ReplicaCoordinatorV2
    {
        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly Dictionary<ushort, IReplicaDomainV2> domains = new Dictionary<ushort, IReplicaDomainV2>();
        private readonly Dictionary<ushort, Hash256> roots = new Dictionary<ushort, Hash256>();
        private readonly Dictionary<ulong, Hash256> recent = new Dictionary<ulong, Hash256>();
        private readonly Queue<ulong> recentOrder = new Queue<ulong>();
        private bool busy;

        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 CurrentRoot { get; private set; }
        public ReplicaPhase Phase { get; private set; }

        public ReplicaCoordinatorV2(SessionStamp stamp, IEnumerable<IReplicaDomainV2> domainAdapters,
            ulong baselineRevision)
        {
            Check.Stamp(stamp);
            if (domainAdapters == null) throw new ArgumentNullException("domainAdapters");
            Stamp = stamp;
            foreach (IReplicaDomainV2 domain in domainAdapters)
            {
                if (domain == null || domain.DomainId == 0 || domain.StateRoot == null || domains.ContainsKey(domain.DomainId))
                    throw new ArgumentException("Invalid or duplicate replica domain.", "domainAdapters");
                domains.Add(domain.DomainId, domain);
                roots.Add(domain.DomainId, domain.StateRoot);
            }
            if (domains.Count == 0 || domains.Count > 256)
                throw new ArgumentException("Replica coordinator needs 1..256 domains.", "domainAdapters");
            Revision = baselineRevision;
            CurrentRoot = AuthorityCoordinatorV2.AggregateRoot(roots);
            Phase = ReplicaPhase.CatchingUp;
        }

        private void Enter()
        {
            owner.AssertCurrent();
            if (busy) throw new InvalidOperationException("Reentrant replica-v2 access is not permitted.");
        }

        public ReplicaDecisionV2 Receive(AuthorityBatch batch)
        {
            Enter();
            if (batch == null) throw new ArgumentNullException("batch");
            if (Phase == ReplicaPhase.Disconnected) return ReplicaDecisionV2.Disconnected;
            if (Phase == ReplicaPhase.NeedsSnapshot) return ReplicaDecisionV2.NeedsSnapshot;
            if (!batch.Stamp.Equals(Stamp)) return ReplicaDecisionV2.WrongSession;
            if (batch.Revision <= Revision)
            {
                Hash256 fingerprint;
                if (recent.TryGetValue(batch.Revision, out fingerprint) && !fingerprint.Equals(batch.Fingerprint))
                {
                    Phase = ReplicaPhase.NeedsSnapshot;
                    return ReplicaDecisionV2.HashMismatch;
                }
                return ReplicaDecisionV2.Duplicate;
            }
            if (Revision == ulong.MaxValue || batch.Revision != Revision + 1)
            {
                Phase = ReplicaPhase.CatchingUp;
                return ReplicaDecisionV2.Gap;
            }
            IReplicaDomainV2 domain;
            Hash256 known;
            if (!domains.TryGetValue(batch.DomainId, out domain) || !roots.TryGetValue(batch.DomainId, out known))
                return ReplicaDecisionV2.UnknownDomain;
            if (!known.Equals(batch.BeforeRoot) || domain.StateRoot == null || !domain.StateRoot.Equals(known))
            {
                Phase = ReplicaPhase.NeedsSnapshot;
                return ReplicaDecisionV2.HashMismatch;
            }

            busy = true;
            try
            {
                domain.ApplyAbsolute(batch.Payload, batch.AfterRoot);
                if (domain.StateRoot == null || !domain.StateRoot.Equals(batch.AfterRoot))
                    throw new InvalidOperationException("Replica domain post-apply root mismatch.");
                roots[batch.DomainId] = batch.AfterRoot;
                Revision = batch.Revision;
                CurrentRoot = AuthorityCoordinatorV2.AggregateRoot(roots);
                recent[Revision] = batch.Fingerprint;
                recentOrder.Enqueue(Revision);
                if (recentOrder.Count > Limits.ReceiptsPerPeer)
                    recent.Remove(recentOrder.Dequeue());
                return ReplicaDecisionV2.Applied;
            }
            catch
            {
                Phase = ReplicaPhase.NeedsSnapshot;
                return ReplicaDecisionV2.ApplyFailed;
            }
            finally { busy = false; }
        }

        public bool MarkLive(ulong revision, Hash256 aggregateRoot)
        {
            Enter();
            if (revision != Revision || aggregateRoot == null || !aggregateRoot.Equals(CurrentRoot) ||
                Phase == ReplicaPhase.NeedsSnapshot || Phase == ReplicaPhase.Disconnected) return false;
            Phase = ReplicaPhase.Live;
            return true;
        }

        public AppliedAck CreateAppliedAck(Guid connectionBinding, int pendingBatches)
        {
            Enter();
            if (connectionBinding == Guid.Empty || Phase == ReplicaPhase.NeedsSnapshot || Phase == ReplicaPhase.Disconnected)
                return null;
            return new AppliedAck(Stamp, connectionBinding, Revision, CurrentRoot, pendingBatches);
        }

        public void Disconnect()
        {
            Enter();
            recent.Clear();
            recentOrder.Clear();
            Phase = ReplicaPhase.Disconnected;
        }
    }
}
