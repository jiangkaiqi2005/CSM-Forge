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
