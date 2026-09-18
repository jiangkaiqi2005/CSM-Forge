from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


class TmpePersistentRulesContractTests(unittest.TestCase):
    def test_exact_surface_and_stable_net_codec_are_present(self) -> None:
        bridge = (REPO / "src" / "Forge.Runtime.Cities1" / "TmpePersistentRulesBridge.cs").read_text(encoding="utf-8-sig")
        registry = (REPO / "src" / "Forge.Runtime.Cities1" / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        for marker in [
            'new Version(11, 9, 4, 25100)',
            'TrafficManager.Lifecycle.TrafficManagerMod',
            'bridge.tmpe-persistent-rules',
            'StableSegment(context, segment)',
            'LaneIndex',
            'Limits.FramePayloadBytes',
            'AsCustomPrioritySegmentsDM',
            'AsLaneArrowsDM',
            'AsLaneSpeedLimitsDM',
            'ResetSegmentVehicleRestrictions',
            'ResetLaneArrows',
            'RemoveNodeFromSimulation',
        ]:
            self.assertIn(marker, bridge)
        self.assertNotIn('ForgeExtensionApi.Register(new TmpePersistentRulesAdapter())', registry)
        self.assertNotIn('TmpeDynamicAuthorityBridge.InstallOptionalPatches(harmony)', registry)
        self.assertIn('TM:PE remains a blocked-mod', registry)

    def test_tmpe_remains_blocked_until_dynamic_authority_closure(self) -> None:
        collector = (REPO / "src" / "Forge.Runtime.Cities1" / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        audit = (REPO / "docs" / "TMPE-PERSISTENT-RULES-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        self.assertIn('typeName == "TrafficManager.Lifecycle.TrafficManagerMod"', collector)
        self.assertIn('return "blocked-mod"', collector)
        for marker in ["7d1360d39047a7fcee59743aabdc705b8fe29914", "2cd504e24b4d89a7361e037610bd5a6bffc8c2184bf62094f9d3f32ce86a52df", "原生 node、segment 或 lane ID", "P6-B/P7", "未验证", "CI green"]:
            self.assertIn(marker, audit)


if __name__ == "__main__":
    unittest.main()
