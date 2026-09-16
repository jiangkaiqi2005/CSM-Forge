using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private EconomyControlAuthorityDomain hostEconomyControl;
        private EconomyControlReplicaDomain clientEconomyControl;
        private Hash256 committedEconomyControlRoot;

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
            EconomyControlStateV2 actual = EconomyControlGameAccess.Capture();
            Hash256 after = actual.Root;
            if (committedEconomyControlRoot == null)
            {
                committedEconomyControlRoot = after;
                return;
            }
            if (committedEconomyControlRoot.Equals(after)) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                EconomyControlAuthorityDomain.Id, committedEconomyControlRoot, after,
                EconomyControlCodecV2.EncodeState(actual));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-economy-control-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }

        private void ObserveCommittedDomainBatch(AuthorityBatch batch)
        {
            if (batch == null) return;
            if (batch.DomainId == EconomyControlAuthorityDomain.Id)
                committedEconomyControlRoot = batch.AfterRoot;
            else if (batch.DomainId == EconomyCashAuthorityDomain.Id)
                committedCashRoot = batch.AfterRoot;
            else if (batch.DomainId == DemandAuthorityDomain.Id)
                committedDemandRoot = batch.AfterRoot;
        }
    }
}
