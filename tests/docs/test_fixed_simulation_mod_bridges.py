from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class FixedSimulationModBridgeContractTests(unittest.TestCase):
    def test_game_anarchy_shared_settings_are_host_owned(self) -> None:
        source = (RUNTIME / "GameAnarchyBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "bridge.gameanarchy", "GameAnarchy.ModSettings.ModSetting", "SharedProperties",
            "SettingWritePrefix", "HostOnlyMutationPrefix", "ModEconomyManager",
            "ClientLoading", "ClientRecovering", "ClientReplicaLive", "1, 3, 1, 0",
            "ValidateSupportedConfiguration", "CurrentUnlockMode", "OilDepletionRate",
            "GetFireProbability", "UnsupportedManualFirePrefix",
        ]:
            self.assertIn(marker, source)
        for marker in [
            "AchievementSystemEnabled", "SkipIntroEnabled", "OptionsPanelCategoriesHorizontalOffset",
            "ToolButtonPresent", "ToolButtonPositionX", "ToolButtonPositionY",
        ]:
            self.assertIn(marker, source)
        self.assertNotIn("CommandReplay", source)

    def test_infinite_goods_runs_original_buffer_loop_only_on_host(self) -> None:
        source = (RUNTIME / "InfiniteGoodsBridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "bridge.infinitegoods", "InfiniteGoodsMod.Settings.Settings", "InfiniteGoodsMod.Settings.SettingId",
            "InfiniteGoodsMod.Transfer.TransferMonitor", "OnAfterSimulationTick", "HostOnlyTickPrefix",
            "ClientShadow", "ClientReplicaLive", "SupportedProductVersion = \"6.1\"",
            "ExpectedSettingNames", "UnsupportedServicePointSettings", "TransferIfMatch",
        ]:
            self.assertIn(marker, source)
        self.assertNotIn("BuildingId", source)

    def test_eighty_one_shared_simulation_switches_are_host_owned(self) -> None:
        source = (RUNTIME / "EightyOne2Bridge.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "bridge.eightyone2", "EightyOne2.ModSettings", "XMLIgnoreUnlocking", "XMLCrossTheLine",
            "XMLNoPowerlines", "XMLElectricRoads", "XMLNoPipes", "XMLIgnoreOriginalWater", "XMLIgnoreExpanded",
            "GameAreaManagerPatches", "ExpandedElectricityManager", "WaterFacilityAIPatches",
            "ClientLoading", "ClientRecovering", "ClientReplicaLive",
        ]:
            self.assertIn(marker, source)

    def test_registry_registers_and_patches_all_known_simulation_bridges(self) -> None:
        source = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        for marker in [
            "DemandControllerBridgeAdapter", "GameAnarchyBridgeAdapter", "InfiniteGoodsBridgeAdapter",
            "EightyOne2BridgeAdapter", "InstallOptionalPatches", "ResetOptionalPatchState",
        ]:
            self.assertIn(marker, source)

    def test_disabled_known_mod_assemblies_do_not_activate_bridges(self) -> None:
        registry = (RUNTIME / "KnownModBridgeRegistry.cs").read_text(encoding="utf-8-sig")
        catalog = (RUNTIME / "EnabledPluginCatalog.cs").read_text(encoding="utf-8-sig")
        bridge = (RUNTIME / "GameAnarchyBridge.cs").read_text(encoding="utf-8-sig")
        for type_name in [
            "DemandController.DemandController", "GameAnarchy.Mod", "InfiniteGoodsMod.ModIdentity",
            "EightyOne2.Mod", "NetworkMultitool.Mod",
        ]:
            self.assertEqual(registry.count(f'"{type_name}"'), 1)
        self.assertIn("KnownModBridgeDescriptor", registry)
        self.assertIn("EnabledPluginCatalog.Capture()", registry)
        self.assertNotIn("GetPluginsInfo()", registry)
        self.assertIn("plugin.isEnabled", catalog)
        self.assertIn("ContainsUserMod", catalog)
        self.assertIn("DeclaredMethod(setter)", bridge)


if __name__ == "__main__":
    unittest.main()
