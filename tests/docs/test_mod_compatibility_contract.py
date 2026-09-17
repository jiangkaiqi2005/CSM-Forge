from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CORE = REPO / "src" / "Forge.Core"


class ModCompatibilityFrameworkContractTests(unittest.TestCase):
    def test_third_party_mods_can_declare_audited_compatibility_kind(self) -> None:
        source = (RUNTIME / "ForgeCompatibilityApi.cs").read_text(encoding="utf-8-sig")
        for value in ["ExactMatch", "ClientOnly", "ForgeSynchronized", "Blocked", "active multiplayer session"]:
            self.assertIn(value, source)

    def test_synchronized_declaration_requires_real_adapter(self) -> None:
        collector = (RUNTIME / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        self.assertIn("ForgeModCompatibilityKind.ForgeSynchronized", collector)
        self.assertIn("HasRegistrationFromAssembly", collector)
        self.assertIn('return "sync-mod"', collector)
        self.assertIn("has no state adapter", collector)

    def test_sync_mod_is_not_a_client_extra_relaxation(self) -> None:
        core = (CORE / "Compatibility.cs").read_text(encoding="utf-8-sig")
        allow_start = core.index("private static bool AllowsClientExtra")
        allow_block = core[allow_start:]
        self.assertIn('"dlc:"', allow_block)
        self.assertIn('"asset:"', allow_block)
        self.assertIn('"client-mod:"', allow_block)
        self.assertNotIn('"sync-mod:"', allow_block)

    def test_adapter_schema_and_binary_are_in_manifest(self) -> None:
        collector = (RUNTIME / "CompatibilityCollector.cs").read_text(encoding="utf-8-sig")
        for value in ["adapter:", "SchemaVersion", "BinaryHash(adapter.Assembly)"]:
            self.assertIn(value, collector)

    def test_public_adapter_documentation_locks_authority_model(self) -> None:
        docs = (REPO / "docs" / "MOD-ADAPTER-API-V1.zh-CN.md").read_text(encoding="utf-8-sig")
        for value in [
            "IForgeShardedStateAdapterV1", "IForgeInteractiveShardedStateAdapterV1",
            "ForgeCompatibilityApi.Declare", "sync-mod:*", "EntityIdentityV2", "Command Replay",
        ]:
            self.assertIn(value, docs)


if __name__ == "__main__":
    unittest.main()
