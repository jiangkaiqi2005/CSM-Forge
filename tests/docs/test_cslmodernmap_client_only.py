from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


class CslModernMapClientOnlyContractTests(unittest.TestCase):
    def test_whitelist_is_exact_type_assembly_version_and_binary(self) -> None:
        source = (REPO / "src" / "Forge.Runtime.Cities1" / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        for marker in [
            'typeName != "CSLModernMap.CSLModernMap"',
            'name.Name, "CSLModernMap"',
            "new Version(6, 6, 2, 0)",
            "9fc331505b43484dc55d38762d5198e7b68aa04dca19af5ff8f59d37e76c300a",
            'return "client-mod"',
        ]:
            self.assertIn(marker, source)
        self.assertNotIn('"CSLModernMap.*"', source)

    def test_audit_records_binary_il_boundary_and_unverified_runtime(self) -> None:
        audit = (REPO / "docs" / "CSLMODERNMAP-AUDIT-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        for marker in [
            "3781187198", "CSLModernMap.CSLModernMap", "Version=6.6.2.0",
            "9fc331505b43484dc55d38762d5198e7b68aa04dca19af5ff8f59d37e76c300a",
            "stfld/stsfld", "PathManager.WaitForAllPaths()", "被 Steam 下架",
            "尚未验证", "CI green 不能写成 gameplay validated",
        ]:
            self.assertIn(marker, audit)


if __name__ == "__main__":
    unittest.main()
