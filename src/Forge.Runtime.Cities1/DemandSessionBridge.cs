using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private DemandAuthorityDomain hostDemand;
        private DemandReplicaDomain clientDemand;
        private Hash256 committedDemandRoot;
        private DemandStateV2 committedDemandState;

        internal void PollObservedHostDemand()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDemand == null || authority == null || snapshotSave != null)
                return;
            // WP-P2: three scalars - compare values instead of hashing every tick. The root is
            // computed only when the values actually changed.
            DemandStateV2 actual = DemandGameAccess.Capture();
            if (committedDemandState == null)
            {
                committedDemandState = actual;
                committedDemandRoot = actual.Root;
                return;
            }
            if (committedDemandState.Equivalent(actual)) return;
            Hash256 after = actual.Root;
            committedDemandState = actual;

            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                DemandAuthorityDomain.Id, committedDemandRoot, after, DemandCodecV2.Encode(actual));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-demand-change-could-not-commit");
                return;
            }
            committedDemandRoot = after;
            BroadcastBatch(batch);
        }
    }
}
