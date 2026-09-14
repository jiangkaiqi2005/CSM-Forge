using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public sealed class FailingAuthority : IAuthorityWorld
    {
        public readonly ParameterWorld Inner = new ParameterWorld();
        public int Calls;
        public bool RejectAfterMutation;
        public Hash256 StateHash { get { return Inner.StateHash; } }
        public WorldImage Capture() { return Inner.Capture(); }
        public WorldExecution Execute(byte[] intent)
        {
            Calls++;
            Inner.Execute(intent);
            if (RejectAfterMutation) return WorldExecution.Rejected();
            throw new InvalidOperationException("Injected failure after world mutation.");
        }
    }

    public sealed class FailingReplica : IReplicaWorld
    {
        public readonly ParameterWorld Inner = new ParameterWorld();
        public int Calls;
        public Hash256 StateHash { get { return Inner.StateHash; } }
        public void Install(WorldImage image) { Inner.Install(image); }
        public void Apply(byte[] delta, Hash256 hash)
        {
            Calls++;
            Inner.Apply(delta, hash);
            throw new InvalidOperationException("Injected failure after partial adapter work.");
        }
    }

    public static class FailureTests
    {
        [Case] public static void AmbiguousHostMutationFencesRoomAndCannotBecomeSnapshot()
        {
            FailingAuthority world = new FailingAuthority();
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            HostSession host = new HostSession(stamp, world, new DiagnosticRing(8));
            Guid peer = Guid.NewGuid();
            host.RegisterPeer(peer, true); host.CompleteJoin(peer, stamp, 0, world.StateHash);
            Intent intent = new Intent(stamp, 1, 0, ParameterWorld.Set(1, 10));
            Assert.Equal(SubmitDecision.Faulted, host.Submit(peer, intent).Decision);
            Assert.True(host.IsFenced);
            Assert.Equal((ulong)0, host.Revision);
            Assert.Equal(0, host.JournalCount);
            Assert.Equal(10, world.Inner.Get(1)); // no fictitious rollback claim
            Assert.Equal(SubmitDecision.Faulted, host.Submit(peer, intent).Decision);
            Assert.Equal(1, world.Calls);
            Assert.Throws<InvalidOperationException>(delegate { host.CaptureSnapshot(Guid.NewGuid()); });
        }

        [Case] public static void AdapterCannotLieAboutRejectionWithoutMutation()
        {
            FailingAuthority world = new FailingAuthority { RejectAfterMutation = true };
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            HostSession host = new HostSession(stamp, world, new DiagnosticRing(8));
            Guid peer = Guid.NewGuid();
            host.RegisterPeer(peer, true); host.CompleteJoin(peer, stamp, 0, world.StateHash);
            Assert.Equal(SubmitDecision.Faulted, host.Submit(peer,
                new Intent(stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
            Assert.True(host.IsFenced);
        }

        [Case] public static void ReplicaPartialFailureRequiresSnapshotNotFlagReset()
        {
            Scenario s = new Scenario();
            FailingReplica world = new FailingReplica();
            ReplicaSession client = new ReplicaSession(s.Stamp, s.HostConnection, world, s.Log);
            Guid ticket = client.BeginSnapshot();
            client.InstallSnapshot(s.HostConnection, s.Host.CaptureSnapshot(ticket));
            client.MarkReady(s.HostConnection, s.Stamp, 0, s.Authority.StateHash);
            Commit first = s.Change(1, 10);
            Assert.Equal(ReplicaDecision.ApplyFailed, client.Receive(s.HostConnection, first));
            Assert.Equal(ReplicaPhase.NeedsSnapshot, client.Phase);
            Assert.Equal((ulong)0, client.Revision);
            Assert.Equal(10, world.Inner.Get(1));
            Assert.True(!client.MarkReady(s.HostConnection, s.Stamp, 0, client.StateHash));
            Assert.Equal(ReplicaDecision.NeedsSnapshot, client.Receive(s.HostConnection, s.Change(2, 11)));
            Assert.Equal(1, world.Calls);
            ticket = client.BeginSnapshot();
            Assert.True(client.InstallSnapshot(s.HostConnection, s.Host.CaptureSnapshot(ticket)));
            Assert.True(client.MarkReady(s.HostConnection, s.Stamp, 2, s.Authority.StateHash));
            Assert.Equal(11, world.Inner.Get(1));
        }
    }
}
