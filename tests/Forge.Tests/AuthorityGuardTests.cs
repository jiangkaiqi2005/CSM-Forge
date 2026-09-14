using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class AuthorityGuardTests
    {
        [Case] public static void UnjournaledHostMutationCannotBeHiddenByNextIntent()
        {
            Scenario s = new Scenario();
            s.Authority.Execute(ParameterWorld.Set(2, 99));
            Assert.Equal(SubmitDecision.Faulted, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
            Assert.Equal(0, s.Authority.Get(1));
            Assert.Equal((ulong)0, s.Host.Revision);
            Assert.True(s.Host.IsFenced);
        }

        [Case] public static void UnjournaledHostMutationCannotBecomeSnapshotBaseline()
        {
            Scenario s = new Scenario();
            s.Authority.Execute(ParameterWorld.Set(2, 99));
            Assert.Throws<InvalidOperationException>(delegate { s.Host.CaptureSnapshot(Guid.NewGuid()); });
            Assert.True(s.Host.IsFenced);
        }

        [Case] public static void UnjournaledHostMutationCannotApproveJoin()
        {
            Scenario s = new Scenario();
            s.Authority.Execute(ParameterWorld.Set(2, 99));
            Assert.True(!s.Host.CompleteJoin(s.ClientConnection, s.Stamp, 0, s.Authority.StateHash));
            Assert.True(s.Host.IsFenced);
        }

        [Case] public static void PeriodicGuardDetectsHostDriftWithoutAnyPlayerInput()
        {
            Scenario s = new Scenario();
            Assert.True(s.Host.VerifyWorld());
            s.Authority.Execute(ParameterWorld.Set(2, 99));
            Assert.True(!s.Host.VerifyWorld());
            Assert.True(s.Host.IsFenced);
        }

        [Case] public static void ThreeReplicasConvergeAfterSeededLossAndDuplicateDelivery()
        {
            Scenario s = new Scenario();
            ReplicaSession[] clients = new ReplicaSession[3];
            ParameterWorld[] worlds = new ParameterWorld[3];
            for (int i = 0; i < clients.Length; i++)
            {
                worlds[i] = new ParameterWorld();
                clients[i] = new ReplicaSession(s.Stamp, s.HostConnection, worlds[i], s.Log);
                Guid ticket = clients[i].BeginSnapshot();
                clients[i].InstallSnapshot(s.HostConnection, s.Host.CaptureSnapshot(ticket));
                clients[i].MarkReady(s.HostConnection, s.Stamp, 0, s.Host.StateHash);
            }
            Random random = new Random(616);
            for (ulong request = 1; request <= 400; request++)
            {
                Commit commit = s.Change(request, (int)request);
                foreach (ReplicaSession client in clients)
                {
                    if (random.Next(100) < 35) continue;
                    client.Receive(s.HostConnection, FrameCodec.DecodeCommit(FrameCodec.EncodeCommit(commit)));
                    if (random.Next(100) < 20) client.Receive(s.HostConnection, commit);
                }
                if (request % 17 == 0) CatchUp(s, clients);
            }
            CatchUp(s, clients);
            for (int i = 0; i < clients.Length; i++)
            {
                Assert.Equal(s.Host.Revision, clients[i].Revision);
                Assert.True(s.Host.StateHash.Equals(worlds[i].StateHash));
                Assert.True(clients[i].CanEdit);
            }
        }

        private static void CatchUp(Scenario s, ReplicaSession[] clients)
        {
            foreach (ReplicaSession client in clients)
            {
                Commit[] commits;
                Assert.True(s.Host.TryReadJournal(client.Revision, out commits));
                foreach (Commit commit in commits)
                    Assert.Equal(ReplicaDecision.Applied, client.Receive(s.HostConnection, commit));
                Assert.True(client.MarkReady(s.HostConnection, s.Stamp, s.Host.Revision, s.Host.StateHash));
            }
        }
    }
}
