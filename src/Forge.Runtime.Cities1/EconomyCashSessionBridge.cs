using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private EconomyCashAuthorityDomain hostCash;
        private EconomyCashReplicaDomain clientCash;
        private Hash256 committedCashRoot;

        internal void PollObservedHostCash()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostCash == null || authority == null || snapshotSave != null)
                return;
            EconomyCashStateV2 actual = EconomyCashGameAccess.Capture();
            Hash256 after = actual.Root;
            if (committedCashRoot == null)
            {
                committedCashRoot = after;
                return;
            }
            if (committedCashRoot.Equals(after)) return;

            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                EconomyCashAuthorityDomain.Id, committedCashRoot, after, EconomyCashCodecV2.Encode(actual));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-cash-change-could-not-commit");
                return;
            }
            committedCashRoot = after;
            BroadcastBatch(batch);
        }
    }
}
