#!/usr/bin/env python3
"""Assert the reviewed Ordering representation at an immutable eShop revision."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
EXPECTATIONS = ROOT / "evidence/expectations/eshop-ordering-domain.json"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def git(repo: Path, *arguments: str, text: bool = True) -> str | bytes:
    return subprocess.run(
        ["git", "-C", str(repo), *arguments],
        check=True,
        capture_output=True,
        text=text,
    ).stdout


def required_members(source: str) -> list[str]:
    type_match = re.search(r"(?:public\s+)?class\s+(\w+)", source)
    require(type_match is not None, "expected a class declaration")
    type_name = type_match.group(1)
    declaration = re.compile(
        r"\[Required\]\s+(?:public|private)\s+[^;{=]+\s+"
        r"(_[A-Za-z]\w*|[A-Za-z]\w*)\s*(?:[;{])"
    )
    return [f"{type_name}.{match}" for match in declaration.findall(source)]


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: assert-eshop-ordering.py /path/to/pinned/eshop", file=sys.stderr)
        return 64

    repo = Path(sys.argv[1]).resolve()
    try:
        with EXPECTATIONS.open(encoding="utf-8") as source:
            expected: dict[str, Any] = json.load(source)

        actual_commit = str(git(repo, "rev-parse", "HEAD")).strip()
        require(actual_commit == expected["commit"], "eShop HEAD is not the reviewed commit")

        finding_count = 0
        for entry in expected["files"]:
            path = entry["sourcePath"]
            blob = str(git(repo, "rev-parse", f"HEAD:{path}")).strip()
            require(blob == entry["gitBlob"], f"unexpected Git blob for {path}")

            contents = bytes(git(repo, "show", f"HEAD:{path}", text=False))
            digest = hashlib.sha256(contents).hexdigest()
            require(digest == entry["sha256"], f"unexpected SHA-256 for {path}")

            actual_members = required_members(contents.decode("utf-8-sig"))
            require(
                actual_members == entry["requiredMembers"],
                f"unexpected Required attributes in {path}: {actual_members}",
            )
            finding_count += len(actual_members)

        require(finding_count == 6, "wrong Ordering domain Required-attribute count")
        print(
            "eShop Ordering evidence ok: "
            f"{actual_commit}, {len(expected['files'])} pinned files, "
            f"{finding_count} exact Required attributes"
        )
    except (
        AssertionError,
        json.JSONDecodeError,
        OSError,
        subprocess.CalledProcessError,
    ) as error:
        print(f"eShop Ordering evidence assertion failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
