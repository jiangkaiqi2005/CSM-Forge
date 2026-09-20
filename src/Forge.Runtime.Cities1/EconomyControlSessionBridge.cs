using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private EconomyControlAuthorityDomain hostEconomyControl;
        private EconomyControlReplicaDomain clientEconomyControl;
        private Hash256 committedEconomyControlRoot;
        private EconomyControlStateV2 committedEconomyControlState;

        internal bool TryQueueEconomyControl(EconomyControlIntentV2 value)
        {
            if (value == null || snapshotSave != null) return false;
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate { SubmitEconomyControl(value); });
        }

        private void SubmitEconomyControl(EconomyControlIntentV2 value)
        {
            if (value == null || snapshotSave != null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostEconomyControl == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("economy-control-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    EconomyControlAuthorityDomain.Id, hostEconomyControl.StateRoot,
                    EconomyControlCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-economy-control-rejected:" + result.Decision); return; }
                committedEconomyControlRoot = result.Batch.AfterRoot;
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientEconomyControl == null || clientOperation == ulong.MaxValue)
                { FenceSession("economy-control-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    EconomyControlAuthorityDomain.Id, clientEconomyControl.StateRoot,
                    EconomyControlCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "economy-control-" + clientOperation + ":pending";
            }
        }

        internal void PollObservedHostEconomyControl()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostEconomyControl == null || authority == null || snapshotSave != null)
                return;
            // WP-P2: compare the captured snapshot bytes; hash only when it actually changed.
            EconomyControlStateV2 actual = EconomyControlGameAccess.Capture();
            Hash256 before = hostEconomyControl.CommittedRoot;
            committedEconomyControlRoot = before;
            if (committedEconomyControlState != null && committedEconomyControlState.Equivalent(actual)) return;
            committedEconomyControlState = actual;
            Hash256 after = actual.Root;
            if (before.Equals(after)) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                EconomyControlAuthorityDomain.Id, before, after,
                EconomyControlCodecV2.EncodeState(actual));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-economy-control-change-could-not-commit");
                return;
            }
            hostEconomyControl.MarkObservedCommitted(after);
            committedEconomyControlRoot = after;
            BroadcastBatch(batch);
        }
    }
}
