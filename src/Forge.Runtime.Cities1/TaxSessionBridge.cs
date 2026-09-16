using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private TaxAuthorityDomain hostTaxes;
        private TaxReplicaDomain clientTaxes;

        internal bool TryQueueTax(TaxIntentV2 value)
        {
            if (value == null || snapshotSave != null) return false;
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate { SubmitTaxIntent(value); });
        }

        private void SubmitTaxIntent(TaxIntentV2 value)
        {
            if (snapshotSave != null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostTaxes == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("tax-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    TaxAuthorityDomain.Id, hostTaxes.StateRoot, TaxDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-tax-intent-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientTaxes == null || clientOperation == ulong.MaxValue)
                { FenceSession("tax-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    TaxAuthorityDomain.Id, clientTaxes.StateRoot, TaxDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "tax-intent-" + clientOperation + ":pending";
            }
        }
    }
}
