from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CORE = REPO / "src" / "Forge.Core"


class NetworkMultitoolBridgeContractTests(unittest.TestCase):
    def test_supported_high_level_modes_are_semantic_stable_id_intents(self) -> None:
        core = (CORE / "NetDomainModelV2.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "MultitoolAddNode", "MultitoolRemoveNode", "MultitoolUnionNodes",
            "MultitoolSplitNode", "MultitoolIntersectSegments", "MultitoolCreateParallel",
            "MultitoolCreateConnection", "NetMultitoolPointV2", "EntityIdentityV2",
            "SecondaryTarget", "RelatedTargets", "SemanticPoints",
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
            "NetworkMultitool.CreateParallelMode", "NetworkMultitool.BaseCreateMode",
            "SemanticVoidOperationPrefix", "HostConstructionCost", "TrySubmitNetIntent", "TryResolveClientNetNode", "TryResolveClientNetSegment",
            "TryResolveHostNetNode", "TryResolveHostNetSegment",
        ]:
            self.assertIn(marker, bridge)
        self.assertNotIn("CommandReplay", bridge)
        self.assertNotIn("CommandReceiver", bridge)

    def test_exact_139_surface_is_validated_before_any_patch_is_installed(self) -> None:
        bridge = (RUNTIME / "NetworkMultitoolBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "2abaf77e665f8b1c2cae65de188f9109f28f0c0b",
            "new Version(1, 3, 9, 0)",
            "ResolveSurface()",
            'StringComparer.Ordinal.Equals(name.Name, "NetworkMultitool")',
            '"NetworkMultitool.BaseNetworkMultitoolMode+Point"',
            '"NetworkMultitool.Settings.NeedMoney.value"',
            "ParametersMatch",
            "result.PointArrayType, typeof(bool), typeof(NetInfo), typeof(int)",
        ]:
            self.assertIn(marker, bridge)
        self.assertLess(bridge.index("Surface resolved = ResolveSurface();"), bridge.index("harmony.Patch"))

    def test_host_executes_mod_surface_inside_existing_net_authority_domain(self) -> None:
        net = (RUNTIME / "NetDomain.cs").read_text(encoding="utf-8-sig")
        self.assertIn("NetworkMultitoolBridge.IsSemanticIntent", net)
        self.assertIn("NetworkMultitoolBridge.TryExecuteHost", net)
        self.assertIn("RuntimeScopeGuard.EnterApply(Load, Id)", net)
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        self.assertIn("NetworkMultitoolBridge.InstallOptionalPatches(harmony)", registry)
        self.assertIn("NetworkMultitoolBridge.ResetPatchState()", registry)

    def test_absolute_projection_orders_topology_dependencies(self) -> None:
        net = (RUNTIME / "NetDomain.cs").read_text(encoding="utf-8-sig")
        start = net.index("public void ApplyAbsolute(byte[] absoluteDelta")
        apply = net[start:]
        self.assertLess(apply.index("mutation.DeleteSegments"), apply.index("mutation.DeleteNodes"))
        self.assertLess(apply.index("mutation.DeleteNodes"), apply.index("mutation.UpsertNodes"))
        self.assertLess(apply.index("mutation.UpsertNodes"), apply.index("mutation.UpsertSegments"))

    def test_source_audit_keeps_gameplay_evidence_unverified(self) -> None:
        audit = (REPO / "docs" / "NETWORK-MULTITOOL-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        for marker in [
            "2abaf77e665f8b1c2cae65de188f9109f28f0c0b",
            "984abb421550369a285498769575fc01c005096b",
            "Client 只提交 Stable-ID semantic intent",
            "尚未完成真实 Network Multitool assembly 的游戏内加载",
            "不能标为 gameplay validated",
        ]:
            self.assertIn(marker, audit)


if __name__ == "__main__":
    unittest.main()
