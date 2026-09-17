from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class DecorationAuthorityContractTests(unittest.TestCase):
    def test_tree_and_prop_are_stable_id_sharded_authority(self) -> None:
        source = (RUNTIME / "TreePropStateAdapters.cs").read_text(encoding="utf-8-sig")
        for marker in [
            '"builtin.trees"', '"builtin.props"', "IForgeInteractiveShardedStateAdapterV1",
            "EntityIdentityV2", "GetOrAllocateIdentity", "BindKnownIdentity", "RetireIdentity",
            "PrefabCollection<TreeInfo>.FindLoaded", "PrefabCollection<PropInfo>.FindLoaded",
            "CreateTree", "MoveTree", "ReleaseTree", "CreateProp", "MoveProp", "ReleaseProp",
            "Limits.FramePayloadBytes",
        ]:
            self.assertIn(marker, source)
        self.assertNotIn("CommandReplay", source)
        self.assertNotIn("CommandReceiver", source)

    def test_client_decoration_tools_route_semantic_intents(self) -> None:
        source = (RUNTIME / "DecorationAuthorityPatches.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "ClientReplicaLive", "ForgeExtensionApi.TrySubmitIntent", "TreeStateAdapter.CreateIntent",
            "TreeStateAdapter.MoveIntent", "TreeStateAdapter.DeleteIntent", "PropStateAdapter.CreateIntent",
            "PropStateAdapter.MoveIntent", "PropStateAdapter.DeleteIntent", "RuntimeScopeGuard.EnterApply",
        ]:
            self.assertIn(marker, source)

    def test_builtin_registry_always_registers_tree_and_prop(self) -> None:
        source = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        self.assertIn("new TreeStateAdapter()", source)
        self.assertIn("new PropStateAdapter()", source)


if __name__ == "__main__":
    unittest.main()
