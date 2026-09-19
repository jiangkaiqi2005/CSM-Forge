from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class UltimateDlcCoverageContractTests(unittest.TestCase):
    def test_all_official_gameplay_dlc_are_classified(self) -> None:
        source = (REPO / "docs" / "DLC-COVERAGE-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        names = [
            "After Dark", "Snowfall", "Natural Disasters", "Mass Transit", "Green Cities",
            "Parklife", "Industries", "Campus", "Sunset Harbor", "Airports",
            "Plazas & Promenades", "Financial Districts", "Hotels & Retreats",
            "Match Day", "Concerts", "Pearls from the East", "Content Creator Packs", "Radio Stations",
        ]
        for name in names:
            self.assertIn(name, source)
        self.assertNotIn("UNCLASSIFIED", source.split("## 2. 官方玩法 DLC", 1)[1].split("## 3.", 1)[0])
        registry = (RUNTIME / "OfficialDlcCoverage.cs").read_text(encoding="utf-8-sig")
        for name in names:
            self.assertIn(name, registry)

    def test_event_lifecycle_is_stable_identity_based(self) -> None:
        source = (RUNTIME / "EventStateAdapter.cs").read_text(encoding="utf-8-sig")
        for value in [
            "SchemaVersion { get { return 2; } }", "EntityIdentityV2", "BuildingAuthorityDomain.Id",
            "PrefabCollection<EventInfo>.FindLoaded", "CreateEvent", "ReleaseEvent", "BindKnownIdentity",
            "m_createdFrame", "m_customSeed", "m_startFrame", "m_totalReward",
        ]:
            self.assertIn(value, source)
        patches = (RUNTIME / "EventControlPatches.cs").read_text(encoding="utf-8-sig")
        self.assertIn("EventCreateSlotBarrierPatch", patches)
        self.assertIn("EventReleaseSlotBarrierPatch", patches)
        self.assertNotIn("CommandReceiver", source + patches)

    def test_natural_disasters_have_dedicated_stable_authority(self) -> None:
        source = (RUNTIME / "DisasterStateAdapter.cs").read_text(encoding="utf-8-sig")
        for value in [
            "builtin.disasters", "EntityIdentityV2", "PrefabCollection<DisasterInfo>.FindLoaded",
            "CreateDisaster", "ReleaseDisaster", "m_randomSeed", "m_targetPosition", "m_casualtiesCount",
        ]:
            self.assertIn(value, source)
        patches = (RUNTIME / "DisasterPatches.cs").read_text(encoding="utf-8-sig")
        for value in ["DisasterCreateSlotBarrierPatch", "DisasterReleaseSlotBarrierPatch", "StartRandomDisaster"]:
            self.assertIn(value, patches)

    def test_campus_host_only_deep_state_is_explicit(self) -> None:
        source = (RUNTIME / "CampusDeepStateAdapter.cs").read_text(encoding="utf-8-sig")
        for value in ["m_coachHireTimes", "m_coachCount", "m_grantType", "m_dynamicVarsityAttractivenessModifier"]:
            self.assertIn(value, source)
        patches = (RUNTIME / "CampusAuthorityPatches.cs").read_text(encoding="utf-8-sig")
        for value in ["OnCoachesCountChanged", "OnBuyResearchGrant", "OnAcademicYearEnded", "Host-only"]:
            self.assertIn(value, patches)

    def test_deep_districtpark_projection_excludes_identity_like_fields(self) -> None:
        source = (RUNTIME / "DistrictParkDeepScalarAdapter.cs").read_text(encoding="utf-8-sig")
        for value in [
            "IForgeShardedStateAdapterV1", "ForbiddenTokens", "building", "vehicle", "citizen",
            "node", "segment", "path", "line", "prefab", "target", "SchemaFingerprint",
        ]:
            self.assertIn(value, source)
        self.assertNotIn("BinaryFormatter", source)
        self.assertNotIn("Marshal.StructureToPtr", source)

    def test_runtime_diagnostics_publish_dlc_coverage(self) -> None:
        source = (RUNTIME / "RuntimeDiagnostics.cs").read_text(encoding="utf-8-sig")
        self.assertIn("OfficialDlcCoverage.Summary", source)
        self.assertIn("districtpark-deep-fields", source)


if __name__ == "__main__":
    unittest.main()
