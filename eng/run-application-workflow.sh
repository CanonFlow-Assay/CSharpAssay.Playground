#!/usr/bin/env bash
set -euo pipefail
workflow_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$workflow_root"
./eng/prepare-application-workflow.sh
./eng/tooling qualify-application-workflow "$@"
