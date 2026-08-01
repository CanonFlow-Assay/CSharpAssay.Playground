#!/usr/bin/env python3
"""Fail unless GoF reports match the reviewed 0.1.1 evidence contract."""

from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
EXPECTED_PATH = ROOT / "samples/30-gof-functional-crosswalk/expected-findings.json"
ALL_RULES = {
    "CSAA0001", "CSAA0002", "CSAD0001", "CSAD0002", "CSAE0001",
    "CSAE0002", "CSAF0001", "CSAF0002", "CSAI0001", "CSAI0002",
    "CSAI0003", "CSAN0001", "CSAN0002", "CSAN0003", "CSAN0004",
    "CSAP0001", "CSAU0001", "CSAU0002", "CSAU0003", "CSAU0004",
}
REQUIRED_RULES = {
    "CSAI0001", "CSAI0002", "CSAN0001", "CSAN0002", "CSAN0003",
    "CSAN0004", "CSAP0001",
}


def load(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as source:
        return json.load(source)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def assert_package_baseline() -> None:
    manifest = load(ROOT / ".config/dotnet-tools.json")
    tool = manifest["tools"]["csassay.tool"]
    require(tool["version"] == "0.1.1", "tool manifest is not pinned to 0.1.1")
    require(tool["rollForward"] is False, "tool manifest permits roll-forward")

    for lane in ("classic", "refined"):
        lock_path = ROOT / f"samples/30-gof-functional-crosswalk/{lane}/packages.lock.json"
        dependency = load(lock_path)["dependencies"]["net10.0"]["CsAssay.Analyzers"]
        require(dependency["type"] == "Direct", f"{lane}: analyzer is not direct")
        require(dependency["resolved"] == "0.1.1", f"{lane}: analyzer is not 0.1.1")

    for project in ROOT.glob("samples/30-gof-functional-crosswalk/**/*.csproj"):
        project_text = project.read_text(encoding="utf-8")
        require("/CSharpAssay" not in project_text, f"{project}: source reference found")
    print("GoF package baseline ok: published tool and analyzer 0.1.1")


def assert_documentation() -> None:
    sample_root = ROOT / "samples/30-gof-functional-crosswalk"
    readme = (sample_root / "README.md").read_text(encoding="utf-8")
    adjudication = (sample_root / "ADJUDICATION.md").read_text(encoding="utf-8")
    site = (ROOT / "docs/index.html").read_text(encoding="utf-8")
    require(
        "not an automatic\n> functional-C# converter or a correctness proof system" in readme,
        "sample positioning statement changed",
    )
    for rule_id in ("CSAF0001", "CSAD0002", "CSAI0003"):
        require(rule_id in adjudication, f"adjudication omits {rule_id}")
        require(rule_id in site, f"rich documentation omits {rule_id}")
    require("CSharpAssay 0.1.1" in site, "rich documentation omits version baseline")
    print("GoF documentation ok: crosswalk, adjudication, and rich site present")


def assert_json(expected: dict[str, Any]) -> Counter[str]:
    name = expected["name"]
    report = load(ROOT / expected["path"])
    evidence = report["evidence"]
    findings = evidence["findings"]

    require(report["verdict"] == expected["verdict"], f"{name}: wrong verdict")
    require(report["exitCode"] == 0, f"{name}: recorded nonzero exit")
    require(
        evidence["authoritative"] is expected["authoritative"],
        f"{name}: wrong authority status",
    )
    require(len(evidence["projects"]) == expected["projects"], f"{name}: wrong project count")
    require(not evidence["failures"], f"{name}: tool failures are present")
    require(not evidence["missingEvidence"], f"{name}: required evidence is missing")

    actual_rules = {rule["id"] for rule in evidence["rules"]}
    require(actual_rules == ALL_RULES, f"{name}: admitted rule inventory changed")
    actual_required = {rule["id"] for rule in evidence["rules"] if rule["required"]}
    require(actual_required == REQUIRED_RULES, f"{name}: required rule inventory changed")
    require(
        all(
            rule["outcome"] == "completed"
            for rule in evidence["rules"]
            if rule["required"]
        ),
        f"{name}: a required rule was incomplete",
    )

    expected_findings = expected["findings"]
    require(len(findings) == len(expected_findings), f"{name}: unexpected finding count")
    unmatched = list(findings)
    for item in expected_findings:
        match = next(
            (
                finding
                for finding in unmatched
                if finding["ruleId"] == item["ruleId"]
                and item["messageContains"] in finding["message"]
                and finding["location"]["path"] == item["path"]
                and finding["location"]["startLine"] == item["startLine"]
                and finding["severity"] == item["severity"]
                and finding["certainty"] == item["certainty"]
                and finding["disposition"] == item["disposition"]
            ),
            None,
        )
        require(match is not None, f"{name}: missing {item['ruleId']} for {item['messageContains']}")
        unmatched.remove(match)
    require(not unmatched, f"{name}: unmatched findings remain")

    passed = sum(test["passed"] for test in evidence["tests"])
    require(passed == expected["testsPassed"], f"{name}: wrong behavior-test total")
    require(
        all(test["outcome"] == "passed" and test["failed"] == 0 for test in evidence["tests"]),
        f"{name}: behavior test gate failed",
    )
    return Counter(finding["ruleId"] for finding in findings)


def assert_sarif(expected: dict[str, Any], expected_counts: Counter[str]) -> None:
    name = expected["name"]
    sarif = load(ROOT / expected["sarifPath"])
    require(sarif["version"] == "2.1.0", f"{name}: wrong SARIF version")
    runs = sarif["runs"]
    require(len(runs) == 1, f"{name}: wrong SARIF run count")
    results = runs[0].get("results", [])
    counts = Counter(result["ruleId"] for result in results)
    require(counts == expected_counts, f"{name}: SARIF finding inventory differs from JSON")


def main() -> int:
    try:
        expected = load(EXPECTED_PATH)
        require(
            expected["packageBaseline"] == {
                "tool": "CsAssay.Tool@0.1.1",
                "analyzer": "CsAssay.Analyzers@0.1.1",
            },
            "package baseline changed",
        )
        assert_package_baseline()
        assert_documentation()
        for report in expected["reports"]:
            counts = assert_json(report)
            assert_sarif(report, counts)
            rendered = ", ".join(f"{rule}={count}" for rule, count in sorted(counts.items()))
            print(f"GoF evidence ok: {report['name']} ({rendered or 'zero findings'})")
    except (AssertionError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"GoF evidence assertion failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
