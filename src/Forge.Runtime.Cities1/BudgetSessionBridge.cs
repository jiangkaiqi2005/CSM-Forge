using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private BudgetAuthorityDomain hostBudgets;
        private BudgetReplicaDomain clientBudgets;

        internal bool TryQueueBudget(ItemClass.Service service, ItemClass.SubService subService, int budget, bool night)
        {
            if (budget < 0 || budget > 255 || snapshotSave != null) return false;
            BudgetStateV2 requested = new BudgetStateV2(new BudgetKeyV2((int)service, (int)subService, night), budget);
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(identity,
                delegate { SubmitBudgetIntent(new BudgetIntentV2(requested)); });
        }

        private void SubmitBudgetIntent(BudgetIntentV2 value)
        {
            if (snapshotSave != null || value == null) return;
            BudgetStateV2 supported;
            if (!BudgetGameAccess.TryRead(value.Requested.Key, out supported))
            {
                if (mode == MultiplayerSessionMode.Hosting) FenceSession("unsupported-host-budget-target");
                else if (mode == MultiplayerSessionMode.ClientLive) FenceSession("unsupported-client-budget-target");
                return;
            }
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostBudgets == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("budget-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    BudgetAuthorityDomain.Id, hostBudgets.StateRoot, BudgetDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-budget-intent-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientBudgets == null || clientOperation == ulong.MaxValue)
                { FenceSession("budget-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    BudgetAuthorityDomain.Id, clientBudgets.StateRoot, BudgetDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "budget-intent-" + clientOperation + ":pending";
            }
        }
    }
}
