from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class InfiniteGoodsBufferContractTests(unittest.TestCase):
    def test_material_buffers_use_building_stable_identity(self) -> None:
        source = (RUNTIME / "InfiniteGoodsBuildingBufferAdapter.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "bridge.infinitegoods-buildingbuffers", "IForgeShardedStateAdapterV1", "EntityIdentityV2",
            "StableNameTargetKindV2.Building", "TryResolveStableNameIdentity", "m_customBuffer1", "m_customBuffer2",
            "ShardCount", "128",
        ]:
            self.assertIn(marker, source)
        self.assertNotIn("writer.Write(native", source)
        self.assertNotIn("CommandReplay", source)

    def test_registry_activates_buffer_projection_with_infinite_goods(self) -> None:
        source = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        self.assertIn("InfiniteGoodsBridge.IsAvailable", source)
        self.assertIn("InfiniteGoodsBuildingBufferAdapter", source)


if __name__ == "__main__":
    unittest.main()
