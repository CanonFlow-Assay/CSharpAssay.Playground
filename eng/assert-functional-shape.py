#!/usr/bin/env python3
"""Fail unless Shape v0.1 matches its reviewed 0.1.2 evidence contract."""

from __future__ import annotations

import hashlib
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "samples/40-functional-shape-v0.1"
EXPECTED = SAMPLE / "expected-evidence.json"
INPUTS = SAMPLE / "evidence-inputs.json"
REPORT = ROOT / "evidence/generated/functional-shape-v0.1/verify.json"
SARIF = ROOT / "evidence/generated/functional-shape-v0.1/verify.sarif"
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


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def assert_packages(expected: dict[str, Any]) -> None:
    require(
        expected["packageBaseline"] == {
            "tool": "CsAssay.Tool@0.1.2",
            "analyzer": "CsAssay.Analyzers@0.1.2",
        },
        "published package baseline changed",
    )
    for project_name in ("Shape.Domain", "Shape.Application"):
        lock = load(SAMPLE / f"src/{project_name}/packages.lock.json")
        package = lock["dependencies"]["net10.0"]["CsAssay.Analyzers"]
        require(package["type"] == "Direct", f"{project_name}: analyzer not direct")
        require(package["resolved"] == "0.1.2", f"{project_name}: analyzer not 0.1.2")

    for project in SAMPLE.rglob("*.csproj"):
        require(
            "/CSharpAssay" not in project.read_text(encoding="utf-8"),
            f"{project}: source project reference found",
        )


def assert_source_binding(evidence: dict[str, Any]) -> None:
    recorded = {
        source["path"]: source["sha256"]
        for source in evidence["sources"]
        if source["path"].startswith(("src/", "tests/"))
        and "/obj/" not in source["path"]
        and "/bin/" not in source["path"]
    }
    current = {
        path.relative_to(SAMPLE).as_posix(): hashlib.sha256(
            path.read_bytes()
        ).hexdigest()
        for root in (SAMPLE / "src", SAMPLE / "tests")
        for path in root.rglob("*.cs")
        if "obj" not in path.parts and "bin" not in path.parts
    }
    require(recorded == current, "evidence source inventory or hash is stale")


def assert_input_binding(evidence: dict[str, Any], inputs: dict[str, Any]) -> None:
    policy = SAMPLE / ".csassay.json"
    expected_hash = sha256(EXPECTED)
    policy_hash = sha256(policy)
    require(inputs["policySha256"] == policy_hash, "policy input lock is stale")
    require(
        evidence["policy"]["sha256"] == policy_hash,
        "evidence policy hash is stale",
    )
    require(
        inputs["expectedEvidenceSha256"] == expected_hash,
        "expected-evidence input lock is stale",
    )

    current_projects = {
        path.relative_to(SAMPLE).as_posix(): sha256(path)
        for path in [SAMPLE / "Shape.slnx", *sorted(SAMPLE.rglob("*.csproj"))]
    }
    require(
        inputs["projectFiles"] == current_projects,
        "solution or project input lock is stale",
    )


def finding_identity(finding: dict[str, Any]) -> dict[str, str]:
    return {
        "ruleId": finding["ruleId"],
        "path": finding["location"]["path"],
        "disposition": finding["disposition"],
        "fingerprint": finding["fingerprint"],
    }


def main() -> int:
    try:
        expected = load(EXPECTED)
        inputs = load(INPUTS)
        report = load(REPORT)
        evidence = report["evidence"]
        assert_packages(expected)
        assert_source_binding(evidence)
        assert_input_binding(evidence, inputs)

        require(report["schemaVersion"] == "1.2.0", "wrong evidence schema")
        require(report["verdict"] == "pass", "verify verdict is not pass")
        require(report["exitCode"] == 0, "verify recorded nonzero exit")
        require(evidence["toolVersion"] == "0.1.2", "wrong tool version")
        require(evidence["authoritative"] is True, "verify is not authoritative")
        require(len(evidence["projects"]) == expected["projects"], "wrong project count")
        require(all(project["loaded"] for project in evidence["projects"]), "project not loaded")
        require(
            not any(project["compilerDiagnostics"] for project in evidence["projects"]),
            "compiler diagnostics are present",
        )
        require(not evidence["failures"], "tool failures are present")
        require(not evidence["missingEvidence"], "required evidence is missing")

        required = {rule["id"] for rule in evidence["rules"] if rule["required"]}
        require(required == REQUIRED_RULES, "required rule inventory changed")
        require(
            all(
                rule["outcome"] == "completed"
                for rule in evidence["rules"]
                if rule["required"]
            ),
            "required rule outcome is incomplete",
        )
        require(
            not any(finding["disposition"] == "block" for finding in evidence["findings"]),
            "blocking finding is present",
        )

        test_counts = {
            Path(test["input"]).stem: test["passed"]
            for test in evidence["tests"]
        }
        require(test_counts == expected["tests"], "configured test totals changed")
        require(
            all(
                test["outcome"] == "passed"
                and test["failed"] == 0
                and test["skipped"] == 0
                for test in evidence["tests"]
            ),
            "configured test did not pass cleanly",
        )

        actual_findings = sorted(
            (finding_identity(finding) for finding in evidence["findings"]),
            key=lambda finding: finding["fingerprint"],
        )
        expected_findings = sorted(
            expected["findings"],
            key=lambda finding: finding["fingerprint"],
        )
        require(actual_findings == expected_findings, "finding identity changed")
        sarif = load(SARIF)
        sarif_findings = sorted(
            ({
                "ruleId": result["ruleId"],
                "path": result["locations"][0]["physicalLocation"]
                    ["artifactLocation"]["uri"],
                "disposition": result["properties"]["disposition"].lower(),
                "fingerprint": result["partialFingerprints"]["csAssay/v1"],
            }
            for run in sarif["runs"]
            for result in run.get("results", [])),
            key=lambda finding: finding["fingerprint"],
        )
        require(sarif_findings == expected_findings, "SARIF finding identity changed")

        actual = Counter(finding["ruleId"] for finding in actual_findings)
        rendered = ", ".join(f"{rule}={count}" for rule, count in sorted(actual.items()))
        print(f"Shape v0.1 evidence ok ({rendered or 'zero findings'})")
    except (AssertionError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"Shape v0.1 evidence assertion failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
