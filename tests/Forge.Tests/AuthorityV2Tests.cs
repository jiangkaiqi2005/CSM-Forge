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
    }
}
