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
        private StableNameAuthorityDomain hostNames;
        private StableNameReplicaDomain clientNames;

        private IAuthorityDomainV2[] CreateHostDomains(LoadIdentity identity)
        {
            hostWater = new WaterBudgetAuthorityDomain(identity);
            hostDemand = new DemandAuthorityDomain(identity);
            committedDemandRoot = hostDemand.StateRoot;
            hostTaxes = new TaxAuthorityDomain(identity);
            hostBudgets = new BudgetAuthorityDomain(identity);
            hostCash = new EconomyCashAuthorityDomain(identity);
            committedCashRoot = hostCash.StateRoot;
            hostEconomyControl = new EconomyControlAuthorityDomain(identity);
            committedEconomyControlRoot = hostEconomyControl.StateRoot;
            hostAreas = new AreaAuthorityDomain(identity);
            hostBuildings = new BuildingAuthorityDomain(identity);
            hostNet = new NetAuthorityDomain(identity);
            hostZones = new ZoneAuthorityDomain(identity, hostNet);
            hostDistricts = new DistrictCompositeAuthorityDomain(identity);
            hostClock = new SimulationClockAuthorityDomain(identity);
            hostTransport = new TransportLineAuthorityDomain(identity);
            hostNames = new StableNameAuthorityDomain(identity);
            hostCityName = new CityNameAuthorityDomain(identity);
            hostWeather = new WeatherAuthorityDomain(identity);
            committedWeatherRoot = hostWeather.StateRoot;
            hostExtensions = new ExtensionStateAuthorityDomain(identity,
                new CitiesExtensionStateRegistry(ForgeExtensionApi.SnapshotRegistrations(), true));
            return new IAuthorityDomainV2[] { hostWater, hostDemand, hostTaxes, hostBudgets, hostCash, hostEconomyControl, hostAreas, hostBuildings, hostNet, hostZones, hostDistricts, hostClock, hostTransport, hostNames, hostCityName, hostWeather, hostExtensions };
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
            clientEconomyControl = new EconomyControlReplicaDomain(identity);
            clientAreas = new AreaReplicaDomain(identity);
            clientBuildings = new BuildingReplicaDomain(identity);
            clientNet = new NetReplicaDomain(identity);
            clientZones = new ZoneReplicaDomain(identity, clientNet);
            clientDistricts = new DistrictCompositeReplicaDomain(identity);
            clientClock = new SimulationClockReplicaDomain(identity);
            clientTransport = new TransportLineReplicaDomain(identity);
            clientNames = new StableNameReplicaDomain(identity);
            clientCityName = new CityNameReplicaDomain(identity);
            clientWeather = new WeatherReplicaDomain(identity);
            clientExtensions = new ExtensionStateReplicaDomain(identity,
                new CitiesExtensionStateRegistry(ForgeExtensionApi.SnapshotRegistrations(), false));
            return new IReplicaDomainV2[] { clientWater, clientDemand, clientTaxes, clientBudgets, clientCash, clientEconomyControl, clientAreas, clientBuildings, clientNet, clientZones, clientDistricts, clientClock, clientTransport, clientNames, clientCityName, clientWeather, clientExtensions };
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

        internal bool TryResolveClientNetNodeNative(EntityIdentityV2 entity, out uint nativeId)
        {
            nativeId = 0;
            return IsClientNetReplicaActive && clientNet.TryResolveNodeNative(entity, out nativeId);
        }

        internal bool TryResolveClientNetSegmentNative(EntityIdentityV2 entity, out uint nativeId)
        {
            nativeId = 0;
            return IsClientNetReplicaActive && clientNet.TryResolveSegmentNative(entity, out nativeId);
        }

        internal bool TryResolveHostNetNodeNative(EntityIdentityV2 entity, out uint nativeId)
        {
            nativeId = 0;
            return IsHostNetAuthorityActive && hostNet.TryResolveNodeNative(entity, out nativeId);
        }

        internal bool TryResolveHostNetSegmentNative(EntityIdentityV2 entity, out uint nativeId)
        {
            nativeId = 0;
            return IsHostNetAuthorityActive && hostNet.TryResolveSegmentNative(entity, out nativeId);
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
                // No graph change at all. Spending construction budget without changing the graph
                // is a genuine inconsistency (the tool charged money and produced nothing).
                if (constructionCost != 0 || refund != 0)
                    FenceSession("net-economy-side-effect-without-graph-change");
                return;
            }
            Hash256 afterRoot = hostNet.StateRoot;
            if (beforeRoot.Equals(afterRoot))
            {
                // A mutation whose encoded graph state is identical (e.g. identity churn that
                // re-encodes to the same content): PublishObserved returns null for this BY DESIGN
                // ("nothing to publish"). Treating that null as a failure used to fence the whole
                // room on a routine road edit. It is a no-op, and the authority's committed root
                // still equals this baseline, so no drift is introduced.
                events.Record(RuntimeEventCode.Error, load.Generation,
                    "net-root-neutral-mutation-ignored; construction=" + constructionCost + "; refund=" + refund);
                return;
            }
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, NetAuthorityDomain.Id,
                beforeRoot, afterRoot, NetDomainCodecV2.EncodeMutation(mutation));
            if (batch == null)
            {
                if (authority.IsFenced) return; // already fenced; the first fence recorded the cause
                // Roots differ but the coordinator refused the batch: a real bridge failure.
                // Record both roots so the next occurrence is diagnosable without a re-run.
                FenceSession("observed-net-change-could-not-commit:" +
                    "before=" + ShortHash(beforeRoot) + ":after=" + ShortHash(afterRoot));
                return;
            }
            BroadcastBatch(batch);
        }

        /// <summary>
        /// WP-1.4c: cadence-driven full observe. The NetTool/bulldoze patches publish graph
        /// changes as they happen; once per window this re-captures the live graph and publishes
        /// any drift the patches missed, so the cached committed root cannot silently diverge.
        /// </summary>
        internal void PollObservedHostNetFull()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostNet == null || authority == null || snapshotSave != null) return;
            if (netVerifyCadence == null || !netVerifyCadence.ShouldVerify(MonotonicMilliseconds())) return;
            PublishObservedHostNet(hostNet.CommittedRoot, 0, 0);
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
            // WP-1.1: the 32k-block sparse capture no longer runs every tick. Net/zone commits
            // force the next poll immediately (BroadcastBatch); this cadence poll is the safety net.
            if (!ZoneVerificationDue()) return;
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
