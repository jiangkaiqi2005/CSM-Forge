using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    public enum SubmitDecision
    {
        Committed, Unauthenticated, WrongSession, NotReady, ReadOnly,
        OutOfOrder, ExpiredRequest, RequestIdConflict, RevisionConflict, DomainRejected, Faulted
    }

    public sealed class SubmitResult
    {
        public SubmitDecision Decision { get; private set; }
        public Commit Commit { get; private set; }
        public bool FromCache { get; private set; }
        internal SubmitResult(SubmitDecision decision, Commit commit, bool fromCache)
        {
            Decision = decision; Commit = commit; FromCache = fromCache;
        }
    }

    /// <summary>
    /// Single writer. Connection IDs are fresh transport-authenticated incarnations, never
    /// taken from an intent payload. Receipts are in-memory, not crash-durable exactly-once.
    /// </summary>
    public sealed class HostSession
    {
        private sealed class Receipt
        {
            public Hash256 Fingerprint;
            public SubmitResult Result;
        }
        private sealed class Peer
        {
            public bool MayEdit;
            public bool Ready;
            public ulong LastRequest;
            public readonly Dictionary<ulong, Receipt> Receipts = new Dictionary<ulong, Receipt>();
            public readonly Queue<ulong> Order = new Queue<ulong>();
        }

        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly IAuthorityWorld world;
        private readonly DiagnosticRing diagnostics;
        private readonly Dictionary<Guid, Peer> peers = new Dictionary<Guid, Peer>();
        private readonly Queue<Commit> journal = new Queue<Commit>();
        private bool busy;
        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 StateHash { get; private set; }
        public bool IsFenced { get; private set; }
        public int JournalCount { get { owner.AssertCurrent(); return journal.Count; } }
        public int PeerCount { get { owner.AssertCurrent(); return peers.Count; } }

        public HostSession(SessionStamp stamp, IAuthorityWorld world, DiagnosticRing diagnostics)
        {
            Check.Stamp(stamp);
            Check.NotNull(world, "world"); Check.NotNull(diagnostics, "diagnostics"); // WP-2: per-argument reporting
            Stamp = stamp; this.world = world; this.diagnostics = diagnostics;
            StateHash = world.StateHash;
            Check.Condition(StateHash == null, "world", "World must expose its initial canonical digest.");
        }

        private void Enter()
        {
            owner.AssertCurrent();
            if (busy) throw new InvalidOperationException("Reentrant authority access is not permitted.");
        }

        // Trusted coordinator API, called only after authentication AND compatibility approval.
        public bool RegisterPeer(Guid connectionIncarnation, bool mayEdit)
        {
            Enter();
            if (IsFenced || connectionIncarnation == Guid.Empty || peers.ContainsKey(connectionIncarnation) || peers.Count == Limits.Peers)
                return false;
            peers.Add(connectionIncarnation, new Peer { MayEdit = mayEdit });
            return true;
        }

        public bool CompleteJoin(Guid connection, SessionStamp stamp, ulong revision, Hash256 hash)
        {
            Enter();
            Peer peer;
            if (IsFenced || !stamp.Equals(Stamp) || !peers.TryGetValue(connection, out peer) || revision != Revision || hash == null)
                return false;
            busy = true;
            try
            {
                AssertCommittedWorld();
                if (!StateHash.Equals(hash)) return false;
                peer.Ready = true;
                return true;
            }
            catch (Exception) { Fence(connection, 0); return false; }
            finally { busy = false; }
        }

        // A coordinator schedules this at safe, coherent domain boundaries, not on network threads.
        public bool VerifyWorld()
        {
            Enter();
            if (IsFenced) return false;
            busy = true;
            try { AssertCommittedWorld(); return true; }
            catch (Exception) { Fence(Guid.Empty, 0); return false; }
            finally { busy = false; }
        }

        private void AssertCommittedWorld()
        {
            if (!StateHash.Equals(world.StateHash))
                throw new InvalidOperationException("World changed outside the committed authority path.");
        }

        public void SuspendPeer(Guid connection)
        {
            Enter();
            Peer peer;
            if (peers.TryGetValue(connection, out peer)) peer.Ready = false;
        }

        public void Disconnect(Guid connection)
        {
            Enter();
            peers.Remove(connection);
            diagnostics.Record(DiagnosticCode.Disconnected, Stamp, connection, Revision, 0);
        }

        public SubmitResult Submit(Guid authenticatedConnection, Intent intent)
        {
            Enter();
            Check.NotNull(intent, "intent");
            busy = true;
            try { return SubmitCore(authenticatedConnection, intent); }
            finally { busy = false; }
        }

        private SubmitResult SubmitCore(Guid connection, Intent intent)
        {
            if (IsFenced) return Result(SubmitDecision.Faulted);
            Peer peer;
            if (!peers.TryGetValue(connection, out peer))
            {
                diagnostics.Record(DiagnosticCode.IdentityRejected, Stamp, connection, Revision, intent.RequestId);
                return Result(SubmitDecision.Unauthenticated);
            }
            if (!intent.Stamp.Equals(Stamp))
            {
                diagnostics.Record(DiagnosticCode.StaleSession, Stamp, connection, Revision, intent.RequestId);
                return Result(SubmitDecision.WrongSession);
            }
            // Inspect receipts before current readiness/revision: a lost reply must remain recoverable.
            if (intent.RequestId <= peer.LastRequest)
            {
                Receipt receipt;
                if (!peer.Receipts.TryGetValue(intent.RequestId, out receipt)) return Result(SubmitDecision.ExpiredRequest);
                if (!receipt.Fingerprint.Equals(intent.Fingerprint)) return Result(SubmitDecision.RequestIdConflict);
                diagnostics.Record(DiagnosticCode.Duplicate, Stamp, connection, Revision, intent.RequestId);
                return new SubmitResult(receipt.Result.Decision, receipt.Result.Commit, true);
            }
            if (peer.LastRequest == ulong.MaxValue || intent.RequestId != peer.LastRequest + 1)
                return Result(SubmitDecision.OutOfOrder);
            if (!peer.Ready) return Result(SubmitDecision.NotReady);
            if (!peer.MayEdit) return Result(SubmitDecision.ReadOnly);
            if (intent.ExpectedRevision != Revision)
                return Remember(peer, intent, Result(SubmitDecision.RevisionConflict));
            if (Revision == ulong.MaxValue)
            {
                Fence(connection, intent.RequestId);
                return Result(SubmitDecision.Faulted);
            }

            try
            {
                AssertCommittedWorld();
                Hash256 before = StateHash;
                WorldExecution execution = world.Execute(intent.Payload);
                if (execution == null) throw new InvalidOperationException("Adapter returned no outcome.");
                Hash256 after = world.StateHash;
                if (!execution.Applied)
                {
                    if (!before.Equals(after)) throw new InvalidOperationException("Rejected operation changed the world.");
                    diagnostics.Record(DiagnosticCode.Rejected, Stamp, connection, Revision, intent.RequestId);
                    return Remember(peer, intent, Result(SubmitDecision.DomainRejected));
                }
                if (after == null || !after.Equals(execution.AfterHash)) throw new InvalidOperationException("Adapter outcome digest mismatch.");
                Commit commit = new Commit(Stamp, Revision + 1, connection, intent.RequestId, before, after, execution.Delta);
                journal.Enqueue(commit);
                if (journal.Count > Limits.JournalEntries) journal.Dequeue();
                Revision = commit.Revision;
                StateHash = after;
                diagnostics.Record(DiagnosticCode.Committed, Stamp, connection, Revision, intent.RequestId);
                return Remember(peer, intent, new SubmitResult(SubmitDecision.Committed, commit, false));
            }
            catch (Exception)
            {
                // The adapter may have changed game state. Do not retry or claim rollback.
                Fence(connection, intent.RequestId);
                return Remember(peer, intent, Result(SubmitDecision.Faulted));
            }
        }

        public WorldSnapshot CaptureSnapshot(Guid transferId)
        {
            Enter();
            if (IsFenced) throw new InvalidOperationException("A fenced host cannot publish a world snapshot.");
            Check.Condition(transferId == Guid.Empty, "transferId", "Missing transfer identity.");
            busy = true;
            try
            {
                AssertCommittedWorld();
                Hash256 before = StateHash;
                WorldImage image = world.Capture();
                if (image == null || !image.HasValidContent() || !before.Equals(image.StateHash) || !before.Equals(world.StateHash))
                    throw new InvalidOperationException("The snapshot is not a coherent committed cut.");
                return new WorldSnapshot(Stamp, Revision, transferId, image);
            }
            catch (Exception) { Fence(Guid.Empty, 0); throw; }
            finally { busy = false; }
        }

        public bool TryReadJournal(ulong afterRevision, out Commit[] commits)
        {
            Enter();
            commits = null;
            if (IsFenced || afterRevision > Revision) return false;
            if (afterRevision == Revision) { commits = new Commit[0]; return true; }
            if (journal.Count == 0 || afterRevision < journal.Peek().Revision - 1) return false;
            List<Commit> result = new List<Commit>();
            foreach (Commit commit in journal)
                if (commit.Revision > afterRevision) result.Add(commit);
            commits = result.ToArray();
            return true;
        }

        private static SubmitResult Result(SubmitDecision decision) { return new SubmitResult(decision, null, false); }
        private static SubmitResult Remember(Peer peer, Intent intent, SubmitResult result)
        {
            peer.LastRequest = intent.RequestId;
            peer.Receipts.Add(intent.RequestId, new Receipt { Fingerprint = intent.Fingerprint, Result = result });
            peer.Order.Enqueue(intent.RequestId);
            if (peer.Order.Count > Limits.ReceiptsPerPeer) peer.Receipts.Remove(peer.Order.Dequeue());
            return result;
        }
        private void Fence(Guid peer, ulong request)
        {
            IsFenced = true;
            diagnostics.Record(DiagnosticCode.WorldFault, Stamp, peer, Revision, request);
        }
    }
}
