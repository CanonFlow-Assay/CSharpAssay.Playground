#!/usr/bin/env python3
"""Assert candidate commit, TRX, Assay, source, and EF-model evidence."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
SAMPLE_ROOT = ROOT / "samples/20-eshop-agent-assay"
EVIDENCE_ROOT = ROOT / "evidence/generated/eshop-candidate"
BASE_REPRESENTATION = ROOT / "evidence/expectations/eshop-ordering-domain.json"


def load_json(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as source:
        return json.load(source)


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


def normalized_findings(report: dict[str, Any]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for finding in report["evidence"]["findings"]:
        location = finding["location"]
        result.append(
            {
                "ruleId": finding["ruleId"],
                "path": location["path"],
                "line": location["startLine"],
                "column": location["startColumn"],
                "message": finding["message"],
            }
        )
    return result


def trx_elements(root: ET.Element, local_name: str) -> list[ET.Element]:
    return [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == local_name]


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: assert-eshop-candidate.py /path/to/candidate/eShop", file=sys.stderr)
        return 64

    candidate_root = Path(sys.argv[1]).resolve()
    try:
        expected = load_json(SAMPLE_ROOT / "candidate-expectations.json")
        provenance = load_json(SAMPLE_ROOT / "provenance.json")
        base = load_json(BASE_REPRESENTATION)
        report = load_json(EVIDENCE_ROOT / "check.json")

        actual_commit = str(git(candidate_root, "rev-parse", "HEAD")).strip()
        candidate = provenance["candidate"]
        require(actual_commit == candidate["commit"], "candidate commit drift")
        recorded_commit = (EVIDENCE_ROOT / "candidate-commit.txt").read_text(
            encoding="utf-8"
        ).strip()
        require(recorded_commit == actual_commit, "recorded candidate commit drift")

        for entry in candidate["files"]:
            path = entry["sourcePath"]
            blob = str(git(candidate_root, "rev-parse", f"HEAD:{path}")).strip()
            require(blob == entry["gitBlob"], f"candidate Git blob drift for {path}")
            contents = bytes(git(candidate_root, "show", f"HEAD:{path}", text=False))
            digest = hashlib.sha256(contents).hexdigest()
            require(digest == entry["sha256"], f"candidate SHA-256 drift for {path}")

        base_required = [
            member
            for entry in base["files"]
            for member in entry["requiredMembers"]
        ]
        require(
            base_required == expected["removedRequiredMembers"],
            "base Required member inventory drift",
        )
        require(
            len(base_required) == expected["removedRequiredAttributes"],
            "base Required count drift",
        )
        domain_entries = [
            entry
            for entry in candidate["files"]
            if entry["sourcePath"].startswith("src/Ordering.Domain/")
        ]
        candidate_required: list[str] = []
        for entry in domain_entries:
            contents = bytes(
                git(candidate_root, "show", f"HEAD:{entry['sourcePath']}", text=False)
            ).decode("utf-8-sig")
            candidate_required.extend(required_members(contents))
        require(not candidate_required, f"candidate still has Required attributes: {candidate_required}")

        trx_path = EVIDENCE_ROOT / "test-results/ordering-candidate.trx"
        trx_root = ET.parse(trx_path).getroot()
        counters = trx_elements(trx_root, "Counters")
        require(len(counters) == 1, "expected one TRX Counters element")
        for name, value in expected["trxCounts"].items():
            require(counters[0].get(name) == str(value), f"wrong TRX {name} count")
        results = trx_elements(trx_root, "UnitTestResult")
        require(len(results) == expected["trxCounts"]["total"], "wrong TRX result count")
        outcomes = {result.get("testName", ""): result.get("outcome", "") for result in results}
        for test_name in expected["requiredMappingTests"]:
            require(outcomes.get(test_name) == "Passed", f"mapping test did not pass: {test_name}")

        evidence = report["evidence"]
        require(report["verdict"] == expected["verdict"], "wrong candidate verdict")
        require(report["exitCode"] == expected["exitCode"], "wrong candidate exit code")
        require(evidence["toolVersion"] == expected["toolVersion"], "wrong tool version")
        require(evidence["authoritative"] is expected["authoritative"], "wrong authority")
        require(len(evidence["projects"]) == expected["projects"], "wrong project count")
        require(not evidence["missingEvidence"], "candidate has missing Assay evidence")
        require(not evidence["failures"], "candidate has Assay tool failures")
        require(normalized_findings(report) == expected["findings"], "candidate finding inventory drift")
    except (
        AssertionError,
        ET.ParseError,
        KeyError,
        OSError,
        subprocess.CalledProcessError,
        json.JSONDecodeError,
    ) as error:
        print(f"eShop candidate evidence assertion failed: {error}", file=sys.stderr)
        return 1

    print(
        "eShop candidate evidence ok: 50 passed, 0 failed; "
        "6 Required attributes removed; 4 EF mapping tests passed; "
        "9 remaining Assay findings recorded"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
