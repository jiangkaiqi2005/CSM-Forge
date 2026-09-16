"""Source-level safety contract for the minimum-playable multiplayer Alpha."""
from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class MinimumPlayableAlphaContractTests(unittest.TestCase):
    def test_unsupported_persistent_writes_fail_closed(self) -> None:
        source = (RUNTIME / "AlphaSafetyPatches.cs").read_text(encoding="utf-8-sig")
        required = [
            "TreeManager", "CreateTree", "MoveTree", "ReleaseTree",
            "PropManager", "CreateProp", "MoveProp", "ReleaseProp",
            "TerrainTool", "ApplyBrush", "AlphaUnsupportedWritePolicy.Block",
            "RuntimeScopeGuard.IsApplying",
        ]
        missing = [value for value in required if value not in source]
        self.assertEqual([], missing, "Alpha safety barrier lost required surfaces: " + ", ".join(missing))

    def test_player_ui_declares_alpha_boundary(self) -> None:
        source = (RUNTIME / "ForgeSettingsPanel.cs").read_text(encoding="utf-8-sig")
        for value in ["最小可玩 Alpha", "道路", "建筑", "交通线路", "Tree/Prop/Terrain", "安全阻断"]:
            self.assertIn(value, source)

    def test_package_notice_declares_same_boundary(self) -> None:
        source = (REPO / "scripts" / "build-runtime.ps1").read_text(encoding="utf-8-sig")
        for value in [
            "minimum-playable Alpha", "roads/networks", "buildings", "transport lines",
            "Tree/Prop", "Terrain", "fail-closed", "projection-drift",
        ]:
            self.assertIn(value, source)


if __name__ == "__main__":
    unittest.main()
