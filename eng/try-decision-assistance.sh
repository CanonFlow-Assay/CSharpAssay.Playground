#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# -gt 1 ]]; then
  echo "usage: $0 [path/to/CSharpAssay]" >&2
  exit 64
fi
cd "$repo_root"
if ! command -v dotnet >/dev/null; then
  if [[ -x "${DOTNET_ROOT:-$HOME/.dotnet}/dotnet" ]]; then
    export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
  else
    echo "Install the exact SDK pinned in global.json and put dotnet on PATH." >&2
    exit 69
  fi
fi
# global.json disables SDK roll-forward; the later C# audit checks every evaluated property.
dotnet --version >/dev/null
assay_root="${1:-$repo_root/../CSharpAssay}"
if [[ ! -f "$assay_root/CSharpAssay.slnx" ]]; then
  echo "Provide the matching CSharpAssay source checkout as the first argument." >&2
  exit 66
fi
./eng/prepare-assay.sh "$assay_root"
dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore -c Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj --no-build --no-restore -c Release
./eng/tooling test-all
./eng/tooling run-decision-assistance
./eng/tooling evaluate-decision-assistance
./eng/tooling run-shape-investigations
./eng/tooling run-csvhelper-functional
./eng/tooling run-csvhelper-streaming
./eng/tooling evaluate-csvhelper-advice
printf '%s\n' 'Offline demo complete. No model providers were called. See evidence/generated/{decision-assistance,shape-orders,csvhelper}/.'
