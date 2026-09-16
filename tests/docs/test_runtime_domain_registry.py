"""Ensure Host and Client build the same authority-domain registry in the same order."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
BRIDGE = RUNTIME / "NetSessionBridge.cs"
HOST_INIT = re.compile(r"\bhost(\w+)\s*=\s*new\s+(\w*AuthorityDomain)\b")
CLIENT_INIT = re.compile(r"\bclient(\w+)\s*=\s*new\s+(\w*ReplicaDomain)\b")


class RuntimeDomainRegistryTests(unittest.TestCase):
    def test_host_and_client_registry_are_symmetric_and_ordered(self) -> None:
        source = BRIDGE.read_text(encoding="utf-8-sig")
        host = {name: cls for name, cls in HOST_INIT.findall(source)}
        client = {name: cls for name, cls in CLIENT_INIT.findall(source)}
        self.assertEqual(sorted(host), sorted(client), "Host/Client domain field sets differ")

        host_match = re.search(r"return\s+new\s+IAuthorityDomainV2\[\]\s*\{([^}]*)\}", source, re.S)
        client_match = re.search(r"return\s+new\s+IReplicaDomainV2\[\]\s*\{([^}]*)\}", source, re.S)
        self.assertIsNotNone(host_match, "Host domain registry array is missing")
        self.assertIsNotNone(client_match, "Client domain registry array is missing")

        host_order = [token.strip()[4:] for token in host_match.group(1).split(",") if token.strip().startswith("host")]
        client_order = [token.strip()[6:] for token in client_match.group(1).split(",") if token.strip().startswith("client")]
        self.assertEqual(host_order, client_order, "Host/Client domain registry order differs")
        self.assertEqual(sorted(host), sorted(host_order), "Host registry does not contain every initialized authority domain exactly once")
        self.assertEqual(sorted(client), sorted(client_order), "Client registry does not contain every initialized replica domain exactly once")


if __name__ == "__main__":
    unittest.main()
