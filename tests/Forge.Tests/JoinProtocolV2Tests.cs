using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    public static class JoinProtocolV2Tests
    {
        private static Hash256 H(byte value) { return Hash256.Compute(new byte[] { value }); }

        [Case]
        public static void NoTransferSnapshotOfferRoundTripsWithoutFakeTransferIds()
        {
            SnapshotOfferV2 source = new SnapshotOfferV2(Guid.NewGuid(), 2, 17, H(1),
                false, Guid.Empty, Guid.Empty, 0, null);
            SnapshotOfferV2 decoded = JoinMessagesV2.DecodeSnapshotOffer(JoinMessagesV2.EncodeSnapshotOffer(source));
            Assert.Equal(source.JoinId, decoded.JoinId);
            Assert.Equal((uint)2, decoded.JoinGeneration);
            Assert.True(!decoded.RequiresTransfer);
            Assert.Equal(Guid.Empty, decoded.SnapshotId);
        }

        [Case]
        public static void BarrierAndActivationMarkersRoundTripAtFixedRevisions()
        {
            Guid join = Guid.NewGuid();
            Guid barrierId = Guid.NewGuid();
            ReplayBarrierV2 barrier = new ReplayBarrierV2(join, 3, barrierId, 25, H(2));
            ReplayBarrierV2 barrierDecoded = JoinMessagesV2.DecodeReplayBarrier(JoinMessagesV2.EncodeReplayBarrier(barrier));
            Assert.Equal(barrierId, barrierDecoded.BarrierId);
            Assert.Equal((ulong)25, barrierDecoded.Revision);

            Guid grantId = Guid.NewGuid();
            ActivationGrantV2 grant = new ActivationGrantV2(join, 3, grantId, 40, H(3), 7);
            ActivationGrantV2 grantDecoded = JoinMessagesV2.DecodeActivationGrant(JoinMessagesV2.EncodeActivationGrant(grant));
            Assert.Equal(grantId, grantDecoded.GrantId);
            Assert.Equal((ulong)40, grantDecoded.Revision);
            Assert.Equal((ulong)7, grantDecoded.PermissionVersion);
        }

        [Case]
        public static void ControlReceiptAndLaneSequenceAreStrict()
        {
            IntentReceiptV2 receipt = new IntentReceiptV2(9, AuthoritySubmitDecisionV2.Committed, 12);
            IntentReceiptV2 decoded = ControlMessagesV2.DecodeReceipt(ControlMessagesV2.EncodeReceipt(receipt));
            Assert.Equal((ulong)9, decoded.OperationCounter);
            Assert.Equal(AuthoritySubmitDecisionV2.Committed, decoded.Decision);
            Assert.Equal((ulong)12, decoded.Revision);

            LaneSequenceTracker tracker = new LaneSequenceTracker();
            Assert.True(tracker.Accept(SessionLane.Control, 1));
            Assert.True(!tracker.Accept(SessionLane.Control, 1));
            Assert.True(!tracker.Accept(SessionLane.Control, 3));
            Assert.True(tracker.Accept(SessionLane.Control, 2));
            Assert.Equal((ulong)1, tracker.Next(SessionLane.State));
            Assert.Equal((ulong)2, tracker.Next(SessionLane.State));
        }
    }
}
