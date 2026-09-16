"""Source-level runtime contracts that must fail before a real CS1 launch."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CLASS = re.compile(r"\bclass\s+(\w*AuthorityDomain)\b")
DOMAIN_ID = re.compile(r"\bconst\s+ushort\s+Id\s*=\s*(\d+)\s*;")
DOMAIN_ALIAS = re.compile(r"\bDomainId\s*\{\s*get\s*\{\s*return\s+(\w*AuthorityDomain)\.Id\s*;\s*\}\s*\}")


class RuntimeDomainContractTests(unittest.TestCase):
    def test_authority_domain_ids_are_unique(self) -> None:
        by_id: dict[int, list[str]] = {}
        aliases: dict[str, str] = {}
        missing: list[str] = []
        explicit_classes: set[str] = set()

        for path in sorted(RUNTIME.glob("*.cs")):
            text = path.read_text(encoding="utf-8-sig")
            lines = text.splitlines()
            for index, line in enumerate(lines):
                match = CLASS.search(line)
                if match is None:
                    continue
                class_name = match.group(1)
                label = f"{path.name}:{class_name}"
                found = None
                alias = None
                for candidate in lines[index:index + 35]:
                    id_match = DOMAIN_ID.search(candidate)
                    if id_match is not None:
                        found = int(id_match.group(1))
                        break
                    alias_match = DOMAIN_ALIAS.search(candidate)
                    if alias_match is not None:
                        alias = alias_match.group(1)
                        break
                    other_class = CLASS.search(candidate)
                    if candidate != line and other_class is not None:
                        break
                if found is not None:
                    explicit_classes.add(class_name)
                    by_id.setdefault(found, []).append(label)
                elif alias is not None:
                    aliases[class_name] = alias
                else:
                    missing.append(label)

        self.assertEqual([], missing,
                         "Authority domains without an explicit ushort Id or explicit alias: " + ", ".join(missing))
        unresolved = {owner: target for owner, target in aliases.items() if target not in explicit_classes}
        self.assertEqual({}, unresolved, "Authority-domain aliases must target an explicit domain id: " + repr(unresolved))
        duplicates = {domain_id: owners for domain_id, owners in by_id.items() if len(owners) > 1}
        self.assertEqual({}, duplicates, "Duplicate explicit authority domain ids: " + repr(duplicates))


if __name__ == "__main__":
    unittest.main()
