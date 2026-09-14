using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    public enum ReplicaPhase { NeedsSnapshot, CatchingUp, Live, Disconnected }
    public enum ReplicaDecision
    {
        Applied, Duplicate, Gap, WrongSource, WrongSession, NeedsSnapshot, HashMismatch, ApplyFailed, Disconnected
    }

    public sealed class ReplicaSession
    {
        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly Guid hostConnection;
        private readonly IReplicaWorld world;
        private readonly DiagnosticRing diagnostics;
        private readonly Dictionary<ulong, Hash256> recent = new Dictionary<ulong, Hash256>();
        private readonly Queue<ulong> recentOrder = new Queue<ulong>();
        private Guid pendingTransfer;
        private bool busy;
        public SessionStamp Stamp { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 StateHash { get; private set; }
        public ReplicaPhase Phase { get; private set; }
        public bool CanEdit { get { return Phase == ReplicaPhase.Live; } }

        public ReplicaSession(SessionStamp stamp, Guid hostConnection, IReplicaWorld world, DiagnosticRing diagnostics)
        {
            Check.Stamp(stamp);
            if (hostConnection == Guid.Empty) throw new ArgumentException("Missing authenticated host.", "hostConnection");
            if (world == null || diagnostics == null) throw new ArgumentNullException("world");
            Stamp = stamp; this.hostConnection = hostConnection; this.world = world; this.diagnostics = diagnostics;
            Phase = ReplicaPhase.NeedsSnapshot;
        }

        private void Enter()
        {
            owner.AssertCurrent();
            if (busy) throw new InvalidOperationException("Reentrant replica access is not permitted.");
        }

        public Guid BeginSnapshot()
        {
            Enter();
            if (Phase == ReplicaPhase.Disconnected) throw new InvalidOperationException("Create a new session after disconnect.");
            pendingTransfer = Guid.NewGuid();
            Phase = ReplicaPhase.NeedsSnapshot;
            return pendingTransfer;
        }

        public bool InstallSnapshot(Guid source, WorldSnapshot snapshot)
        {
            Enter();
            if (Phase == ReplicaPhase.Disconnected || source != hostConnection || snapshot == null ||
                !snapshot.Stamp.Equals(Stamp) || pendingTransfer == Guid.Empty || snapshot.TransferId != pendingTransfer ||
                snapshot.Revision < Revision || Phase != ReplicaPhase.NeedsSnapshot)
                return false;
            busy = true;
            try
            {
                if (!snapshot.Image.HasValidContent()) return false;
                world.Install(snapshot.Image);
                if (!world.StateHash.Equals(snapshot.Image.StateHash)) throw new InvalidOperationException("Installed state mismatch.");
                Revision = snapshot.Revision;
                StateHash = snapshot.Image.StateHash;
                recent.Clear(); recentOrder.Clear();
                pendingTransfer = Guid.Empty;
                Phase = ReplicaPhase.CatchingUp;
                diagnostics.Record(DiagnosticCode.SnapshotInstalled, Stamp, source, Revision, 0);
                return true;
            }
            catch (Exception)
            {
                RequireSnapshot(DiagnosticCode.WorldFault);
                return false;
            }
            finally { busy = false; }
        }

        public ReplicaDecision Receive(Guid authenticatedSource, Commit commit)
        {
            Enter();
            if (commit == null) throw new ArgumentNullException("commit");
            if (authenticatedSource != hostConnection) return ReplicaDecision.WrongSource;
            if (!commit.Stamp.Equals(Stamp)) return ReplicaDecision.WrongSession;
            if (Phase == ReplicaPhase.Disconnected) return ReplicaDecision.Disconnected;
            if (Phase == ReplicaPhase.NeedsSnapshot) return ReplicaDecision.NeedsSnapshot;
            if (commit.Revision <= Revision)
            {
                Hash256 fingerprint;
                if (recent.TryGetValue(commit.Revision, out fingerprint) && !fingerprint.Equals(commit.Fingerprint))
                {
                    RequireSnapshot(DiagnosticCode.HashMismatch);
                    return ReplicaDecision.HashMismatch;
                }
                diagnostics.Record(DiagnosticCode.Duplicate, Stamp, authenticatedSource, Revision, commit.RequestId);
                return ReplicaDecision.Duplicate;
            }
            if (Revision == ulong.MaxValue || commit.Revision != Revision + 1)
            {
                Phase = ReplicaPhase.CatchingUp;
                diagnostics.Record(DiagnosticCode.Gap, Stamp, authenticatedSource, Revision, commit.RequestId);
                return ReplicaDecision.Gap;
            }
            busy = true;
            try
            {
                if (!StateHash.Equals(commit.BeforeHash) || !world.StateHash.Equals(StateHash))
                {
                    RequireSnapshot(DiagnosticCode.HashMismatch);
                    return ReplicaDecision.HashMismatch;
                }
                world.Apply(commit.Payload, commit.AfterHash);
                if (!world.StateHash.Equals(commit.AfterHash)) throw new InvalidOperationException("Post-apply digest mismatch.");
                Revision = commit.Revision;
                StateHash = commit.AfterHash;
                recent.Add(Revision, commit.Fingerprint);
                recentOrder.Enqueue(Revision);
                if (recentOrder.Count > Limits.ReceiptsPerPeer) recent.Remove(recentOrder.Dequeue());
                diagnostics.Record(DiagnosticCode.Committed, Stamp, authenticatedSource, Revision, commit.RequestId);
                return ReplicaDecision.Applied;
            }
            catch (Exception)
            {
                RequireSnapshot(DiagnosticCode.WorldFault);
                return ReplicaDecision.ApplyFailed;
            }
            finally { busy = false; }
        }

        /// <summary>A separately authenticated host barrier, not a local "download complete" flag.</summary>
        public bool MarkReady(Guid source, SessionStamp stamp, ulong revision, Hash256 hash)
        {
            Enter();
            if (source != hostConnection || !stamp.Equals(Stamp) || revision != Revision || hash == null ||
                (Phase != ReplicaPhase.CatchingUp && Phase != ReplicaPhase.Live)) return false;
            busy = true;
            try
            {
                if (!hash.Equals(StateHash) || !world.StateHash.Equals(StateHash))
                {
                    RequireSnapshot(DiagnosticCode.HashMismatch);
                    return false;
                }
                Phase = ReplicaPhase.Live;
                return true;
            }
            catch (Exception) { RequireSnapshot(DiagnosticCode.WorldFault); return false; }
            finally { busy = false; }
        }

        public void Disconnect()
        {
            Enter();
            pendingTransfer = Guid.Empty;
            recent.Clear(); recentOrder.Clear();
            Phase = ReplicaPhase.Disconnected;
            diagnostics.Record(DiagnosticCode.Disconnected, Stamp, hostConnection, Revision, 0);
        }

        private void RequireSnapshot(DiagnosticCode reason)
        {
            Phase = ReplicaPhase.NeedsSnapshot;
            pendingTransfer = Guid.Empty;
            diagnostics.Record(reason, Stamp, hostConnection, Revision, 0);
        }
    }
}
