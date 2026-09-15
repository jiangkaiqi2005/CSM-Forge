#!/usr/bin/env python3
"""Offline structural checks for the v2 design contract, not gameplay validation."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

SHA = re.compile(r"[0-9a-f]{40}")
LINK = re.compile(r"!?\[[^\]\n]*\]\(([^)\n]+)\)")
FENCES = re.compile(r"^\s*(`{3,}|~{3,})")


def without_fences(text: str) -> str:
    """Ignore Markdown code examples while checking local inline links."""
    lines: list[str] = []
    fence: str | None = None
    for line in text.splitlines():
        match = FENCES.match(line)
        if match:
            marker = match.group(1)[0]
            if fence is None:
                fence = marker
            elif marker == fence:
                fence = None
            continue
        if fence is None:
            lines.append(line)
    return "\n".join(lines)


def validate(root: Path) -> list[str]:
    root = root.resolve()
    errors: list[str] = []

    def local_path(value: object, label: str) -> Path | None:
        if not isinstance(value, str) or not value:
            errors.append(f"{label}: missing path")
            return None
        candidate = (root / value).resolve()
        try:
            candidate.relative_to(root)
        except ValueError:
            errors.append(f"{label}: path escapes repository")
            return None
        if not candidate.is_file():
            errors.append(f"{label}: missing file {value}")
            return None
        return candidate

    def read_json(relative: str) -> dict:
        path = local_path(relative, relative)
        if path is None:
            return {}
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                raise ValueError("JSON root must be an object")
            return data
        except (OSError, UnicodeError, ValueError) as exc:
            errors.append(f"{relative}: invalid JSON ({exc})")
            return {}

    def read_text(relative: str) -> str:
        path = local_path(relative, relative)
        if path is None:
            return ""
        try:
            return path.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as exc:
            errors.append(f"{relative}: cannot read UTF-8 ({exc})")
            return ""

    index = read_json("docs/spec/spec-index.json")
    if index.get("schema_version") != 1:
        errors.append("spec-index: unsupported schema_version")
    if not SHA.fullmatch(str(index.get("baseline_commit", ""))):
        errors.append("spec-index: baseline_commit must be an exact commit SHA")
    if index.get("status") not in {"design_baseline", "implementation_tracking"}:
        errors.append("spec-index: invalid tracking status")

    documents = index.get("documents", [])
    if not isinstance(documents, list) or not documents:
        errors.append("spec-index: documents must be a nonempty list")
        documents = []
    seen_documents: set[str] = set()
    for document in documents:
        if isinstance(document, str):
            if document in seen_documents:
                errors.append(f"duplicate document: {document}")
            seen_documents.add(document)
        local_path(document, "document")

    def records(name: str, pattern: str) -> dict[str, dict]:
        values = index.get(name)
        result: dict[str, dict] = {}
        if not isinstance(values, list) or not values:
            errors.append(f"{name}: expected nonempty list")
            return result
        for value in values:
            if not isinstance(value, dict):
                errors.append(f"{name}: entry must be an object")
                continue
            identity = value.get("id")
            if not isinstance(identity, str) or not re.fullmatch(pattern, identity):
                errors.append(f"{name}: invalid ID {identity!r}")
                continue
            if identity in result:
                errors.append(f"{name}: duplicate ID {identity}")
            result[identity] = value
        return result

    requirements = records("requirements", r"F-[A-Z]+-\d{2}")
    packages = records("work_packages", r"WP-\d{2}")
    acceptances = records("acceptances", r"AT-\d{2}")
    master = read_text("docs/TECHNICAL-SPEC.zh-CN.md")
    roadmap = read_text("docs/ROADMAP.zh-CN.md")
    acceptance_text = read_text("docs/spec/ACCEPTANCE.zh-CN.md")

    for identity, requirement in requirements.items():
        if identity not in master:
            errors.append(f"{identity}: absent from technical master")
        target = requirement.get("spec")
        if target not in seen_documents:
            errors.append(f"{identity}: specification not indexed")
        path = local_path(target, identity)
        if path is not None and identity not in path.read_text(encoding="utf-8"):
            errors.append(f"{identity}: absent from assigned specification")
        if requirement.get("work_package") not in packages:
            errors.append(f"{identity}: unknown work package")
        if requirement.get("acceptance") not in acceptances:
            errors.append(f"{identity}: unknown acceptance")

    def evidence(value: dict, verified: bool, minimum_level: int) -> None:
        items = value.get("evidence", [])
        if not isinstance(items, list):
            errors.append(f"{value['id']}: evidence must be a list")
            return
        if verified and not items:
            errors.append(f"{value['id']}: verified status requires evidence")
        highest = -1
        for item in items:
            if not isinstance(item, dict):
                errors.append(f"{value['id']}: invalid evidence record")
                continue
            level = str(item.get("level", ""))
            if not re.fullmatch(r"E[0-4]", level):
                errors.append(f"{value['id']}: evidence level must be E0..E4")
            else:
                highest = max(highest, int(level[1]))
            if not SHA.fullmatch(str(item.get("commit", ""))):
                errors.append(f"{value['id']}: evidence needs an exact commit")
            reference = item.get("reference")
            if not isinstance(reference, str) or not reference:
                errors.append(f"{value['id']}: evidence needs a reference")
            elif not reference.startswith("https://"):
                local_path(reference, value["id"])
        if verified and items and highest < minimum_level:
            errors.append(f"{value['id']}: evidence level does not support status")

    graph: dict[str, list[str]] = {}
    for identity, package in packages.items():
        if not re.search(r"^## " + re.escape(identity) + r"\b", roadmap, re.MULTILINE):
            errors.append(f"{identity}: missing roadmap definition")
        status = package.get("status")
        if status not in {"planned", "implementing", "model_verified", "runtime_verified", "integrated"}:
            errors.append(f"{identity}: invalid implementation status")
        evidence(package, status in {"model_verified", "runtime_verified", "integrated"},
                 3 if status == "runtime_verified" else 1)
        dependencies = package.get("depends_on")
        if not isinstance(dependencies, list) or not all(isinstance(dep, str) for dep in dependencies):
            errors.append(f"{identity}: depends_on must contain IDs")
            dependencies = []
        if len(set(dependencies)) != len(dependencies):
            errors.append(f"{identity}: duplicate dependency")
        for dependency in dependencies:
            if dependency not in packages:
                errors.append(f"{identity}: unknown dependency {dependency}")
        graph[identity] = [dep for dep in dependencies if dep in packages]

    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(identity: str) -> None:
        if identity in visiting:
            errors.append(f"work package dependency cycle at {identity}")
            return
        if identity in visited:
            return
        visiting.add(identity)
        for dependency in graph.get(identity, []):
            visit(dependency)
        visiting.remove(identity)
        visited.add(identity)

    for identity in graph:
        visit(identity)

    for identity, acceptance in acceptances.items():
        if not re.search(r"^### " + re.escape(identity) + r"\b", acceptance_text, re.MULTILINE):
            errors.append(f"{identity}: missing acceptance definition")
        if not any(req.get("acceptance") == identity for req in requirements.values()):
            errors.append(f"{identity}: acceptance has no requirement")
        status = acceptance.get("status")
        if status not in {"not_run", "failed", "model_verified", "runtime_verified", "passed"}:
            errors.append(f"{identity}: invalid acceptance status")
        evidence(acceptance, status in {"model_verified", "runtime_verified", "passed"},
                 3 if status in {"runtime_verified", "passed"} else 1)

    budgets = read_json("docs/spec/budgets.json")
    if budgets.get("status") != "design_target_not_measured":
        errors.append("budgets: this profile must remain explicitly unmeasured")
    try:
        cap, times = budgets["capacity"], budgets["time_ms"]
        sizes, counts = budgets["bytes"], budgets["counts"]
        for group in (cap, times, sizes, counts, budgets["retry"], budgets["performance_targets_ms"]):
            if not isinstance(group, dict) or not group:
                raise ValueError("budget group must be a nonempty object")
            if any(type(number) is not int or number <= 0 for number in group.values()):
                raise ValueError("budgets must use positive integer units")
        relationships = {
            "join capacity": 2 <= cap["max_concurrent_joins"] < cap["max_players_including_host"],
            "heartbeat thresholds": times["heartbeat_interval"] < times["network_degraded_after"] < times["network_expired_after"],
            "join deadlines": times["join_progress_stall"] < times["join_total_deadline"],
            "lease retention": times["join_lease_max_lifetime"] <= times["journal_max_age"],
            "barrier deadline": times["barrier_ttl"] <= times["join_total_deadline"],
            "grant deadline": times["activation_grant_ttl"] <= times["join_total_deadline"],
            "snapshot chunk coverage": counts["max_snapshot_chunks"] * sizes["chunk_data"] >= sizes["max_snapshot"],
            "commit part coverage": counts["max_commit_parts"] * sizes["chunk_data"] >= sizes["max_logical_commit"],
            "chunk frame room": sizes["chunk_data"] < sizes["max_frame_payload"],
            "snapshot cache": sizes["snapshot_cache"] >= sizes["max_snapshot"],
            "state queue total": sizes["state_queues_total"] >= sizes["state_queue_per_peer"] * (cap["max_players_including_host"] - 1),
            "bulk window total": sizes["bulk_inflight_total"] >= sizes["bulk_inflight_per_peer"] * cap["max_concurrent_joins"],
            "catchup spool total": sizes["catchup_spools_total"] >= sizes["catchup_spool_per_join"] * cap["max_concurrent_joins"],
        }
        for name, valid in relationships.items():
            if not valid:
                errors.append(f"budget relation failed: {name}")
    except (KeyError, TypeError, ValueError) as exc:
        errors.append(f"budgets: invalid structure ({exc})")

    # This lightweight check supports inline local links, not remote availability or GFM anchors.
    markdown = list((root / "docs").rglob("*.md"))
    markdown += [root / "README.md", root / "AGENTS.md"]
    for path in markdown:
        if not path.is_file():
            continue
        try:
            text = without_fences(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError) as exc:
            errors.append(f"{path.relative_to(root)}: cannot read ({exc})")
            continue
        for match in LINK.finditer(text):
            destination = match.group(1).strip().split(' "', 1)[0].strip("<>")
            parsed = urlsplit(destination)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            target = (path.parent / unquote(parsed.path)).resolve()
            try:
                target.relative_to(root)
            except ValueError:
                errors.append(f"{path.relative_to(root)}: link escapes repository")
                continue
            if not target.exists():
                errors.append(f"{path.relative_to(root)}: broken local link {destination}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    errors = validate(args.root)
    for error in errors:
        print(f"ERROR {error}", file=sys.stderr)
    if errors:
        print(f"DOCS CHECK FAILED: {len(errors)} issue(s)", file=sys.stderr)
        return 1
    print("DOCS CHECK PASSED: paths, traceability, evidence labels, dependency DAG, and proposed budgets")
    print("This is structural validation only; it does not prove protocol or game implementation.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
