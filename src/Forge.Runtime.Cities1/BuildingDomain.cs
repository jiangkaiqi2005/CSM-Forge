using System;
using ColossalFramework.Math;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class BuildingGameAccess
    {
        public static BuildingStateV2 Capture(EntityIdentityV2 entity, uint nativeId, uint buildIndex)
        {
            if (!entity.IsValid || nativeId == 0 || nativeId > ushort.MaxValue)
                throw new ArgumentException("Invalid building capture identity.");
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            Building building = manager.m_buildings.m_buffer[(ushort)nativeId];
            if (building.m_flags == Building.Flags.None) throw new InvalidOperationException("Building is not live.");
            BuildingInfo info = building.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Building prefab is unavailable.");
            Vector3 position = building.m_position;
            return new BuildingStateV2(entity, info.name, position.x, position.y, position.z,
                building.m_angle, building.Length, buildIndex);
        }

        public static BuildingInfo ResolvePrefab(string prefabKey)
        {
            BuildingInfo info = PrefabCollection<BuildingInfo>.FindLoaded(prefabKey);
            if (info == null) throw new InvalidOperationException("Building prefab is not loaded: " + prefabKey);
            return info;
        }

        public static ushort CreateHost(LoadIdentity load, BuildingIntentV2 intent, uint buildIndex)
        {
            BuildingManager manager = BuildingManager.instance;
            SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("CS1 building services are unavailable.");
            ushort nativeId;
            BuildingInfo info = ResolvePrefab(intent.PrefabKey);
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                if (!manager.CreateBuilding(out nativeId, ref simulation.m_randomizer, info,
                    new Vector3(intent.X, intent.Y, intent.Z), intent.Angle, intent.Length, buildIndex))
                    throw new InvalidOperationException("CS1 rejected authoritative building creation.");
                if (simulation.m_currentBuildIndex <= buildIndex) simulation.m_currentBuildIndex = buildIndex + 1u;
            }
            return nativeId;
        }

        public static ushort CreateReplica(LoadIdentity load, BuildingStateV2 state)
        {
            BuildingManager manager = BuildingManager.instance;
            SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("CS1 building services are unavailable.");
            BuildingInfo info = ResolvePrefab(state.PrefabKey);
            Randomizer localRandomizer = simulation.m_randomizer;
            ushort nativeId;
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
            {
                if (!manager.CreateBuilding(out nativeId, ref localRandomizer, info,
                    new Vector3(state.X, state.Y, state.Z), state.Angle, state.Length, state.BuildIndex))
                    throw new InvalidOperationException("CS1 rejected replica building projection.");
                if (simulation.m_currentBuildIndex <= state.BuildIndex) simulation.m_currentBuildIndex = state.BuildIndex + 1u;
            }
            return nativeId;
        }

        public static void Delete(LoadIdentity load, ushort nativeId)
        {
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            if (nativeId == 0 || manager.m_buildings.m_buffer[nativeId].m_flags == Building.Flags.None)
                throw new InvalidOperationException("Building is already absent.");
            using (RuntimeScopeGuard.EnterApply(load, BuildingAuthorityDomain.Id))
                manager.ReleaseBuilding(nativeId);
            if (manager.m_buildings.m_buffer[nativeId].m_flags != Building.Flags.None)
                throw new InvalidOperationException("CS1 did not release the requested building.");
        }

        public static bool Equivalent(BuildingStateV2 expected, BuildingStateV2 actual)
        {
            return expected != null && actual != null && expected.Entity.Equals(actual.Entity) &&
                expected.PrefabKey == actual.PrefabKey && expected.X == actual.X && expected.Y == actual.Y &&
                expected.Z == actual.Z && expected.Angle == actual.Angle && expected.Length == actual.Length;
        }
    }

    public abstract class BuildingDomainBase
    {
        protected readonly LoadIdentity Load;
        protected readonly EntityIdMapV2 Ids = new EntityIdMapV2();

        protected BuildingDomainBase(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            Load = load;
            RuntimeServices.EntityMaps.AttachDomain(BuildingAuthorityDomain.Id, Ids);
            if (Ids.Count == 0) SeedExistingBuildings();
            else ValidateRestoredMappings();
        }

        protected Hash256 CaptureRoot()
        {
            BuildingStateIndexV2 index = new BuildingStateIndexV2();
            EntityMapEntryV2[] mappings = Ids.SnapshotEntries();
            for (int i = 0; i < mappings.Length; i++)
                index.Seed(BuildingGameAccess.Capture(mappings[i].Identity, mappings[i].NativeId, 0));
            return index.Root;
        }

        private void SeedExistingBuildings()
        {
            BuildingManager manager = BuildingManager.instance;
            if (manager == null) throw new InvalidOperationException("BuildingManager is unavailable.");
            int size = manager.m_buildings.m_size;
            for (int i = 1; i < size; i++)
            {
                if (manager.m_buildings.m_buffer[i].m_flags == Building.Flags.None) continue;
                EntityIdentityV2 identity = Ids.Allocate((uint)i);
                BuildingGameAccess.Capture(identity, (uint)i, 0);
            }
        }

        private void ValidateRestoredMappings()
        {
            EntityMapEntryV2[] mappings = Ids.SnapshotEntries();
            for (int i = 0; i < mappings.Length; i++)
                BuildingGameAccess.Capture(mappings[i].Identity, mappings[i].NativeId, 0);
        }
    }

    public sealed class BuildingAuthorityDomain : BuildingDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 10;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CaptureRoot(); } }

        public BuildingAuthorityDomain(LoadIdentity load) : base(load) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            BuildingIntentV2 intent;
            try { intent = BuildingDomainCodecV2.DecodeIntent(payload); }
            catch { return DomainExecutionV2.Rejected(); }

            if (intent.Kind == BuildingIntentKindV2.Create)
            {
                SimulationManager simulation = SimulationManager.instance;
                if (simulation == null) return DomainExecutionV2.Rejected();
                uint buildIndex = simulation.m_currentBuildIndex;
                ushort nativeId = BuildingGameAccess.CreateHost(Load, intent, buildIndex);
                EntityIdentityV2 identity = Ids.Allocate(nativeId);
                BuildingStateV2 state = BuildingGameAccess.Capture(identity, nativeId, buildIndex);
                BuildingResultV2 result = BuildingResultV2.Created(state);
                return DomainExecutionV2.Success(BuildingDomainCodecV2.EncodeResult(result), StateRoot);
            }

            uint native;
            if (!Ids.TryGetNative(intent.Entity, out native) || native == 0 || native > ushort.MaxValue)
                return DomainExecutionV2.Rejected();
            BuildingGameAccess.Delete(Load, (ushort)native);
            if (!Ids.Retire(intent.Entity)) throw new InvalidOperationException("Building identity retirement failed.");
            BuildingResultV2 deleted = BuildingResultV2.Deleted(intent.Entity);
            return DomainExecutionV2.Success(BuildingDomainCodecV2.EncodeResult(deleted), StateRoot);
        }
    }

    public sealed class BuildingReplicaDomain : BuildingDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return BuildingAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CaptureRoot(); } }

        public BuildingReplicaDomain(LoadIdentity load) : base(load) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica building apply is invalid in the current role.");

            BuildingResultV2 result = BuildingDomainCodecV2.DecodeResult(absoluteDelta);
            if (result.Kind == BuildingResultKindV2.Created)
            {
                uint existing;
                if (Ids.TryGetNative(result.Entity, out existing))
                    throw new InvalidOperationException("Replica building entity already exists.");
                ushort nativeId = BuildingGameAccess.CreateReplica(Load, result.State);
                Ids.BindKnown(result.Entity, nativeId);
                BuildingStateV2 actual = BuildingGameAccess.Capture(result.Entity, nativeId, result.State.BuildIndex);
                if (!BuildingGameAccess.Equivalent(result.State, actual))
                    throw new InvalidOperationException("Replica building projection did not match the Host result.");
            }
            else
            {
                uint native;
                if (!Ids.TryGetNative(result.Entity, out native) || native == 0 || native > ushort.MaxValue)
                    throw new InvalidOperationException("Replica cannot delete an unknown building entity.");
                BuildingGameAccess.Delete(Load, (ushort)native);
                if (!Ids.Retire(result.Entity)) throw new InvalidOperationException("Replica building identity retirement failed.");
            }

            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Building projection root mismatch.");
        }
    }
}
