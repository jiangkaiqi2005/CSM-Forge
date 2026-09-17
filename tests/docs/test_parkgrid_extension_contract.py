from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class ParkGridExtensionContractTests(unittest.TestCase):
    def test_park_grid_is_sharded_absolute_state_with_stable_identity(self) -> None:
        source = (RUNTIME / "ParkGridStateAdapter.cs").read_text(encoding="utf-8-sig")
        for value in [
            "IForgeInteractiveShardedStateAdapterV1", "ShardCount", "64", "m_parkGrid",
            "EntityIdentityV2", "builtin.districtpark", "CreatePark", "BindKnown", "AreaModified",
            "DistrictTool.Layer.Parks", "EncodeBrush",
        ]:
            self.assertIn(value, source)
        self.assertNotIn("CommandReceiver", source)
        # Native park slots may appear as local variables/comments, but the wire dictionary must
        # be keyed by the stable entity identity rather than serializing the local slot itself.
        self.assertIn("writer.Write(value.Identity.EntityId)", source)
        self.assertIn("writer.Write(value.Identity.Generation)", source)
        self.assertNotIn("writer.Write(value.NativeId)", source)
        self.assertNotIn("writer.Write(native)", source)

    def test_client_park_tool_routes_through_extension_intent(self) -> None:
        source = (RUNTIME / "ParkGridPatches.cs").read_text(encoding="utf-8-sig")
        self.assertIn("TrySubmitIntent(\"builtin.parkgrid\"", source)
        self.assertIn("RuntimeScopeGuard.EnterApply", source)
        self.assertIn("DistrictParkCreateSlotBarrierPatch", source)
        self.assertIn("DistrictParkReleaseSlotBarrierPatch", source)


if __name__ == "__main__":
    unittest.main()
