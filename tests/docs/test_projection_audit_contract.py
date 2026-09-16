"""Projection audit is evidence only until a dedicated recovery request exists."""
from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"


class ProjectionAuditContractTests(unittest.TestCase):
    def test_projection_audit_cannot_mutate_recovery_state(self) -> None:
        text = (RUNTIME / "ProjectionAudit.cs").read_text(encoding="utf-8-sig")
        forbidden = (
            "FenceSession(",
            "BeginPeerResync(",
            "NeedsSnapshot",
            "MessageKindV2.",
            "SendClientFrame(",
            "SendServerFrame(",
        )
        for token in forbidden:
            self.assertNotIn(token, text, f"Diagnostic projection audit must not perform recovery/network action: {token}")
        self.assertIn("diagnostic-only projection drift", text)
        self.assertIn("clientProjectionTicks < 256", text)

    def test_projection_audit_is_wired_and_cleared(self) -> None:
        forge_mod = (RUNTIME / "ForgeMod.cs").read_text(encoding="utf-8-sig")
        lifecycle = (RUNTIME / "DomainLifecycle.cs").read_text(encoding="utf-8-sig")
        client = (RUNTIME / "CitiesMultiplayerSessionV3.Client.cs").read_text(encoding="utf-8-sig")
        self.assertIn("AuditClientProjection();", forge_mod)
        self.assertIn("ClearClientProjectionAudit();", lifecycle)
        self.assertIn("InitializeClientProjectionAudit();", client)
        self.assertIn("ObserveClientProjectionBatch(batch);", client)


if __name__ == "__main__":
    unittest.main()
