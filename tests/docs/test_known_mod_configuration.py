from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
WORKFLOW = REPO / ".github" / "workflows" / "ci.yml"


class KnownModConfigurationContractTests(unittest.TestCase):
    def test_load_sensitive_known_mod_settings_enter_manifest_fingerprint(self) -> None:
        source = (RUNTIME / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        for marker in ["ModConfigurationHash", 'typeName == "GameAnarchy.Mod"', 'typeName == "EightyOne2.Mod"', "forge-shared="]:
            self.assertIn(marker, source)

    def test_real_reference_probe_covers_remaining_simulation_surfaces(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8-sig")
        for marker in [
            "Building BuildingManager", "Vehicle VehicleManager", "Citizen CitizenInstance CitizenManager",
            "PathUnit PathManager", "TreeInstance TreeManager", "PropInstance PropManager", "TerrainManager",
            "WaterManager ElectricityManager NaturalResourceManager",
        ]:
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
