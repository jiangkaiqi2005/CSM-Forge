using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private NetAuthorityDomain hostNet;
        private NetReplicaDomain clientNet;
        private ZoneAuthorityDomain hostZones;
        private ZoneReplicaDomain clientZones;
        private TransportLineAuthorityDomain hostTransport;
        private TransportLineReplicaDomain clientTransport;

        private IAuthorityDomainV2[] CreateHostDomains(LoadIdentity identity)
        {
            hostWater = new WaterBudgetAuthorityDomain(identity);
            hostDemand = new DemandAuthorityDomain(identity);
            committedDemandRoot = hostDemand.StateRoot;
            hostTaxes = new TaxAuthorityDomain(identity);
            hostBudgets = new BudgetAuthorityDomain(identity);
            hostCash = new EconomyCashAuthorityDomain(identity);
            committedCashRoot = hostCash.StateRoot;
            hostBuildings = new BuildingAuthorityDomain(identity);
            hostNet = new NetAuthorityDomain(identity);
            hostZones = new ZoneAuthorityDomain(identity, hostNet);
            hostDistricts = new DistrictCompositeAuthorityDomain(identity);
            hostClock = new SimulationClockAuthorityDomain(identity);
            hostTransport = new TransportLineAuthorityDomain(identity);
            return new IAuthorityDomainV2[] { hostWater, hostDemand, hostTaxes, hostBudgets, hostCash, hostBuildings, hostNet, hostZones, hostDistricts, hostClock, hostTransport };
        }

        private IReplicaDomainV2[] CreateClientDomains(LoadIdentity identity)
        {
            ClearDistrictClientPending();
            ClearTransportClientPending();
            SimulationClockSave.Store.ApplyPending(identity);
            clientWater = new WaterBudgetReplicaDomain(identity);
            clientDemand = new DemandReplicaDomain(identity);
            clientTaxes = new TaxReplicaDomain(identity);
            clientBudgets = new BudgetReplicaDomain(identity);
            clientCash = new EconomyCashReplicaDomain(identity);
            clientBuildings = new BuildingReplicaDomain(identity);
            clientNet = new NetReplicaDomain(identity);
            clientZones = new ZoneReplicaDomain(identity, clientNet);
            clientDistricts = new DistrictCompositeReplicaDomain(identity);
            clientClock = new SimulationClockReplicaDomain(identity);
            clientTransport = new TransportLineReplicaDomain(identity);
            return new IReplicaDomainV2[] { clientWater, clientDemand, clientTaxes, clientBudgets, clientCash, clientBuildings, clientNet, clientZones, clientDistricts, clientClock, clientTransport };
        }

        internal bool IsHostNetAuthorityActive
        {
            get { return mode == MultiplayerSessionMode.Hosting && hostNet != null && authority != null; }
        }

        internal bool IsClientNetReplicaActive
        {
            get { return mode == MultiplayerSessionMode.ClientLive && clientNet != null && replica != null; }
        }

        internal Hash256 CaptureHostNetRoot()
        {
            return IsHostNetAuthorityActive ? hostNet.StateRoot : null;
        }

        internal bool TryResolveClientNetNode(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsClientNetReplicaActive && clientNet.TryResolveNode(nativeId, out entity);
        }

        internal bool TryResolveClientNetSegment(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsClientNetReplicaActive && clientNet.TryResolveSegment(nativeId, out entity);
        }

        internal bool TryResolveHostNetNode(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsHostNetAuthorityActive && hostNet.TryResolveNode(nativeId, out entity);
        }

        internal bool TryResolveHostNetSegment(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsHostNetAuthorityActive && hostNet.TryResolveSegment(nativeId, out entity);
        }

        internal bool TrySubmitNetIntent(NetIntentV2 value)
        {
            if (value == null || snapshotSave != null) return false;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostNet == null || hostLocalOperation == ulong.MaxValue) return false;
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    NetAuthorityDomain.Id, hostNet.StateRoot, NetDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null) return false;
                BroadcastBatch(result.Batch);
                return true;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientNet == null || clientOperation == ulong.MaxValue) return false;
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    NetAuthorityDomain.Id, clientNet.StateRoot, NetDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "net-intent-" + clientOperation + ":pending";
                return true;
            }
            return false;
        }

        internal void PublishObservedHostNet(Hash256 beforeRoot, int constructionCost, int refund)
        {
            if (!IsHostNetAuthorityActive || beforeRoot == null || snapshotSave != null) return;
            NetMutationV2 mutation = hostNet.ObserveHostChanges(constructionCost, refund);
            if (mutation == null)
            {
                if (constructionCost != 0 || refund != 0)
                    FenceSession("net-economy-side-effect-without-graph-change");
                return;
            }
            Hash256 afterRoot = hostNet.StateRoot;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, NetAuthorityDomain.Id,
                beforeRoot, afterRoot, NetDomainCodecV2.EncodeMutation(mutation));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-net-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }

        internal bool TryInterceptClientZoneRefresh(ushort blockId, ulong requestedZone1, ulong requestedZone2,
            bool playerTool, out ulong restoreZone1, out ulong restoreZone2)
        {
            restoreZone1 = restoreZone2 = 0;
            if (mode != MultiplayerSessionMode.ClientLive || clientZones == null || replica == null) return false;
            ZoneBlockKeyV2 key;
            if (!clientZones.TryGetCommittedForBlock(blockId, out key, out restoreZone1, out restoreZone2))
                return false;
            if (!playerTool || (requestedZone1 == restoreZone1 && requestedZone2 == restoreZone2)) return true;
            if (snapshotSave != null || clientOperation == ulong.MaxValue) return false;
            clientOperation++;
            ZoneIntentV2 request = new ZoneIntentV2(new ZoneStateV2(key, requestedZone1, requestedZone2));
            PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                ZoneAuthorityDomain.Id, clientZones.StateRoot, ZoneDomainCodecV2.EncodeIntent(request));
            SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
            lock (gate) detail = "zone-intent-" + clientOperation + ":pending";
            return true;
        }

        internal void ObserveHostZoneBlock(ushort blockId)
        {
            if (mode != MultiplayerSessionMode.Hosting || hostZones == null || authority == null || snapshotSave != null) return;
            Hash256 before = hostZones.StateRoot;
            ZoneMutationV2 mutation = hostZones.ObserveBlock(blockId);
            PublishObservedZoneMutation(before, mutation);
        }

        internal void PollObservedHostZones()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostZones == null || authority == null || snapshotSave != null) return;
            Hash256 before = hostZones.StateRoot;
            ZoneMutationV2 mutation = hostZones.ReconcileWorld();
            PublishObservedZoneMutation(before, mutation);
        }

        private void PublishObservedZoneMutation(Hash256 before, ZoneMutationV2 mutation)
        {
            if (mutation == null || mutation.Count == 0) return;
            Hash256 after = hostZones.StateRoot;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, ZoneAuthorityDomain.Id,
                before, after, ZoneDomainCodecV2.EncodeMutation(mutation));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-zone-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }
    }
}
