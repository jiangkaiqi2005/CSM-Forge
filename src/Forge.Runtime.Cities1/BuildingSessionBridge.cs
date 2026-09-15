using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal bool IsHostBuildingAuthorityActive
        {
            get { return mode == MultiplayerSessionMode.Hosting && hostBuildings != null && authority != null; }
        }

        internal bool IsClientBuildingReplicaActive
        {
            get { return mode == MultiplayerSessionMode.ClientLive && clientBuildings != null && replica != null; }
        }

        internal bool TryQueueClientBuildingDelete(ushort nativeId)
        {
            if (!IsClientBuildingReplicaActive || nativeId == 0) return false;
            EntityIdentityV2 identity;
            if (!clientBuildings.TryResolveEntity(nativeId, out identity)) return false;
            return TryQueueBuilding(BuildingIntentV2.Delete(identity));
        }

        internal void ObserveHostBuildingCreated(ushort nativeId, uint buildIndex, int constructionCost)
        {
            if (!IsHostBuildingAuthorityActive || snapshotSave != null)
                throw new InvalidOperationException("Observed building creation is not valid in the current Host state.");
            ObservedBuildingChange change = hostBuildings.ObserveCreated(nativeId, buildIndex, constructionCost);
            if (change == null) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                BuildingAuthorityDomain.Id, change.BeforeRoot, change.AfterRoot,
                BuildingDomainCodecV2.EncodeResult(change.Result));
            if (batch == null || authority.IsFenced)
                throw new InvalidOperationException("Observed building creation could not be committed.");
            BroadcastBatch(batch);
        }

        internal ObservedBuildingDeleteTicket PrepareHostBuildingDelete(ushort nativeId)
        {
            if (!IsHostBuildingAuthorityActive || snapshotSave != null) return null;
            return hostBuildings.PrepareObservedDelete(nativeId);
        }

        internal void ObserveHostBuildingDeleted(ObservedBuildingDeleteTicket ticket)
        {
            if (ticket == null) return;
            if (!IsHostBuildingAuthorityActive || snapshotSave != null)
                throw new InvalidOperationException("Observed building deletion is not valid in the current Host state.");
            ObservedBuildingChange change = hostBuildings.CompleteObservedDelete(ticket);
            if (change == null) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                BuildingAuthorityDomain.Id, change.BeforeRoot, change.AfterRoot,
                BuildingDomainCodecV2.EncodeResult(change.Result));
            if (batch == null || authority.IsFenced)
                throw new InvalidOperationException("Observed building deletion could not be committed.");
            BroadcastBatch(batch);
        }
    }
}
