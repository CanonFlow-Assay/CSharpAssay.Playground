#!/usr/bin/env python3
"""Assert that the pinned eShop agent assay matches its reviewed baseline."""

from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]


def load_json(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as source:
        return json.load(source)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    sample_root = ROOT / "samples/20-eshop-agent-assay"
    report_path = ROOT / "evidence/generated/eshop-agent/check.json"

    try:
        expected = load_json(sample_root / "expectations.json")
        provenance = load_json(sample_root / "provenance.json")
        report = load_json(report_path)
        evidence = report["evidence"]

        require(report["verdict"] == expected["verdict"], "wrong verdict")
        require(report["exitCode"] == 1, "expected policy-failure exit code")
        require(evidence["toolVersion"] == expected["toolVersion"], "wrong tool version")
        require(evidence["authoritative"] is expected["authoritative"], "wrong authority")
        require(len(evidence["projects"]) == expected["projects"], "wrong project count")
        require(len(evidence["findings"]) == expected["findings"], "wrong finding count")
        require(len(evidence["missingEvidence"]) == expected["missingEvidence"], "missing evidence drift")
        require(len(evidence["failures"]) == expected["toolFailures"], "tool failure drift")

        actual_counts = Counter(finding["ruleId"] for finding in evidence["findings"])
        require(actual_counts == Counter(expected["ruleFindingCounts"]), "rule inventory drift")

        actual_upstream = (ROOT / "evidence/generated/eshop-agent/upstream-commit.txt").read_text(
            encoding="utf-8"
        ).strip()
        require(actual_upstream == provenance["commit"], "upstream commit drift")
        require(
            provenance["toolCommit"] == "f5fb8e7dd27da20f6d5c26306dc0e56823e37962",
            "CSharpAssay commit drift",
        )
    except (AssertionError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"eShop evidence assertion failed: {error}", file=sys.stderr)
        return 1

    print("eShop evidence ok: 1 project, 9 findings, 0 missing, 0 failures")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
