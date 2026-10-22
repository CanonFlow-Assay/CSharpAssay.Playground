#!/usr/bin/env bash
set -euo pipefail
inventory_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$inventory_root"
./eng/prepare-application-workflow.sh
./eng/tooling with-inventory-database -- ./eng/tooling qualify-inventory-maturity "$@"
