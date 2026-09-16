"""Keep rebaseline/stop cleanup complete as runtime domains are added."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
DOMAIN_FIELD = re.compile(r"\bprivate\s+\w+(?:AuthorityDomain|ReplicaDomain)\s+(\w+)\s*;")
COMMITTED_ROOT = re.compile(r"\bprivate\s+Hash256\s+(committed\w+Root)\s*;")


class RuntimeDomainLifecycleTests(unittest.TestCase):
    def test_all_domain_references_are_cleared(self) -> None:
        client_fields: set[str] = set()
        host_fields: set[str] = set()
        committed_roots: set[str] = set()
        for path in sorted(RUNTIME.glob("*.cs")):
            text = path.read_text(encoding="utf-8-sig")
            for name in DOMAIN_FIELD.findall(text):
                if name.startswith("client"):
                    client_fields.add(name)
                elif name.startswith("host"):
                    host_fields.add(name)
            committed_roots.update(COMMITTED_ROOT.findall(text))

        lifecycle = (RUNTIME / "DomainLifecycle.cs").read_text(encoding="utf-8-sig")
        self.assertIn("private void ClearClientDomainReferences()", lifecycle)
        self.assertIn("private void ClearAllDomainReferences()", lifecycle)
        client_body = lifecycle.split("private void ClearClientDomainReferences()", 1)[1].split(
            "private void ClearAllDomainReferences()", 1
        )[0]
        all_body = lifecycle.split("private void ClearAllDomainReferences()", 1)[1]

        missing_client = sorted(name for name in client_fields if f"{name} = null;" not in client_body)
        missing_host = sorted(name for name in host_fields if f"{name} = null;" not in all_body)
        missing_roots = sorted(name for name in committed_roots if f"{name} = null;" not in all_body)
        self.assertEqual([], missing_client, "Client domains missing from rebaseline cleanup: " + ", ".join(missing_client))
        self.assertEqual([], missing_host, "Host domains missing from stop cleanup: " + ", ".join(missing_host))
        self.assertEqual([], missing_roots, "Committed roots missing from stop cleanup: " + ", ".join(missing_roots))

    def test_rebaseline_and_stop_use_central_cleanup(self) -> None:
        client = (RUNTIME / "CitiesMultiplayerSessionV3.Client.cs").read_text(encoding="utf-8-sig")
        session = (RUNTIME / "CitiesMultiplayerSessionV3.cs").read_text(encoding="utf-8-sig")
        self.assertIn("ClearClientDomainReferences();", client)
        self.assertGreaterEqual(session.count("ClearAllDomainReferences();"), 2)


if __name__ == "__main__":
    unittest.main()
