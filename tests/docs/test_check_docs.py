"""Regression tests for the documentation checker using isolated fixture repositories."""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("forge_check_docs", REPO / "scripts/check_docs.py")
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class DocumentationContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("docs/TECHNICAL-SPEC.zh-CN.md", "# Master\nF-TEST-01\n[design](spec/detail.md)\n")
        self.write("docs/spec/detail.md", "# Design\nF-TEST-01\n")
        self.write("docs/ROADMAP.zh-CN.md", "# Roadmap\n## WP-01 First\n")
        self.write("docs/spec/ACCEPTANCE.zh-CN.md", "# Acceptance\n### AT-01 First\n")
        self.index = {
            "schema_version": 1,
            "status": "design_baseline",
            "baseline_commit": "a" * 40,
            "documents": ["docs/TECHNICAL-SPEC.zh-CN.md", "docs/spec/detail.md",
                          "docs/ROADMAP.zh-CN.md", "docs/spec/ACCEPTANCE.zh-CN.md"],
            "requirements": [{"id": "F-TEST-01", "spec": "docs/spec/detail.md",
                              "work_package": "WP-01", "acceptance": "AT-01"}],
            "work_packages": [{"id": "WP-01", "depends_on": [], "status": "planned", "evidence": []}],
            "acceptances": [{"id": "AT-01", "status": "not_run", "evidence": []}],
        }
        self.budgets = json.loads((REPO / "docs/spec/budgets.json").read_text(encoding="utf-8"))
        self.save()

    def write(self, relative: str, value: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def save(self) -> None:
        self.write("docs/spec/spec-index.json", json.dumps(self.index))
        self.write("docs/spec/budgets.json", json.dumps(self.budgets))

    def errors(self) -> str:
        return "\n".join(MODULE.validate(self.root))

    def test_valid_contract_passes(self) -> None:
        self.assertEqual([], MODULE.validate(self.root))

    def test_missing_document_is_reported(self) -> None:
        (self.root / "docs/spec/detail.md").unlink()
        self.assertIn("missing file", self.errors())

    def test_duplicate_requirement_is_reported(self) -> None:
        self.index["requirements"].append(dict(self.index["requirements"][0]))
        self.save()
        self.assertIn("duplicate ID", self.errors())

    def test_unknown_acceptance_is_reported(self) -> None:
        self.index["requirements"][0]["acceptance"] = "AT-99"
        self.save()
        self.assertIn("unknown acceptance", self.errors())

    def test_unknown_work_package_is_reported(self) -> None:
        self.index["requirements"][0]["work_package"] = "WP-99"
        self.save()
        self.assertIn("unknown work package", self.errors())

    def test_dependency_cycle_is_reported(self) -> None:
        self.index["work_packages"][0]["depends_on"] = ["WP-01"]
        self.save()
        self.assertIn("dependency cycle", self.errors())

    def test_status_cannot_claim_validation_without_evidence(self) -> None:
        self.index["acceptances"][0]["status"] = "passed"
        self.save()
        self.assertIn("requires evidence", self.errors())

    def test_model_evidence_cannot_support_game_status(self) -> None:
        item = self.index["acceptances"][0]
        item["status"] = "runtime_verified"
        item["evidence"] = [{"level": "E1", "commit": "b" * 40, "reference": "docs/spec/detail.md"}]
        self.save()
        self.assertIn("evidence level does not support", self.errors())

    def test_snapshot_chunk_coverage_is_checked(self) -> None:
        self.budgets["counts"]["max_snapshot_chunks"] = 1
        self.save()
        self.assertIn("snapshot chunk coverage", self.errors())

    def test_heartbeat_thresholds_are_checked(self) -> None:
        self.budgets["time_ms"]["network_expired_after"] = 1
        self.save()
        self.assertIn("heartbeat thresholds", self.errors())

    def test_paths_cannot_escape_repository(self) -> None:
        self.index["documents"].append("../outside.md")
        self.save()
        self.assertIn("path escapes repository", self.errors())

    def test_broken_relative_link_is_reported(self) -> None:
        self.write("docs/spec/detail.md", "F-TEST-01\n[missing](absent.md)\n")
        self.assertIn("broken local link", self.errors())

    def test_fenced_examples_are_not_link_checked(self) -> None:
        self.write("docs/spec/detail.md", "F-TEST-01\n```md\n[example](absent.md)\n```\n")
        self.assertEqual([], MODULE.validate(self.root))

    def test_invalid_json_returns_error(self) -> None:
        self.write("docs/spec/spec-index.json", "{broken")
        self.assertIn("invalid JSON", self.errors())

    def test_acceptance_definition_is_required(self) -> None:
        self.write("docs/spec/ACCEPTANCE.zh-CN.md", "# No case heading\n")
        self.assertIn("missing acceptance definition", self.errors())

    def test_boolean_is_not_accepted_as_numeric_budget(self) -> None:
        self.budgets["capacity"]["max_concurrent_joins"] = True
        self.save()
        self.assertIn("positive integer units", self.errors())


if __name__ == "__main__":
    unittest.main()
