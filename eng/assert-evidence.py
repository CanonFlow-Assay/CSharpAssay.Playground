#!/usr/bin/env python3
"""Assert that generated CSharpAssay reports match the reviewed contract."""

from __future__ import annotations

import hashlib
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


def assert_report(expected: dict[str, Any]) -> None:
    name = expected["name"]
    path = ROOT / expected["path"]
    require(path.is_file(), f"{name}: report is missing: {path}")

    report = load_json(path)
    evidence = report["evidence"]
    require(report["verdict"] == expected["verdict"], f"{name}: wrong verdict")
    require(report["exitCode"] == 0, f"{name}: nonzero recorded exit code")
    require(
        evidence["authoritative"] is expected["authoritative"],
        f"{name}: wrong authority status",
    )
    require(len(evidence["projects"]) == expected["projects"], f"{name}: wrong project count")
    require(not evidence["failures"], f"{name}: tool failures are present")
    require(not evidence["missingEvidence"], f"{name}: required evidence is missing")

    actual_findings = Counter(item["ruleId"] for item in evidence["findings"])
    expected_counts = expected["ruleCounts"]
    expected_findings = Counter(
        {rule_id: count for rule_id, count in expected_counts.items() if count}
    )
    require(actual_findings == expected_findings, f"{name}: unexpected finding inventory")

    rules = {item["id"]: item for item in evidence["rules"]}
    for rule_id, count in expected_counts.items():
        require(rule_id in rules, f"{name}: required rule {rule_id} was not reported")
        require(rules[rule_id]["required"], f"{name}: {rule_id} is not marked required")
        require(rules[rule_id]["outcome"] == "completed", f"{name}: {rule_id} did not complete")
        require(rules[rule_id]["findingCount"] == count, f"{name}: wrong {rule_id} count")

    tests_passed = sum(test["passed"] for test in evidence["tests"])
    require(tests_passed == expected["testsPassed"], f"{name}: wrong passed-test count")
    require(
        all(test["outcome"] == "passed" and test["failed"] == 0 for test in evidence["tests"]),
        f"{name}: a configured test gate did not pass",
    )
    print(f"evidence ok: {name}")


def assert_provenance() -> None:
    sample_root = ROOT / "samples/10-gilded-rose"
    provenance = load_json(sample_root / "provenance.json")
    entries = [*provenance["import"]["files"], *provenance["supportingMaterial"]]
    for entry in entries:
        path = sample_root / entry["localPath"]
        require(path.is_file(), f"provenance: missing {entry['localPath']}")
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        require(actual == entry["localSha256"], f"provenance: hash drift in {entry['localPath']}")
    print(f"provenance ok: {len(entries)} pinned files")


def main() -> int:
    try:
        expectations = load_json(ROOT / "evidence/expectations/reports.json")
        for report in expectations["reports"]:
            assert_report(report)
        assert_provenance()
    except (AssertionError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"evidence assertion failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
