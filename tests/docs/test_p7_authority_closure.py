from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def test_p7_building_natural_lifecycle_is_host_owned_and_stable_id_keyed():
    model = (ROOT / "src/Forge.Core/BuildingDomainModelV2.cs").read_text(encoding="utf-8")
    domain = (ROOT / "src/Forge.Runtime.Cities1/BuildingDomain.cs").read_text(encoding="utf-8")
    patches = (ROOT / "src/Forge.Runtime.Cities1/BuildingPatches.cs").read_text(encoding="utf-8")
    adapter = (ROOT / "src/Forge.Runtime.Cities1/BuildingSimulationStateAdapter.cs").read_text(encoding="utf-8")
    session = (ROOT / "src/Forge.Runtime.Cities1/BuildingSessionBridge.cs").read_text(encoding="utf-8")

    assert "Updated = 3" in model
    assert "PollNaturalChanges" in domain
    assert "PollObservedHostBuildings" in session
    assert 'Adapter = "builtin.building-simulation"' in adapter
    assert "EntityIdentityV2 identity" in adapter
    assert '"m_accessSegment"' in adapter
    assert '"m_infoIndex"' in adapter
    assert '[HarmonyPatch(typeof(BuildingManager), "SimulationStepImpl")]' in patches
    assert "ClientReplicaLive" in patches


def test_p7_contract_does_not_claim_gameplay_validation_or_tmpe_support():
    doc = (ROOT / "docs/P7-CITIZEN-VEHICLE-PATH-AUTHORITY-V1.zh-CN.md").read_text(encoding="utf-8")
    assert "真实双机/多机未验证" in doc
    assert "TM:PE 继续是 `blocked-mod`" in doc
    assert "不得发送 native VehicleId/CitizenInstanceId" in doc
    assert "Stable Segment identity + lane index/offset" in doc


def test_p7_dynamic_managers_use_stable_sharded_results_and_client_barriers():
    runtime = ROOT / "src/Forge.Runtime.Cities1"
    path = (runtime / "PathUnitStateAdapter.cs").read_text(encoding="utf-8")
    vehicle = (runtime / "VehiclePresentationStateAdapter.cs").read_text(encoding="utf-8")
    citizen = (runtime / "CitizenInstancePresentationStateAdapter.cs").read_text(encoding="utf-8")
    patches = (runtime / "DynamicSimulationPatches.cs").read_text(encoding="utf-8")
    tmpe = (runtime / "TmpeDynamicAuthorityBridge.cs").read_text(encoding="utf-8")
    for marker in ["EntityIdentityV2", "m_lane", "m_offset", "StableNameTargetKindV2.NetSegment", "PathManagerSimulationBarrierPatch"]:
        assert marker in path + patches
    assert 'Adapter = "builtin.vehicle-presentation"' in vehicle
    assert 'Adapter = "builtin.citizeninstance-presentation"' in citizen
    for marker in ["VehicleManagerSimulationBarrierPatch", "CitizenManagerSimulationBarrierPatch", "PathManagerCreateBarrierPatch"]:
        assert marker in patches
    for marker in ["new Version(11, 9, 4, 25100)", "CustomPathManager", "CustomCreatePath", "ThreadingExtension"]:
        assert marker in tmpe


def test_p7_observation_work_is_bounded_to_one_extension_entry_per_tick():
    source = (ROOT / "src/Forge.Runtime.Cities1/ExtensionStateDomain.cs").read_text(encoding="utf-8")
    method = source.split("internal void PollObservedHostExtensions()", 1)[1].split("\n        }", 1)[0]
    assert method.count("PollNextHostEntry") == 1
    assert "for (int i = 0; i < 8; i++)" not in method


def test_p7_dynamic_capture_uses_a_shard_index_instead_of_rescanning_every_mapping():
    runtime = ROOT / "src/Forge.Runtime.Cities1"
    helper = (runtime / "StableMappingShardIndex.cs").read_text(encoding="utf-8")
    assert "sealed class StableMappingShardIndex" in helper
    assert "SnapshotMappings()" in helper
    for name in ["PathUnitStateAdapter.cs", "VehiclePresentationStateAdapter.cs", "CitizenInstancePresentationStateAdapter.cs"]:
        source = (runtime / name).read_text(encoding="utf-8")
        capture = source.split("public byte[] CaptureShard", 1)[1].split("public void ApplyShard", 1)[0]
        assert "mappingShards.Get(shardIndex)" in capture
        assert "context.SnapshotMappings()" not in capture


def test_p7_stale_dynamic_building_references_are_omitted_before_stable_lookup():
    runtime = ROOT / "src/Forge.Runtime.Cities1"
    citizen = (runtime / "CitizenInstancePresentationStateAdapter.cs").read_text(encoding="utf-8")
    vehicle = (runtime / "VehiclePresentationStateAdapter.cs").read_text(encoding="utf-8")
    methods = [
        citizen.split("private static EntityIdentityV2 StableBuilding", 1)[1].split("private static ushort ResolveBuilding", 1)[0],
        vehicle.split("private static EntityIdentityV2 Stable", 1)[1].split("private static ushort Resolve", 1)[0],
    ]
    for method in methods:
        assert "BuildingManager.instance" in method
        assert "m_buildings.m_buffer" in method
        assert "Building.Flags.None" in method
        assert method.index("Building.Flags.None") < method.index("TryResolveStableNameIdentity")


def test_multiplayer_status_does_not_present_authority_revision_as_city_version():
    source = (ROOT / "src/Forge.Runtime.Cities1/ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
    assert "城市版本" not in source
