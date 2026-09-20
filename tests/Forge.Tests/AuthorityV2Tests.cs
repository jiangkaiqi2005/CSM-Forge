using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class AuthorityV2Tests
    {
        private sealed class ValueAuthorityDomain : IAuthorityDomainV2
        {
            private byte value;
            public ushort DomainId { get { return 7; } }
            public Hash256 StateRoot { get { return Hash256.Compute(new byte[] { value }); } }

            public DomainExecutionV2 ExecutePlayer(byte[] payload)
            {
                if (payload == null || payload.Length != 1 || payload[0] > 100)
                    return DomainExecutionV2.Rejected();
                value = payload[0];
                return DomainExecutionV2.Success(new byte[] { value }, StateRoot);
            }

            public void Observe(byte next) { value = next; }
        }

        private sealed class ValueReplicaDomain : IReplicaDomainV2
        {
            private byte value;
            public ushort DomainId { get { return 7; } }
            public Hash256 StateRoot { get { return Hash256.Compute(new byte[] { value }); } }

            public void ApplyAbsolute(byte[] delta, Hash256 expectedAfterRoot)
            {
                if (delta == null || delta.Length != 1 || delta[0] > 100)
                    throw new ArgumentException("bad delta");
                value = delta[0];
                if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("root mismatch");
            }
        }

        [Case]
        public static void PlayerCommitReplicatesAndAppliedAckUsesAggregateRoot()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            ValueAuthorityDomain hostDomain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { hostDomain });
            Guid binding = Guid.NewGuid();
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            Assert.True(host.RegisterConnection(binding, member, true, 1));
            Assert.True(host.SetLive(binding, true));

            PlayerIntentV2 intent = new PlayerIntentV2(stamp, member, 1, 1, hostDomain.DomainId,
                hostDomain.StateRoot, new byte[] { 10 });
            AuthoritySubmitResultV2 result = host.Submit(binding, intent);
            Assert.Equal(AuthoritySubmitDecisionV2.Committed, result.Decision);
            Assert.True(result.Batch != null);
            Assert.Equal((ulong)1, result.Batch.Revision);

            AuthoritySubmitResultV2 duplicate = host.Submit(binding, intent);
            Assert.True(duplicate.FromCache);
            Assert.Equal(result.Batch.Fingerprint, duplicate.Batch.Fingerprint);

            ValueReplicaDomain clientDomain = new ValueReplicaDomain();
            ReplicaCoordinatorV2 client = new ReplicaCoordinatorV2(stamp,
                new IReplicaDomainV2[] { clientDomain }, 0);
            Assert.Equal(ReplicaDecisionV2.Applied, client.Receive(result.Batch));
            Assert.Equal(host.CurrentRoot, client.CurrentRoot);
            AppliedAck ack = client.CreateAppliedAck(binding, 0);
            Assert.True(ack != null);
            Assert.True(host.RecordApplied(ack));
        }

        [Case]
        public static void SameOperationIdentityWithDifferentPayloadIsRejected()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 2);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Guid binding = Guid.NewGuid();
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            host.RegisterConnection(binding, member, true, 1);
            host.SetLive(binding, true);
            Hash256 initial = domain.StateRoot;
            PlayerIntentV2 first = new PlayerIntentV2(stamp, member, 1, 1, 7, initial, new byte[] { 3 });
            Assert.Equal(AuthoritySubmitDecisionV2.Committed, host.Submit(binding, first).Decision);
            PlayerIntentV2 conflict = new PlayerIntentV2(stamp, member, 1, 1, 7, initial, new byte[] { 4 });
            Assert.Equal(AuthoritySubmitDecisionV2.OperationIdentityConflict, host.Submit(binding, conflict).Decision);
        }

        [Case]
        public static void SimulationPublicationDoesNotPretendToBePlayerWork()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 3);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Hash256 before = domain.StateRoot;
            domain.Observe(22);
            Hash256 after = domain.StateRoot;
            AuthorityBatch batch = host.PublishObserved(AuthorityOriginKind.Simulation, 7, before, after, new byte[] { 22 });
            Assert.True(batch != null);
            Assert.Equal(AuthorityOriginKind.Simulation, batch.OriginKind);
            Assert.Equal(Guid.Empty, batch.MemberId);
            Assert.Equal((ulong)0, batch.OperationCounter);
            Assert.Equal((ulong)1, host.Revision);
        }

        [Case]
        public static void ReplicaGapDoesNotApplyFutureBatch()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 4);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Guid binding = Guid.NewGuid();
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            host.RegisterConnection(binding, member, true, 1);
            host.SetLive(binding, true);
            AuthorityBatch first = host.Submit(binding, new PlayerIntentV2(stamp, member, 1, 1, 7,
                domain.StateRoot, new byte[] { 1 })).Batch;
            AuthorityBatch second = host.Submit(binding, new PlayerIntentV2(stamp, member, 2, 1, 7,
                domain.StateRoot, new byte[] { 2 })).Batch;

            ReplicaCoordinatorV2 client = new ReplicaCoordinatorV2(stamp,
                new IReplicaDomainV2[] { new ValueReplicaDomain() }, 0);
            Assert.Equal(ReplicaDecisionV2.Gap, client.Receive(second));
            Assert.Equal((ulong)0, client.Revision);
            Assert.Equal(ReplicaDecisionV2.Applied, client.Receive(first));
            Assert.Equal(ReplicaDecisionV2.Applied, client.Receive(second));
        }

        [Case]
        public static void StaleDomainRootProducesExplicitReadConflict()
        {
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 5);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Guid binding = Guid.NewGuid();
            MemberIdentity member = new MemberIdentity(Guid.NewGuid(), 1);
            host.RegisterConnection(binding, member, true, 1);
            host.SetLive(binding, true);
            Hash256 stale = Hash256.Compute(new byte[] { 99 });
            AuthoritySubmitResultV2 result = host.Submit(binding,
                new PlayerIntentV2(stamp, member, 1, 1, 7, stale, new byte[] { 8 }));
            Assert.Equal(AuthoritySubmitDecisionV2.ReadConflict, result.Decision);
            Assert.Equal((ulong)0, host.Revision);
        }

        [Case] public static void RootNeutralObservedPublishIsANoOpNotAFailure()
        {
            // Regression for the in-game fence on a routine road edit ("observed-net-change-
            // could-not-commit"). PublishObserved returns null BOTH when it refuses (a real
            // bridge failure) and when the observed before/after roots are equal (nothing to
            // publish, by design). A caller that treats every null as a failure fences the room
            // on a no-op. This pins the coordinator's half of the contract: equal roots -> null,
            // no fence, session still usable.
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 3);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Guid binding = Guid.NewGuid();
            Assert.True(host.RegisterConnection(binding, new MemberIdentity(Guid.NewGuid(), 1), true, 1));
            Assert.True(host.SetLive(binding, true));

            // advance the committed root to H(42) with a real publish first
            Hash256 baseline = domain.StateRoot;      // H(0), what the coordinator committed
            domain.Observe(42);
            Hash256 root = domain.StateRoot;
            Assert.True(host.PublishObserved(AuthorityOriginKind.Simulation, 7, baseline, root, new byte[] { 1 }) != null);

            // now the no-op case: baseline == observed after-root
            AuthorityBatch batch = host.PublishObserved(AuthorityOriginKind.Simulation, 7, root, root, new byte[] { 2 });
            Assert.True(batch == null);      // nothing to publish
            Assert.True(!host.IsFenced);     // and emphatically not a failure

            // The session keeps working afterwards: a real change still publishes.
            domain.Observe(43);
            AuthorityBatch real = host.PublishObserved(AuthorityOriginKind.Simulation, 7, root, domain.StateRoot, new byte[] { 3 });
            Assert.True(real != null);
            Assert.True(!host.IsFenced);
        }

        [Case] public static void PublishRejectsAStaleBaselineAndFences()
        {
            // The counterpart: a baseline that does not match the committed root IS a real bridge
            // failure and must fence rather than silently publish a divergent world.
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 4);
            ValueAuthorityDomain domain = new ValueAuthorityDomain();
            AuthorityCoordinatorV2 host = new AuthorityCoordinatorV2(stamp, new IAuthorityDomainV2[] { domain });
            Guid binding = Guid.NewGuid();
            host.RegisterConnection(binding, new MemberIdentity(Guid.NewGuid(), 1), true, 1);
            host.SetLive(binding, true);

            domain.Observe(10);
            Hash256 wrongBaseline = Hash256.Compute(new byte[] { 99 });
            AuthorityBatch batch = host.PublishObserved(AuthorityOriginKind.Simulation, 7, wrongBaseline, domain.StateRoot, new byte[] { 3 });
            Assert.True(batch == null);
            Assert.True(host.IsFenced);
        }
    }
}
