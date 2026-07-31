#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 /path/to/cs-assay.dll" >&2
  exit 64
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
assay_dll="$1"
if [[ "$assay_dll" != /* ]]; then
  assay_dll="$repo_root/$assay_dll"
fi
if [[ ! -f "$assay_dll" ]]; then
  echo "CSharpAssay runner not found: $assay_dll" >&2
  exit 66
fi

cd "$repo_root"
mkdir -p \
  evidence/generated/impure \
  evidence/generated/upstream \
  evidence/generated/refined

dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore --configuration Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj \
  --no-build --no-restore --configuration Release
dotnet run \
  --project samples/10-gilded-rose/characterization/GildedRose.Characterization.csproj \
  --no-build --no-restore --configuration Release

dotnet "$assay_dll" check \
  samples/00-rule-matrix/impure/RuleMatrix.Impure.csproj \
  --policy .csassay.impure.json \
  --profile compat \
  --json evidence/generated/impure/check.json \
  --sarif evidence/generated/impure/check.sarif

dotnet "$assay_dll" check \
  samples/10-gilded-rose/harness/GildedRose.Upstream.csproj \
  --policy .csassay.gildedrose-upstream.json \
  --profile compat \
  --json evidence/generated/upstream/check.json \
  --sarif evidence/generated/upstream/check.sarif

dotnet "$assay_dll" verify Playground.Refined.slnx \
  --policy .csassay.refined.json \
  --profile compat \
  --json evidence/generated/refined/verify.json \
  --sarif evidence/generated/refined/verify.sarif

python3 eng/assert-evidence.py
