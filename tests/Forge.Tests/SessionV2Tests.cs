using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class SessionV2Tests
    {
        private static Hash256 H(byte value) { return Hash256.Compute(new byte[] { value }); }

        [Case]
        public static void ParallelJoinsAreIndependentAndCancellationIsLocal()
        {
            long now = 1000;
            JoinCoordinator coordinator = new JoinCoordinator(delegate { return now; });
            MemberIdentity memberA = new MemberIdentity(Guid.NewGuid(), 1);
            MemberIdentity memberB = new MemberIdentity(Guid.NewGuid(), 1);
            JoinIdentity a = coordinator.StartJoin(memberA, Guid.NewGuid(), 10, H(10));
            JoinIdentity b = coordinator.StartJoin(memberB, Guid.NewGuid(), 10, H(10));

            Assert.True(coordinator.MarkLoading(a));
            Assert.True(coordinator.MarkLoading(b));
            Assert.True(coordinator.MarkSnapshotInstalled(a, 10, H(10)));
            Assert.True(coordinator.MarkSnapshotInstalled(b, 10, H(10)));
            Assert.True(coordinator.Cancel(b));

            Assert.Equal(JoinPhase.CatchingUp, coordinator.Inspect(a).Phase);
            Assert.Equal(JoinPhase.Closed, coordinator.Inspect(b).Phase);
            Assert.Equal(2, coordinator.Count);
        }

        [Case]
        public static void BarrierUsesFixedHistoricalWatermarkWhileHostMovesOn()
        {
            long now = 1000;
            JoinCoordinator coordinator = new JoinCoordinator(delegate { return now; });
            JoinIdentity join = coordinator.StartJoin(new MemberIdentity(Guid.NewGuid(), 1), Guid.NewGuid(), 5, H(5));
            Assert.True(coordinator.MarkLoading(join));
            Assert.True(coordinator.MarkSnapshotInstalled(join, 5, H(5)));
            Assert.True(coordinator.ReportApplied(join, 20, H(20)));

            ReplayBarrier barrier = coordinator.IssueBarrier(join, 25, H(25), 10000);
            Assert.True(barrier != null);
            // Host may now be at 40; the acknowledgement is still for fixed H=25.
            Assert.True(coordinator.ReportApplied(join, 30, H(30)));
            Assert.True(coordinator.AcknowledgeBarrier(join, barrier.BarrierId, 25, H(25)));

            ActivationGrant grant = coordinator.IssueActivation(join, 40, H(40), 7, 10000);
            Assert.True(grant != null);
            Assert.True(coordinator.ReportApplied(join, 40, H(40)));
            Assert.True(coordinator.Activate(join, grant.GrantId, 40, H(40)));
            Assert.True(coordinator.Inspect(join).CanEdit);
        }

        [Case]
        public static void OldJoinGenerationCannotControlReplacementJoin()
        {
            JoinCoordinator coordinator = new JoinCoordinator(delegate { return 10; });
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            Guid connection = Guid.NewGuid();
            JoinIdentity oldJoin = coordinator.StartJoin(member, connection, 0, H(1));
            JoinIdentity newJoin = coordinator.StartJoin(member, Guid.NewGuid(), 0, H(1));

            Assert.True(oldJoin.Generation != newJoin.Generation);
            Assert.True(coordinator.Cancel(oldJoin));
            Assert.Equal(JoinPhase.Downloading, coordinator.Inspect(newJoin).Phase);
            Assert.True(!coordinator.MarkLoading(new JoinIdentity(oldJoin.JoinId, newJoin.Generation, oldJoin.ConnectionBinding)));
        }

        [Case]
        public static void ExpiredBarrierRequiresRecoveryInsteadOfActivating()
        {
            long now = 100;
            JoinCoordinator coordinator = new JoinCoordinator(delegate { return now; });
            JoinIdentity join = coordinator.StartJoin(new MemberIdentity(Guid.NewGuid(), 1), Guid.NewGuid(), 1, H(1));
            Assert.True(coordinator.MarkLoading(join));
            Assert.True(coordinator.MarkSnapshotInstalled(join, 1, H(1)));
            ReplayBarrier barrier = coordinator.IssueBarrier(join, 2, H(2), 50);
            Assert.True(barrier != null);
            now = 151;
            Assert.Equal(1, coordinator.Expire());
            Assert.Equal(JoinPhase.RecoveryPending, coordinator.Inspect(join).Phase);
            Assert.True(!coordinator.AcknowledgeBarrier(join, barrier.BarrierId, 2, H(2)));
        }

        [Case]
        public static void AuthorityBatchDoesNotFakePlayerIdentityForSimulation()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            AuthorityBatch batch = new AuthorityBatch(stamp, 1, AuthorityOriginKind.Simulation,
                Guid.Empty, 0, 0, 7, H(1), H(2), new byte[] { 9 });
            Assert.Equal(AuthorityOriginKind.Simulation, batch.OriginKind);
            Assert.Equal(Guid.Empty, batch.MemberId);
            Assert.Throws<ArgumentException>(delegate
            {
                new AuthorityBatch(stamp, 2, AuthorityOriginKind.PlayerIntent,
                    Guid.Empty, 0, 0, 7, H(2), H(3), new byte[] { 10 });
            });
        }
    }
}
