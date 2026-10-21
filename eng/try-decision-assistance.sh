#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# -gt 1 ]]; then
  echo "usage: $0 [path/to/CSharpAssay]" >&2
  exit 64
fi
cd "$repo_root"
if ! command -v python3 >/dev/null; then
  echo "Python 3 is required." >&2
  exit 69
fi
if ! command -v dotnet >/dev/null; then
  if [[ -x "${DOTNET_ROOT:-$HOME/.dotnet}/dotnet" ]]; then
    export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
  else
    echo "Install the exact SDK pinned in global.json and put dotnet on PATH." >&2
    exit 69
  fi
fi
pinned_sdk="$(python3 -c 'import json; print(json.load(open("global.json"))["sdk"]["version"])')"
if [[ "$(dotnet --version)" != "$pinned_sdk" ]]; then
  echo "This checkout requires SDK $pinned_sdk from global.json." >&2
  exit 78
fi
assay_root="${1:-$repo_root/../CSharpAssay}"
if [[ ! -f "$assay_root/CSharpAssay.slnx" ]]; then
  echo "Provide the matching CSharpAssay source checkout as the first argument." >&2
  exit 66
fi
./eng/prepare-assay.sh "$assay_root"
dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore -c Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj --no-build --no-restore -c Release
python3 eng/test-local-advice.py
python3 eng/test-mutation-cases.py
python3 eng/test-shape-investigations.py
python3 eng/run-decision-assistance.py
python3 eng/evaluate-decision-assistance.py
python3 eng/run-shape-investigations.py
python3 eng/test-csvhelper-evidence.py
python3 eng/test-csvhelper-streaming.py
python3 eng/test-csvhelper-hefty.py
python3 eng/test-csvhelper-review.py
python3 eng/test-csvhelper-roundtrip.py
python3 eng/run-csvhelper-functional.py
python3 eng/run-csvhelper-streaming.py
python3 eng/evaluate-csvhelper-advice.py
printf '%s\n' 'Offline demo complete. No model providers were called. See evidence/generated/{decision-assistance,shape-orders,csvhelper}/.'
