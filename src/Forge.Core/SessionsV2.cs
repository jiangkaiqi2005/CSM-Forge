using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    public struct MemberIdentity : IEquatable<MemberIdentity>
    {
        public readonly Guid MemberId;
        public readonly uint Generation;

        public MemberIdentity(Guid memberId, uint generation)
        {
            if (memberId == Guid.Empty || generation == 0)
                throw new ArgumentException("Member identity is incomplete.");
            MemberId = memberId;
            Generation = generation;
        }

        public bool IsValid { get { return MemberId != Guid.Empty && Generation != 0; } }
        public bool Equals(MemberIdentity other) { return MemberId == other.MemberId && Generation == other.Generation; }
        public override bool Equals(object obj) { return obj is MemberIdentity && Equals((MemberIdentity)obj); }
        public override int GetHashCode() { return MemberId.GetHashCode() ^ Generation.GetHashCode(); }
    }

    public struct JoinIdentity : IEquatable<JoinIdentity>
    {
        public readonly Guid JoinId;
        public readonly uint Generation;
        public readonly Guid ConnectionBinding;

        public JoinIdentity(Guid joinId, uint generation, Guid connectionBinding)
        {
            if (joinId == Guid.Empty || generation == 0 || connectionBinding == Guid.Empty)
                throw new ArgumentException("Join identity is incomplete.");
            JoinId = joinId;
            Generation = generation;
            ConnectionBinding = connectionBinding;
        }

        public bool IsValid { get { return JoinId != Guid.Empty && Generation != 0 && ConnectionBinding != Guid.Empty; } }
        public bool Equals(JoinIdentity other)
        {
            return JoinId == other.JoinId && Generation == other.Generation && ConnectionBinding == other.ConnectionBinding;
        }
        public override bool Equals(object obj) { return obj is JoinIdentity && Equals((JoinIdentity)obj); }
        public override int GetHashCode() { return JoinId.GetHashCode() ^ Generation.GetHashCode() ^ ConnectionBinding.GetHashCode(); }
    }

    public enum JoinPhase
    {
        Downloading,
        Loading,
        CatchingUp,
        AwaitingBarrierAck,
        AwaitingActivation,
        Live,
        RecoveryPending,
        Cancelling,
        Closed
    }

    public sealed class ReplayBarrier
    {
        public Guid BarrierId { get; private set; }
        public JoinIdentity Join { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }
        public long DeadlineMilliseconds { get; private set; }

        internal ReplayBarrier(JoinIdentity join, ulong revision, Hash256 root, long deadline)
        {
            BarrierId = Guid.NewGuid();
            Join = join;
            Revision = revision;
            Root = root;
            DeadlineMilliseconds = deadline;
        }
    }

    public sealed class ActivationGrant
    {
        public Guid GrantId { get; private set; }
        public JoinIdentity Join { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }
        public ulong PermissionVersion { get; private set; }
        public long DeadlineMilliseconds { get; private set; }

        internal ActivationGrant(JoinIdentity join, ulong revision, Hash256 root, ulong permissionVersion, long deadline)
        {
            GrantId = Guid.NewGuid();
            Join = join;
            Revision = revision;
            Root = root;
            PermissionVersion = permissionVersion;
            DeadlineMilliseconds = deadline;
        }
    }

    public sealed class JoinSnapshot
    {
        public MemberIdentity Member { get; internal set; }
        public JoinIdentity Join { get; internal set; }
        public JoinPhase Phase { get; internal set; }
        public ulong BaselineRevision { get; internal set; }
        public Hash256 BaselineRoot { get; internal set; }
        public ulong AppliedRevision { get; internal set; }
        public Hash256 AppliedRoot { get; internal set; }
        public ReplayBarrier Barrier { get; internal set; }
        public ActivationGrant Grant { get; internal set; }
        public bool CanEdit { get { return Phase == JoinPhase.Live; } }
    }

    /// <summary>
    /// Owner-thread join state machine. Each join is independent; a shared snapshot may be
    /// referenced externally, but cancellation or expiry here never mutates another join.
    /// </summary>
    public sealed class JoinCoordinator
    {
        private sealed class State
        {
            public MemberIdentity Member;
            public JoinIdentity Join;
            public JoinPhase Phase;
            public ulong BaselineRevision;
            public Hash256 BaselineRoot;
            public ulong AppliedRevision;
            public Hash256 AppliedRoot;
            public ReplayBarrier Barrier;
            public bool BarrierAcked;
            public ActivationGrant Grant;
        }

        private readonly ThreadOwner owner = new ThreadOwner();
        private readonly Func<long> clock;
        private readonly Dictionary<Guid, State> joins = new Dictionary<Guid, State>();
        private readonly Dictionary<Guid, uint> generations = new Dictionary<Guid, uint>();

        public JoinCoordinator(Func<long> monotonicMilliseconds)
        {
            if (monotonicMilliseconds == null) throw new ArgumentNullException("monotonicMilliseconds");
            clock = monotonicMilliseconds;
        }

        public int Count { get { owner.AssertCurrent(); return joins.Count; } }

        public JoinIdentity StartJoin(MemberIdentity member, Guid connectionBinding, ulong baselineRevision, Hash256 baselineRoot)
        {
            owner.AssertCurrent();
            if (!member.IsValid || connectionBinding == Guid.Empty || baselineRoot == null)
                throw new ArgumentException("Join start data is incomplete.");
            if (joins.Count >= Limits.Peers * 2) throw new InvalidOperationException("Join capacity reached.");

            uint generation;
            if (!generations.TryGetValue(member.MemberId, out generation)) generation = 0;
            if (generation == uint.MaxValue) throw new InvalidOperationException("Join generation exhausted.");
            generation++;
            generations[member.MemberId] = generation;

            JoinIdentity identity = new JoinIdentity(Guid.NewGuid(), generation, connectionBinding);
            joins.Add(identity.JoinId, new State
            {
                Member = member,
                Join = identity,
                Phase = JoinPhase.Downloading,
                BaselineRevision = baselineRevision,
                BaselineRoot = baselineRoot,
                AppliedRevision = 0,
                AppliedRoot = null
            });
            return identity;
        }

        public bool MarkLoading(JoinIdentity join)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.Downloading) return false;
            state.Phase = JoinPhase.Loading;
            return true;
        }

        public bool MarkSnapshotInstalled(JoinIdentity join, ulong revision, Hash256 root)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.Loading || root == null ||
                revision != state.BaselineRevision || !root.Equals(state.BaselineRoot)) return false;
            state.AppliedRevision = revision;
            state.AppliedRoot = root;
            state.Phase = JoinPhase.CatchingUp;
            return true;
        }

        public bool ReportApplied(JoinIdentity join, ulong revision, Hash256 root)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || root == null || state.Phase == JoinPhase.Closed ||
                state.Phase == JoinPhase.Cancelling || state.Phase == JoinPhase.RecoveryPending ||
                state.Phase == JoinPhase.Downloading || state.Phase == JoinPhase.Loading) return false;
            if (revision < state.AppliedRevision) return false;
            if (revision == state.AppliedRevision && state.AppliedRoot != null && !state.AppliedRoot.Equals(root)) return false;
            state.AppliedRevision = revision;
            state.AppliedRoot = root;
            return true;
        }

        public ReplayBarrier IssueBarrier(JoinIdentity join, ulong hostRevision, Hash256 hostRoot, int ttlMilliseconds)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.CatchingUp || hostRoot == null ||
                hostRevision < state.AppliedRevision || ttlMilliseconds < 1 || ttlMilliseconds > 600000) return null;
            ReplayBarrier barrier = new ReplayBarrier(join, hostRevision, hostRoot, checked(clock() + ttlMilliseconds));
            state.Barrier = barrier;
            state.BarrierAcked = false;
            state.Phase = JoinPhase.AwaitingBarrierAck;
            return barrier;
        }

        public bool AcknowledgeBarrier(JoinIdentity join, Guid barrierId, ulong revision, Hash256 root)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.AwaitingBarrierAck || state.Barrier == null ||
                barrierId == Guid.Empty || barrierId != state.Barrier.BarrierId || root == null ||
                clock() > state.Barrier.DeadlineMilliseconds || revision != state.Barrier.Revision ||
                !root.Equals(state.Barrier.Root) || state.AppliedRevision < revision) return false;
            state.BarrierAcked = true;
            state.Phase = JoinPhase.AwaitingActivation;
            return true;
        }

        public ActivationGrant IssueActivation(JoinIdentity join, ulong hostRevision, Hash256 hostRoot,
            ulong permissionVersion, int ttlMilliseconds)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.AwaitingActivation || !state.BarrierAcked ||
                state.Barrier == null || hostRoot == null || hostRevision < state.Barrier.Revision ||
                permissionVersion == 0 || ttlMilliseconds < 1 || ttlMilliseconds > 600000) return null;
            ActivationGrant grant = new ActivationGrant(join, hostRevision, hostRoot, permissionVersion,
                checked(clock() + ttlMilliseconds));
            state.Grant = grant;
            return grant;
        }

        public bool Activate(JoinIdentity join, Guid grantId, ulong revision, Hash256 root)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase != JoinPhase.AwaitingActivation || state.Grant == null ||
                grantId == Guid.Empty || grantId != state.Grant.GrantId || root == null ||
                clock() > state.Grant.DeadlineMilliseconds || revision != state.Grant.Revision ||
                !root.Equals(state.Grant.Root) || state.AppliedRevision < revision) return false;
            state.Phase = JoinPhase.Live;
            return true;
        }

        public bool RequireRecovery(JoinIdentity join)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase == JoinPhase.Closed) return false;
            state.Barrier = null;
            state.Grant = null;
            state.BarrierAcked = false;
            state.Phase = JoinPhase.RecoveryPending;
            return true;
        }

        public bool Cancel(JoinIdentity join)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state) || state.Phase == JoinPhase.Closed) return false;
            state.Phase = JoinPhase.Cancelling;
            state.Barrier = null;
            state.Grant = null;
            state.BarrierAcked = false;
            state.Phase = JoinPhase.Closed;
            return true;
        }

        public int Expire()
        {
            owner.AssertCurrent();
            int expired = 0;
            long now = clock();
            foreach (State state in joins.Values)
            {
                bool deadline = state.Phase == JoinPhase.AwaitingBarrierAck && state.Barrier != null &&
                    now > state.Barrier.DeadlineMilliseconds;
                deadline |= state.Phase == JoinPhase.AwaitingActivation && state.Grant != null &&
                    now > state.Grant.DeadlineMilliseconds;
                if (!deadline) continue;
                state.Barrier = null;
                state.Grant = null;
                state.BarrierAcked = false;
                state.Phase = JoinPhase.RecoveryPending;
                expired++;
            }
            return expired;
        }

        public JoinSnapshot Inspect(JoinIdentity join)
        {
            owner.AssertCurrent();
            State state;
            if (!TryGet(join, out state)) return null;
            return new JoinSnapshot
            {
                Member = state.Member,
                Join = state.Join,
                Phase = state.Phase,
                BaselineRevision = state.BaselineRevision,
                BaselineRoot = state.BaselineRoot,
                AppliedRevision = state.AppliedRevision,
                AppliedRoot = state.AppliedRoot,
                Barrier = state.Barrier,
                Grant = state.Grant
            };
        }

        private bool TryGet(JoinIdentity join, out State state)
        {
            state = null;
            if (!join.IsValid || !joins.TryGetValue(join.JoinId, out state)) return false;
            return state.Join.Equals(join);
        }
    }
}
