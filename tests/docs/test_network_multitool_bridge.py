from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CORE = REPO / "src" / "Forge.Core"


class NetworkMultitoolBridgeContractTests(unittest.TestCase):
    def test_first_five_high_level_modes_are_semantic_stable_id_intents(self) -> None:
        core = (CORE / "NetDomainModelV2.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "MultitoolAddNode", "MultitoolRemoveNode", "MultitoolUnionNodes",
            "MultitoolSplitNode", "MultitoolIntersectSegments", "EntityIdentityV2",
            "SecondaryTarget", "RelatedTargets",
        ]:
            self.assertIn(marker, core)

    def test_optional_patch_targets_high_level_multitool_methods(self) -> None:
        bridge = (RUNTIME / "NetworkMultitoolBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "NetworkMultitool.AddNodeMode", '"InsertNode"',
            "NetworkMultitool.RemoveNodeMode", '"RemoveNode"',
            "NetworkMultitool.UnionNodeMode", '"Union"',
            "NetworkMultitool.SplitNodeMode", '"Split"',
            "NetworkMultitool.IntersectSegmentMode", '"IntersectSegments"',
            "TrySubmitNetIntent", "TryResolveClientNetNode", "TryResolveClientNetSegment",
            "TryResolveHostNetNode", "TryResolveHostNetSegment",
        ]:
            self.assertIn(marker, bridge)
        self.assertNotIn("CommandReplay", bridge)
        self.assertNotIn("CommandReceiver", bridge)

    def test_host_executes_mod_surface_inside_existing_net_authority_domain(self) -> None:
        net = (RUNTIME / "NetDomain.cs").read_text(encoding="utf-8-sig")
        self.assertIn("NetworkMultitoolBridge.IsSemanticIntent", net)
        self.assertIn("NetworkMultitoolBridge.TryExecuteHost", net)
        self.assertIn("RuntimeScopeGuard.EnterApply(Load, Id)", net)
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        self.assertIn("NetworkMultitoolBridge.InstallOptionalPatches(harmony)", registry)
        self.assertIn("NetworkMultitoolBridge.ResetPatchState()", registry)


if __name__ == "__main__":
    unittest.main()
