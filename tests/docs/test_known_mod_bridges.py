from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class KnownModBridgeContractTests(unittest.TestCase):
    def test_demand_controller_is_host_owned_absolute_state(self) -> None:
        source = (RUNTIME / "DemandControllerBridge.cs").read_text(encoding="utf-8-sig")
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "bridge.demandcontroller", "IForgeStateAdapterV1", "DemandController.DemandControllerExtension",
            '"Enabled"', '"ResidentialDemand"', '"ResidentialEnabled"', '"CommercialDemand"',
            '"CommercialEnabled"', '"WorkplaceDemand"', '"WorkplaceEnabled"',
        ]:
            self.assertIn(marker, source)
        for marker in ["DemandControllerRefreshPrefix", "ClientReplicaLive", "ClientLoading", "ClientRecovering"]:
            self.assertIn(marker, registry)
        self.assertNotIn("CommandReplay", source)
        self.assertNotIn("CommandReceiver", source)

    def test_bridge_registration_and_dynamic_harmony_patch_are_wired(self) -> None:
        mod = (RUNTIME / "ForgeMod.cs").read_text(encoding="utf-8-sig")
        patches = (RUNTIME / "PatchCoordinator.cs").read_text(encoding="utf-8-sig")
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        self.assertIn("KnownModBridgeRegistry.RegisterAvailable()", mod)
        self.assertIn("RefreshOptionalBridges", mod)
        self.assertIn("KnownModBridgeRegistry.InstallOptionalPatches", patches)
        self.assertIn("KnownModBridgeRegistry.ResetOptionalPatchState", patches)
        self.assertIn("DemandControllerBridge.ResolveRefresh", registry)

    def test_audited_visual_helpers_are_client_only(self) -> None:
        source = (RUNTIME / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        self.assertIn('"ACME.Mod"', source)
        self.assertIn('"PrecisionEngineering.Mod"', source)
        self.assertIn('return "client-mod"', source)


if __name__ == "__main__":
    unittest.main()
