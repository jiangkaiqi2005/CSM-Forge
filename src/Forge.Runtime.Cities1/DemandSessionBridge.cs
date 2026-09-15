using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private DemandAuthorityDomain hostDemand;
        private DemandReplicaDomain clientDemand;
        private Hash256 committedDemandRoot;

        internal void PollObservedHostDemand()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDemand == null || authority == null || snapshotSave != null)
                return;
            Hash256 after = hostDemand.StateRoot;
            if (committedDemandRoot == null)
            {
                committedDemandRoot = after;
                return;
            }
            if (committedDemandRoot.Equals(after)) return;

            DemandStateV2 state = DemandGameAccess.Capture();
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                DemandAuthorityDomain.Id, committedDemandRoot, state.Root, DemandCodecV2.Encode(state));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-demand-change-could-not-commit");
                return;
            }
            committedDemandRoot = state.Root;
            BroadcastBatch(batch);
        }
    }
}
