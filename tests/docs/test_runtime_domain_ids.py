"""Source-level runtime contracts that must fail before a real CS1 launch."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RUNTIME = REPO / "src" / "Forge.Runtime.Cities1"
CLASS = re.compile(r"\bclass\s+(\w*AuthorityDomain)\b")
DOMAIN_ID = re.compile(r"\bconst\s+ushort\s+Id\s*=\s*(\d+)\s*;")


class RuntimeDomainContractTests(unittest.TestCase):
    def test_authority_domain_ids_are_unique(self) -> None:
        by_id: dict[int, list[str]] = {}
        missing: list[str] = []
        for path in sorted(RUNTIME.glob("*.cs")):
            lines = path.read_text(encoding="utf-8-sig").splitlines()
            for index, line in enumerate(lines):
                match = CLASS.search(line)
                if match is None:
                    continue
                class_name = match.group(1)
                found = None
                for candidate in lines[index:index + 25]:
                    id_match = DOMAIN_ID.search(candidate)
                    if id_match is not None:
                        found = int(id_match.group(1))
                        break
                    other_class = CLASS.search(candidate)
                    if candidate is not line and other_class is not None:
                        break
                label = f"{path.name}:{class_name}"
                if found is None:
                    missing.append(label)
                else:
                    by_id.setdefault(found, []).append(label)

        self.assertEqual([], missing, "Authority domains without an explicit ushort Id: " + ", ".join(missing))
        duplicates = {domain_id: owners for domain_id, owners in by_id.items() if len(owners) > 1}
        self.assertEqual({}, duplicates, "Duplicate authority domain ids: " + repr(duplicates))


if __name__ == "__main__":
    unittest.main()
