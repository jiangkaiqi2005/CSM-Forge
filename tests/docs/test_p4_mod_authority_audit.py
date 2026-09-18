from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class P4ModAuthorityAuditTests(unittest.TestCase):
    def test_game_anarchy_drift_and_unprojected_writes_fail_closed(self) -> None:
        source = (RUNTIME / "GameAnarchyBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            'AssemblyName = "GameAnarchy"', "new Version(1, 3, 1, 0)",
            "GameAnarchy.Managers.CityServicesManager", "GameAnarchy.Extension.OilAndOreResourceExtension",
            "GameAnarchy.Extension.MilestonesExtension", "GameAnarchy.Managers.FireControlManager",
            "ValidateSupportedConfiguration", "UnsupportedBooleanSettings", "both rates must be 100",
            "FireProbabilityPrefix", "UnsupportedManualFirePrefix",
        ]:
            self.assertIn(marker, source)

    def test_infinite_goods_drift_and_service_point_native_queues_fail_closed(self) -> None:
        source = (RUNTIME / "InfiniteGoodsBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            'AssemblyName = "InfiniteGoodsMod"', 'SupportedProductVersion = "6.1"',
            "name.Version.Major != 6", "name.Version.Minor != 0", "ExpectedSettingNames",
            "UnsupportedServicePointSettings", "PedestrianServicePointGoods",
            "CargoServicePointGenericIndustryLumber", "TransferIfMatch",
        ]:
            self.assertIn(marker, source)

    def test_known_incompatible_versions_are_classified_blocked(self) -> None:
        source = (RUNTIME / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        self.assertIn('typeName == "GameAnarchy.Mod" && !GameAnarchyBridge.IsAvailable', source)
        self.assertIn('typeName == "InfiniteGoodsMod.ModIdentity" && !InfiniteGoodsBridge.IsAvailable', source)

    def test_audit_records_real_source_commits_and_unverified_gameplay(self) -> None:
        audit = (REPO / "docs" / "GAME-ANARCHY-INFINITE-GOODS-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        for marker in [
            "b4bed4cf9dd0e5be9d3a09b1ca3a659633368ad3",
            "b5074f9e4e342eb37cb8b17964410357dfff667f",
            "native ID 未进入 wire", "未验证", "CI green 不能写成 gameplay validated",
        ]:
            self.assertIn(marker, audit)


if __name__ == "__main__":
    unittest.main()
