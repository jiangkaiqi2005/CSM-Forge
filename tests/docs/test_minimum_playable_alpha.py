"""Source-level safety contract for the current multiplayer candidate."""
from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class MinimumPlayableAlphaContractTests(unittest.TestCase):
    def test_only_remaining_alpha_persistent_write_barrier_is_terrain(self) -> None:
        source = (RUNTIME / "AlphaSafetyPatches.cs").read_text(encoding="utf-8-sig")
        for value in ["TerrainTool", "ApplyBrush", "AlphaUnsupportedWritePolicy.Block", "RuntimeScopeGuard.IsApplying"]:
            self.assertIn(value, source)
        for removed_surface in ["TreeManager", "CreateTree", "MoveTree", "ReleaseTree", "PropManager", "CreateProp", "MoveProp", "ReleaseProp"]:
            self.assertNotIn(removed_surface, source)

    def test_player_ui_declares_tree_prop_authority_and_terrain_boundary(self) -> None:
        source = (RUNTIME / "ForgeSettingsPanel.cs").read_text(encoding="utf-8-sig")
        for value in ["道路", "建筑", "交通线路", "Stable-ID Tree/Prop", "真实多机验证", "Terrain", "安全阻断"]:
            self.assertIn(value, source)

    def test_package_notice_declares_same_boundary(self) -> None:
        source = (REPO / "scripts" / "build-runtime.ps1").read_text(encoding="utf-8-sig")
        for value in [
            "roads/networks", "buildings", "transport lines", "Tree/Prop",
            "Stable IDs", "Terrain", "fail-closed", "projection-drift",
        ]:
            self.assertIn(value, source)


if __name__ == "__main__":
    unittest.main()
