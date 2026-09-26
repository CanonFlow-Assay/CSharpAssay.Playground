"""Shared assertions for the sole supported Assay toolchain."""

from typing import Any


def assert_native_profile(evidence: dict[str, Any]) -> None:
    if evidence["toolVersion"] != "0.2.0-rc.1":
        raise AssertionError("evidence was produced by the wrong Assay version")
    if evidence["requestedProfile"] != "native" or evidence["profile"] != "native":
        raise AssertionError("evidence must use the native profile")
    for project in evidence["projects"]:
        framework = project["targetFramework"]
        if framework != "net11.0" and not framework.startswith("net11.0-"):
            raise AssertionError(f"unsupported target in evidence: {framework}")
        if (
            project["profile"] != "native"
            or project["profileEvidence"] != "native"
            or project["languageVersion"] != "preview"
            or not project["loaded"]
        ):
            raise AssertionError(f"native project evidence is incomplete: {project['name']}")
