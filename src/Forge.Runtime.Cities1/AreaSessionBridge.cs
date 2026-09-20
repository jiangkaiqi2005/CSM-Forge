using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private AreaAuthorityDomain hostAreas;
        private AreaReplicaDomain clientAreas;
        private AreaStateV2 committedAreaState;

        internal bool TryQueueAreaUnlock(int x, int z)
        {
            AreaUnlockIntentV2 value;
            try { value = new AreaUnlockIntentV2(x, z); } catch { return false; }
            if (snapshotSave != null) return false;
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate { SubmitAreaUnlock(value); });
        }

        private void SubmitAreaUnlock(AreaUnlockIntentV2 value)
        {
            if (value == null || snapshotSave != null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostAreas == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("area-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    AreaAuthorityDomain.Id, hostAreas.StateRoot, AreaDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-area-unlock-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientAreas == null || clientOperation == ulong.MaxValue)
                { FenceSession("area-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    AreaAuthorityDomain.Id, clientAreas.StateRoot, AreaDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "area-unlock-" + clientOperation + ":pending";
            }
        }

        internal void PollObservedHostAreas()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostAreas == null || authority == null || snapshotSave != null) return;
            // WP-P2: compare the small mask instead of hashing every tick.
            AreaStateV2 actual = AreaGameAccess.Capture();
            Hash256 before = hostAreas.CommittedRoot;
            if (committedAreaState != null && committedAreaState.Equivalent(actual)) return;
            committedAreaState = actual;
            if (before.Equals(actual.Root)) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, AreaAuthorityDomain.Id,
                before, actual.Root, AreaDomainCodecV2.EncodeState(actual));
            if (batch == null || authority.IsFenced)
            { FenceSession("observed-area-change-could-not-commit"); return; }
            hostAreas.MarkObservedCommitted(actual.Root);
            BroadcastBatch(batch);
        }
    }
}
