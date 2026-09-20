using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private EconomyCashAuthorityDomain hostCash;
        private EconomyCashReplicaDomain clientCash;
        private Hash256 committedCashRoot;
        private EconomyCashStateV2 committedCashState;

        internal void PollObservedHostCash()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostCash == null || authority == null || snapshotSave != null)
                return;
            // WP-P2: one scalar - compare the value, hash only on change.
            EconomyCashStateV2 actual = EconomyCashGameAccess.Capture();
            if (committedCashState == null)
            {
                committedCashState = actual;
                committedCashRoot = actual.Root;
                return;
            }
            if (committedCashState.Equivalent(actual)) return;
            Hash256 after = actual.Root;
            committedCashState = actual;

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
