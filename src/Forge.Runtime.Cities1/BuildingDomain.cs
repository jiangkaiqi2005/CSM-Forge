using System;
using ColossalFramework.Math;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class ObservedBuildingChange { public Hash256 BeforeRoot; public Hash256 AfterRoot; public BuildingResultV2 Result; }
    internal sealed class ObservedBuildingDeleteTicket { public ushort NativeId; public EntityIdentityV2 Entity; public Hash256 BeforeRoot; public int RefundAmount; }

    internal static class BuildingGameAccess
    {
        public static BuildingStateV2 Capture(EntityIdentityV2 entity, uint nativeId, uint buildIndex, int constructionCost)
        {
            if (!entity.IsValid || nativeId == 0 || nativeId > ushort.MaxValue) throw new ArgumentException("Invalid building capture identity.");
            BuildingManager manager = BuildingManager.instance; if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            Building building = manager.m_buildings.m_buffer[(ushort)nativeId];
            if (building.m_flags == Building.Flags.None) throw new InvalidOperationException("Building is not live.");
            BuildingInfo info = building.Info; if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Building prefab is unavailable.");
            int length = building.Length;
            if (length <= 0 || length > byte.MaxValue) throw new InvalidOperationException("Building length exceeds Forge V3 supported range.");
            Vector3 position = building.m_position;
            return new BuildingStateV2(entity, info.name, position.x, position.y, position.z, building.m_angle, (byte)length, buildIndex, constructionCost);
        }

        public static BuildingInfo ResolvePrefab(string prefabKey)
        {
            BuildingInfo info = PrefabCollection<BuildingInfo>.FindLoaded(prefabKey);
            if (info == null) throw new InvalidOperationException("Building prefab is not loaded: " + prefabKey); return info;
        }

        private static int ChargeHostConstruction(BuildingInfo info)
        {
            ToolManager tools = ToolManager.instance;
            if (tools == null || (tools.m_properties.m_mode & ItemClass.Availability.Game) == 0) return 0;
            int constructionCost = info.GetConstructionCost(); if (constructionCost <= 0) return 0;
            int fetched = EconomyManager.instance.FetchResource(EconomyManager.Resource.Construction, constructionCost, info.m_class);
            return fetched == constructionCost ? constructionCost : -1;
        }

        private static void ChargeReplicaConstruction(BuildingInfo info, int constructionCost)
        {
            if (constructionCost <= 0) return;
            int fetched = EconomyManager.instance.FetchResource(EconomyManager.Resource.Construction, constructionCost, info.m_class);
            if (fetched != constructionCost) throw new InvalidOperationException("Replica could not project Host construction cost.");
        }

        public static int CalculateRefund(ushort nativeId)
        {
            BuildingManager manager = BuildingManager.instance; SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null || nativeId == 0) return 0;
            Building building = manager.m_buildings.m_buffer[nativeId];
            if (building.m_flags == Building.Flags.None || !simulation.IsRecentBuildIndex(building.m_buildIndex)) return 0;
            BuildingInfo info = building.Info;
            if (info == null || info.m_buildingAI == null) return 0;
            return Math.Max(0, info.m_buildingAI.GetRefundAmount(nativeId, ref building));
        }

        private static void ApplyRefund(BuildingInfo info, int refundAmount)
        {
            if (refundAmount <= 0) return;
            EconomyManager.instance.AddResource(EconomyManager.Resource.RefundAmount, refundAmount, info.m_class);
        }

        public static ushort CreateHost(LoadIdentity load, BuildingIntentV2 intent, uint buildIndex, out int constructionCost)
        {
            BuildingManager manager = BuildingManager.instance; SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("CS1 building services are unavailable.");
            BuildingInfo info = ResolvePrefab(intent.PrefabKey); constructionCost = ChargeHostConstruction(info); if (constructionCost < 0) return 0;
            ushort nativeId;
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                if (!manager.CreateBuilding(out nativeId, ref simulation.m_randomizer, info, new Vector3(intent.X, intent.Y, intent.Z), intent.Angle, intent.Length, buildIndex))
                    throw new InvalidOperationException("CS1 rejected authoritative building creation after charging construction cost.");
                if (simulation.m_currentBuildIndex <= buildIndex) simulation.m_currentBuildIndex = buildIndex + 1u;
            }
            return nativeId;
        }

        public static ushort CreateReplica(LoadIdentity load, BuildingStateV2 state)
        {
            BuildingManager manager = BuildingManager.instance; SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("CS1 building services are unavailable.");
            BuildingInfo info = ResolvePrefab(state.PrefabKey); Randomizer localRandomizer = simulation.m_randomizer; ushort nativeId;
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                ChargeReplicaConstruction(info, state.ConstructionCost);
                if (!manager.CreateBuilding(out nativeId, ref localRandomizer, info, new Vector3(state.X, state.Y, state.Z), state.Angle, state.Length, state.BuildIndex))
                    throw new InvalidOperationException("CS1 rejected replica building projection.");
                if (simulation.m_currentBuildIndex <= state.BuildIndex) simulation.m_currentBuildIndex = state.BuildIndex + 1u;
            }
            return nativeId;
        }

        public static int DeletePlayerAuthority(LoadIdentity load, ushort nativeId)
        {
            BuildingManager manager = BuildingManager.instance; if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            Building building = manager.m_buildings.m_buffer[nativeId];
            if (building.m_flags == Building.Flags.None) throw new InvalidOperationException("Building is already absent.");
            BuildingInfo info = building.Info; int refund = CalculateRefund(nativeId);
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                ApplyRefund(info, refund); manager.ReleaseBuilding(nativeId);
            }
            if (manager.m_buildings.m_buffer[nativeId].m_flags != Building.Flags.None) throw new InvalidOperationException("CS1 did not release requested building.");
            return refund;
        }

        public static void DeleteReplica(LoadIdentity load, ushort nativeId, int refundAmount)
        {
            BuildingManager manager = BuildingManager.instance; if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            Building building = manager.m_buildings.m_buffer[nativeId];
            if (building.m_flags == Building.Flags.None) throw new InvalidOperationException("Building is already absent.");
            BuildingInfo info = building.Info;
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                ApplyRefund(info, refundAmount); manager.ReleaseBuilding(nativeId);
            }
            if (manager.m_buildings.m_buffer[nativeId].m_flags != Building.Flags.None) throw new InvalidOperationException("CS1 did not release requested building.");
        }

        public static void UpdateReplica(LoadIdentity load, ushort nativeId, BuildingStateV2 state)
        {
            BuildingManager manager = BuildingManager.instance; if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            Building current = manager.m_buildings.m_buffer[nativeId]; if (current.m_flags == Building.Flags.None) throw new InvalidOperationException("Building is absent.");
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                if (current.Info == null || current.Info.name != state.PrefabKey) manager.UpdateBuildingInfo(nativeId, ResolvePrefab(state.PrefabKey));
                current = manager.m_buildings.m_buffer[nativeId]; Vector3 position = current.m_position;
                if (position.x != state.X || position.y != state.Y || position.z != state.Z || current.m_angle != state.Angle)
                    manager.RelocateBuilding(nativeId, new Vector3(state.X, state.Y, state.Z), state.Angle);
            }
            BuildingStateV2 actual = Capture(state.Entity, nativeId, state.BuildIndex, 0);
            if (!Equivalent(state, actual)) throw new InvalidOperationException("Replica building natural update did not match Host result.");
        }

        public static bool Equivalent(BuildingStateV2 expected, BuildingStateV2 actual)
        {
            return expected != null && actual != null && expected.Entity.Equals(actual.Entity) && expected.PrefabKey == actual.PrefabKey &&
                expected.X == actual.X && expected.Y == actual.Y && expected.Z == actual.Z && expected.Angle == actual.Angle && expected.Length == actual.Length;
        }
    }

    public abstract class BuildingDomainBase
    {
        protected readonly LoadIdentity Load; protected readonly EntityIdMapV2 Ids = new EntityIdMapV2();
        protected readonly BuildingStateIndexV2 Committed = new BuildingStateIndexV2();
        protected BuildingDomainBase(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); Load = load;
            RuntimeServices.EntityMaps.AttachDomain(BuildingAuthorityDomain.Id, Ids); if (Ids.Count == 0) SeedExistingBuildings(); else ValidateRestoredMappings();
        }
        public bool TryResolveEntity(uint nativeId, out EntityIdentityV2 entity) { return Ids.TryGetIdentity(nativeId, out entity); }
        public bool TryResolveNative(EntityIdentityV2 entity, out uint nativeId) { return Ids.TryGetNative(entity, out nativeId); }
        public EntityMapEntryV2[] SnapshotMappings() { return Ids.SnapshotEntries(); }
        protected Hash256 CaptureRoot() { return Committed.Root; }
        private void SeedExistingBuildings()
        {
            BuildingManager manager = BuildingManager.instance; if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            int size = checked((int)manager.m_buildings.m_size);
            for (int i = 1; i < size; i++) if (manager.m_buildings.m_buffer[i].m_flags != Building.Flags.None)
            { EntityIdentityV2 identity = Ids.Allocate((uint)i); Committed.Seed(BuildingGameAccess.Capture(identity, (uint)i, manager.m_buildings.m_buffer[i].m_buildIndex, 0)); }
        }
        private void ValidateRestoredMappings()
        { EntityMapEntryV2[] mappings = Ids.SnapshotEntries(); for (int i = 0; i < mappings.Length; i++) { Building value = BuildingManager.instance.m_buildings.m_buffer[(ushort)mappings[i].NativeId]; Committed.Seed(BuildingGameAccess.Capture(mappings[i].Identity, mappings[i].NativeId, value.m_buildIndex, 0)); } }
    }

    public sealed class BuildingAuthorityDomain : BuildingDomainBase, IAuthorityDomainV2
    {
        private int observationCursor;
        public const ushort Id = 10; public ushort DomainId { get { return Id; } } public Hash256 StateRoot { get { return CaptureRoot(); } }
        public BuildingAuthorityDomain(LoadIdentity load) : base(load) { }
        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return DomainExecutionV2.Rejected();
            BuildingIntentV2 intent; try { intent = BuildingDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            if (intent.Kind == BuildingIntentKindV2.Create)
            {
                SimulationManager simulation = SimulationManager.instance; if (simulation == null) return DomainExecutionV2.Rejected();
                uint buildIndex = simulation.m_currentBuildIndex; int constructionCost;
                ushort nativeId = BuildingGameAccess.CreateHost(Load, intent, buildIndex, out constructionCost); if (nativeId == 0) return DomainExecutionV2.Rejected();
                EntityIdentityV2 identity = Ids.Allocate(nativeId); BuildingStateV2 state = BuildingGameAccess.Capture(identity, nativeId, buildIndex, constructionCost);
                Committed.Apply(BuildingResultV2.Created(state));
                return DomainExecutionV2.Success(BuildingDomainCodecV2.EncodeResult(BuildingResultV2.Created(state)), StateRoot);
            }
            uint native; if (!Ids.TryGetNative(intent.Entity, out native) || native == 0 || native > ushort.MaxValue) return DomainExecutionV2.Rejected();
            int refund = BuildingGameAccess.DeletePlayerAuthority(Load, (ushort)native);
            if (!Ids.Retire(intent.Entity)) throw new InvalidOperationException("Building identity retirement failed.");
            Committed.Apply(BuildingResultV2.Deleted(intent.Entity, refund));
            return DomainExecutionV2.Success(BuildingDomainCodecV2.EncodeResult(BuildingResultV2.Deleted(intent.Entity, refund)), StateRoot);
        }
        internal ObservedBuildingChange ObserveCreated(ushort nativeId, uint buildIndex, int constructionCost)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) throw new InvalidOperationException("Observed building creation is outside HostLive.");
            EntityIdentityV2 existing; if (Ids.TryGetIdentity(nativeId, out existing)) return null; Hash256 before = StateRoot;
            EntityIdentityV2 identity = Ids.Allocate(nativeId); BuildingStateV2 state = BuildingGameAccess.Capture(identity, nativeId, buildIndex, constructionCost);
            Committed.Apply(BuildingResultV2.Created(state));
            return new ObservedBuildingChange { BeforeRoot = before, AfterRoot = StateRoot, Result = BuildingResultV2.Created(state) };
        }
        internal ObservedBuildingDeleteTicket PrepareObservedDelete(ushort nativeId, bool playerBulldoze)
        {
            EntityIdentityV2 identity; if (!Ids.TryGetIdentity(nativeId, out identity)) return null;
            return new ObservedBuildingDeleteTicket { NativeId = nativeId, Entity = identity, BeforeRoot = StateRoot,
                RefundAmount = playerBulldoze ? BuildingGameAccess.CalculateRefund(nativeId) : 0 };
        }
        internal ObservedBuildingChange CompleteObservedDelete(ObservedBuildingDeleteTicket ticket)
        {
            if (ticket == null) return null; if (!Ids.Retire(ticket.Entity)) throw new InvalidOperationException("Observed building identity retirement failed.");
            Committed.Apply(BuildingResultV2.Deleted(ticket.Entity, ticket.RefundAmount));
            return new ObservedBuildingChange { BeforeRoot = ticket.BeforeRoot, AfterRoot = StateRoot,
                Result = BuildingResultV2.Deleted(ticket.Entity, ticket.RefundAmount) };
        }

        internal ObservedBuildingChange PollNaturalChanges(int budget)
        {
            EntityMapEntryV2[] mappings = Ids.SnapshotEntries(); if (mappings.Length == 0 || budget <= 0) return null;
            int count = Math.Min(budget, mappings.Length);
            for (int i = 0; i < count; i++)
            {
                if (observationCursor >= mappings.Length) observationCursor = 0;
                EntityMapEntryV2 mapping = mappings[observationCursor++];
                if (mapping.NativeId == 0 || mapping.NativeId > ushort.MaxValue) throw new InvalidOperationException("Building mapping exceeds CS1 native range.");
                Building building = BuildingManager.instance.m_buildings.m_buffer[(ushort)mapping.NativeId];
                if (building.m_flags == Building.Flags.None) continue;
                BuildingStateV2 actual = BuildingGameAccess.Capture(mapping.Identity, mapping.NativeId, building.m_buildIndex, 0); BuildingStateV2 previous;
                if (!Committed.TryGet(mapping.Identity, out previous) || BuildingGameAccess.Equivalent(previous, actual)) continue;
                Hash256 before = StateRoot; BuildingResultV2 result = BuildingResultV2.Updated(actual); Committed.Apply(result);
                return new ObservedBuildingChange { BeforeRoot = before, AfterRoot = StateRoot, Result = result };
            }
            return null;
        }
    }

    public sealed class BuildingReplicaDomain : BuildingDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return BuildingAuthorityDomain.Id; } } public Hash256 StateRoot { get { return CaptureRoot(); } }
        public BuildingReplicaDomain(LoadIdentity load) : base(load) { }
        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot"); CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica building apply is invalid in current role.");
            BuildingResultV2 result = BuildingDomainCodecV2.DecodeResult(absoluteDelta);
            if (result.Kind == BuildingResultKindV2.Created)
            {
                uint existing; if (Ids.TryGetNative(result.Entity, out existing)) throw new InvalidOperationException("Replica building entity already exists.");
                ushort nativeId = BuildingGameAccess.CreateReplica(Load, result.State); Ids.BindKnown(result.Entity, nativeId);
                BuildingStateV2 actual = BuildingGameAccess.Capture(result.Entity, nativeId, result.State.BuildIndex, result.State.ConstructionCost);
                if (!BuildingGameAccess.Equivalent(result.State, actual)) throw new InvalidOperationException("Replica building projection did not match Host result.");
                Committed.Apply(result);
            }
            else if (result.Kind == BuildingResultKindV2.Deleted)
            {
                uint native; if (!Ids.TryGetNative(result.Entity, out native) || native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("Replica cannot delete unknown building entity.");
                BuildingGameAccess.DeleteReplica(Load, (ushort)native, result.RefundAmount);
                if (!Ids.Retire(result.Entity)) throw new InvalidOperationException("Replica building identity retirement failed.");
                Committed.Apply(result);
            }
            else
            {
                uint native; if (!Ids.TryGetNative(result.Entity, out native) || native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("Replica cannot update unknown building entity.");
                BuildingGameAccess.UpdateReplica(Load, (ushort)native, result.State); Committed.Apply(result);
            }
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Building projection root mismatch.");
        }
    }
}
