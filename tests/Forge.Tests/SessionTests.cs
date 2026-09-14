using System;
using System.Threading;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public sealed class Scenario
    {
        public readonly SessionStamp Stamp = new SessionStamp(Guid.NewGuid(), 1);
        public readonly Guid ClientConnection = Guid.NewGuid();
        public readonly Guid HostConnection = Guid.NewGuid();
        public readonly ParameterWorld Authority = new ParameterWorld();
        public readonly ParameterWorld Replica = new ParameterWorld();
        public readonly DiagnosticRing Log = new DiagnosticRing(64);
        public readonly HostSession Host;
        public readonly ReplicaSession Client;
        public Scenario()
        {
            Host = new HostSession(Stamp, Authority, Log);
            Client = new ReplicaSession(Stamp, HostConnection, Replica, Log);
            Assert.True(Host.RegisterPeer(ClientConnection, true));
            Recover();
        }
        public void Recover()
        {
            Guid ticket = Client.BeginSnapshot();
            Assert.True(Client.InstallSnapshot(HostConnection, Host.CaptureSnapshot(ticket)));
            Assert.True(Client.MarkReady(HostConnection, Stamp, Host.Revision, Authority.StateHash));
            Assert.True(Host.CompleteJoin(ClientConnection, Stamp, Host.Revision, Replica.StateHash));
        }
        public Commit Change(ulong request, int value)
        {
            Intent intent = new Intent(Stamp, request, Host.Revision, ParameterWorld.Set(1, value));
            SubmitResult result = Host.Submit(ClientConnection, FrameCodec.DecodeIntent(FrameCodec.EncodeIntent(intent)));
            Assert.Equal(SubmitDecision.Committed, result.Decision);
            return FrameCodec.DecodeCommit(FrameCodec.EncodeCommit(result.Commit));
        }
    }

    public static class SessionTests
    {
        [Case] public static void EndToEndUsesProductionIntentAndCommitCodecs()
        {
            Scenario s = new Scenario();
            Assert.Equal(ReplicaDecision.Applied, s.Client.Receive(s.HostConnection, s.Change(1, 23)));
            Assert.Equal(23, s.Replica.Get(1));
            Assert.True(s.Authority.StateHash.Equals(s.Client.StateHash));
        }

        [Case] public static void DuplicateIntentReturnsOriginalReceiptWithoutExecutingAgain()
        {
            Scenario s = new Scenario();
            Intent intent = new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10));
            SubmitResult first = s.Host.Submit(s.ClientConnection, intent);
            SubmitResult second = s.Host.Submit(s.ClientConnection, intent);
            Assert.True(second.FromCache);
            Assert.True(ReferenceEquals(first.Commit, second.Commit));
            Assert.Equal((ulong)1, s.Host.Revision);
        }

        [Case] public static void ReusedRequestIdentityWithDifferentPayloadIsRejected()
        {
            Scenario s = new Scenario();
            s.Change(1, 10);
            SubmitResult result = s.Host.Submit(s.ClientConnection, new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 11)));
            Assert.Equal(SubmitDecision.RequestIdConflict, result.Decision);
            Assert.Equal(10, s.Authority.Get(1));
        }

        [Case] public static void StalePreconditionHasAStableRejectedReceipt()
        {
            Scenario s = new Scenario();
            Intent invalid = new Intent(s.Stamp, 1, 99, ParameterWorld.Set(1, 10));
            Assert.Equal(SubmitDecision.RevisionConflict, s.Host.Submit(s.ClientConnection, invalid).Decision);
            Assert.True(s.Host.Submit(s.ClientConnection, invalid).FromCache);
            Assert.Equal(SubmitDecision.Committed, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 2, 0, ParameterWorld.Set(1, 10))).Decision);
        }

        [Case] public static void PeerCannotSkipItsRequestSequence()
        {
            Scenario s = new Scenario();
            Assert.Equal(SubmitDecision.OutOfOrder, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 2, 0, ParameterWorld.Set(1, 10))).Decision);
            Assert.Equal((ulong)0, s.Host.Revision);
        }

        [Case] public static void ClientCannotSpoofAnotherTransportConnection()
        {
            Scenario s = new Scenario();
            Assert.Equal(SubmitDecision.Unauthenticated, s.Host.Submit(Guid.NewGuid(),
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
        }

        [Case] public static void OldIncarnationCannotMutateNewSession()
        {
            Scenario s = new Scenario();
            SessionStamp old = new SessionStamp(s.Stamp.WorldId, 2);
            Assert.Equal(SubmitDecision.WrongSession, s.Host.Submit(s.ClientConnection,
                new Intent(old, 1, 0, ParameterWorld.Set(1, 10))).Decision);
        }

        [Case] public static void TransportConnectedIsNotWorldReady()
        {
            Scenario s = new Scenario();
            Guid second = Guid.NewGuid();
            Assert.True(s.Host.RegisterPeer(second, true));
            Assert.Equal(SubmitDecision.NotReady, s.Host.Submit(second,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
            Assert.True(!s.Host.CompleteJoin(second, s.Stamp, 1, s.Authority.StateHash));
        }

        [Case] public static void SpectatorsHaveNoWritePermission()
        {
            Scenario s = new Scenario();
            Guid spectator = Guid.NewGuid();
            Assert.True(s.Host.RegisterPeer(spectator, false));
            Assert.True(s.Host.CompleteJoin(spectator, s.Stamp, 0, s.Authority.StateHash));
            Assert.Equal(SubmitDecision.ReadOnly, s.Host.Submit(spectator,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
        }

        [Case] public static void HostOrdersConcurrentPlayerIntentsAndRejectsStaleOne()
        {
            Scenario s = new Scenario();
            Guid second = Guid.NewGuid();
            s.Host.RegisterPeer(second, true);
            s.Host.CompleteJoin(second, s.Stamp, 0, s.Authority.StateHash);
            s.Change(1, 10);
            Assert.Equal(SubmitDecision.RevisionConflict, s.Host.Submit(second,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(2, 11))).Decision);
            Assert.Equal(SubmitDecision.Committed, s.Host.Submit(second,
                new Intent(s.Stamp, 2, 1, ParameterWorld.Set(2, 11))).Decision);
            Assert.Equal((ulong)2, s.Host.Revision);
        }

        [Case] public static void DomainRejectionDoesNotConsumeAWorldRevision()
        {
            Scenario s = new Scenario();
            Assert.Equal(SubmitDecision.DomainRejected, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(99, 0))).Decision);
            Assert.Equal((ulong)0, s.Host.Revision);
            s.Change(2, 10);
        }

        [Case] public static void DuplicateCommitDoesNotApplyAgain()
        {
            Scenario s = new Scenario();
            Commit commit = s.Change(1, 10);
            Assert.Equal(ReplicaDecision.Applied, s.Client.Receive(s.HostConnection, commit));
            Assert.Equal(ReplicaDecision.Duplicate, s.Client.Receive(s.HostConnection, commit));
            Assert.Equal((ulong)1, s.Client.Revision);
        }

        [Case] public static void CommitMustComeFromAuthenticatedHost()
        {
            Scenario s = new Scenario();
            Assert.Equal(ReplicaDecision.WrongSource, s.Client.Receive(Guid.NewGuid(), s.Change(1, 10)));
            Assert.Equal((ulong)0, s.Client.Revision);
        }

        [Case] public static void AGapDisablesEditingUntilReplayAndMatchingBarrier()
        {
            Scenario s = new Scenario();
            Commit first = s.Change(1, 10);
            Commit second = s.Change(2, 11);
            Assert.Equal(ReplicaDecision.Gap, s.Client.Receive(s.HostConnection, second));
            Assert.True(!s.Client.CanEdit);
            Commit[] replay;
            Assert.True(s.Host.TryReadJournal(s.Client.Revision, out replay));
            Assert.Equal(2, replay.Length);
            Assert.Equal(ReplicaDecision.Applied, s.Client.Receive(s.HostConnection, first));
            Assert.Equal(ReplicaDecision.Applied, s.Client.Receive(s.HostConnection, second));
            Assert.True(!s.Client.CanEdit);
            Assert.True(s.Client.MarkReady(s.HostConnection, s.Stamp, 2, s.Authority.StateHash));
            Assert.True(s.Client.CanEdit);
        }

        [Case] public static void ConflictingDuplicateRequiresSnapshot()
        {
            Scenario s = new Scenario();
            Commit commit = s.Change(1, 10);
            s.Client.Receive(s.HostConnection, commit);
            Commit conflict = new Commit(s.Stamp, 1, commit.Origin, commit.RequestId,
                commit.BeforeHash, commit.AfterHash, ParameterWorld.Set(1, 11));
            Assert.Equal(ReplicaDecision.HashMismatch, s.Client.Receive(s.HostConnection, conflict));
            Assert.Equal(ReplicaPhase.NeedsSnapshot, s.Client.Phase);
        }

        [Case] public static void LocalDriftIsDetectedBeforeApplyingNextCommit()
        {
            Scenario s = new Scenario();
            s.Replica.Execute(ParameterWorld.Set(2, 99));
            Assert.Equal(ReplicaDecision.HashMismatch, s.Client.Receive(s.HostConnection, s.Change(1, 10)));
            Assert.Equal((ulong)0, s.Client.Revision);
            Assert.True(!s.Client.CanEdit);
            s.Recover();
            Assert.True(s.Replica.StateHash.Equals(s.Authority.StateHash));
        }

        [Case] public static void LateSnapshotCannotReplaceNewTransfer()
        {
            Scenario s = new Scenario();
            Guid first = s.Client.BeginSnapshot();
            WorldSnapshot stale = s.Host.CaptureSnapshot(first);
            Guid second = s.Client.BeginSnapshot();
            Assert.True(!s.Client.InstallSnapshot(s.HostConnection, stale));
            Assert.True(s.Client.InstallSnapshot(s.HostConnection, s.Host.CaptureSnapshot(second)));
        }

        [Case] public static void SnapshotCannotRollBackAnAcceptedRevision()
        {
            Scenario s = new Scenario();
            WorldSnapshot old = s.Host.CaptureSnapshot(Guid.NewGuid());
            s.Client.Receive(s.HostConnection, s.Change(1, 10));
            Guid ticket = s.Client.BeginSnapshot();
            Assert.True(!s.Client.InstallSnapshot(s.HostConnection, new WorldSnapshot(s.Stamp, 0, ticket, old.Image)));
            Assert.Equal((ulong)1, s.Client.Revision);
        }

        [Case] public static void InvalidSnapshotContentCannotReachWorldAdapter()
        {
            Scenario s = new Scenario();
            Guid ticket = s.Client.BeginSnapshot();
            WorldImage image = s.Authority.Capture();
            byte[] bytes = image.Bytes; bytes[0] ^= 1;
            WorldSnapshot bad = new WorldSnapshot(s.Stamp, 0, ticket, new WorldImage(bytes, image.ContentHash, image.StateHash));
            Assert.True(!s.Client.InstallSnapshot(s.HostConnection, bad));
            Assert.True(s.Replica.StateHash.Equals(image.StateHash));
            Assert.True(!s.Client.CanEdit);
        }

        [Case] public static void DownloadCompletionDoesNotGrantWriteAccess()
        {
            Scenario s = new Scenario();
            Guid ticket = s.Client.BeginSnapshot();
            Assert.True(s.Client.InstallSnapshot(s.HostConnection, s.Host.CaptureSnapshot(ticket)));
            Assert.True(!s.Client.CanEdit);
            Assert.True(!s.Client.MarkReady(s.HostConnection, s.Stamp, 99, s.Authority.StateHash));
            Assert.True(!s.Client.CanEdit);
        }

        [Case] public static void DisconnectInvalidatesPendingCallbacksAndAuthorityMembership()
        {
            Scenario s = new Scenario();
            Guid ticket = s.Client.BeginSnapshot();
            WorldSnapshot pending = s.Host.CaptureSnapshot(ticket);
            s.Client.Disconnect(); s.Host.Disconnect(s.ClientConnection);
            Assert.True(!s.Client.InstallSnapshot(s.HostConnection, pending));
            Assert.Throws<InvalidOperationException>(delegate { s.Client.BeginSnapshot(); });
            Assert.Equal(SubmitDecision.Unauthenticated, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 10))).Decision);
        }

        [Case] public static void RetentionIsBoundedAndEvictedRequestsNeverReexecute()
        {
            Scenario s = new Scenario();
            for (ulong i = 1; i <= 600; i++) s.Change(i, (int)i);
            Assert.Equal(Limits.JournalEntries, s.Host.JournalCount);
            Assert.Equal(SubmitDecision.ExpiredRequest, s.Host.Submit(s.ClientConnection,
                new Intent(s.Stamp, 1, 0, ParameterWorld.Set(1, 1))).Decision);
            Commit[] commits;
            Assert.True(!s.Host.TryReadJournal(0, out commits));
            Assert.True(s.Host.TryReadJournal(599, out commits));
            Assert.Equal(1, commits.Length);
            s.Recover();
            Assert.Equal(600, s.Replica.Get(1));
        }

        [Case] public static void WorldOperationsRejectWrongThread()
        {
            Scenario s = new Scenario();
            Exception caught = null;
            Thread worker = new Thread(delegate()
            {
                try { s.Host.CaptureSnapshot(Guid.NewGuid()); }
                catch (Exception error) { caught = error; }
            });
            worker.Start(); worker.Join();
            Assert.True(caught is InvalidOperationException);
        }

        [Case] public static void DiagnosticsAndTransportInboxAreBounded()
        {
            DiagnosticRing log = new DiagnosticRing(2);
            SessionStamp stamp = new SessionStamp(Guid.NewGuid(), 1);
            for (ulong i = 1; i <= 5; i++) log.Record(DiagnosticCode.Committed, stamp, Guid.Empty, i, i);
            DiagnosticRecord[] records = log.Read();
            Assert.Equal(2, records.Length);
            Assert.Equal((ulong)4, records[0].Revision);
            Assert.Equal((ulong)5, records[1].Revision);
            BoundedInbox<Intent> inbox = new BoundedInbox<Intent>(1);
            Intent value = new Intent(stamp, 1, 0, ParameterWorld.Set(1, 1));
            Assert.True(inbox.TryPost(value));
            Assert.True(!inbox.TryPost(value));
            Intent taken;
            Assert.True(inbox.TryTake(out taken));
            Assert.True(ReferenceEquals(value, taken));
            Assert.True(!inbox.TryTake(out taken));
        }
    }
}
