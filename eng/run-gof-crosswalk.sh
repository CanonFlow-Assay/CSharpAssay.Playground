#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sample_root="samples/30-gof-functional-crosswalk"
generated_root="evidence/generated/gof"
repeat_root="evidence/generated/gof-repeat"

cd "$repo_root"

dotnet tool restore
dotnet restore "$sample_root/Gof.Crosswalk.slnx" --locked-mode
dotnet build "$sample_root/Gof.Crosswalk.slnx" \
  --no-restore --configuration Release
dotnet test "$sample_root/tests/Gof.Crosswalk.Tests.csproj" \
  --no-build --no-restore --configuration Release

rm -rf "$generated_root" "$repeat_root"
mkdir -p \
  "$generated_root/classic" \
  "$generated_root/refined" \
  "$repeat_root/classic" \
  "$repeat_root/refined"

run_reports() {
  local output_root="$1"

  dotnet tool run cs-assay -- check \
    "$sample_root/classic/Gof.Classic.csproj" \
    --policy "$sample_root/classic.policy.json" \
    --profile compat \
    --json "$output_root/classic/check.json" \
    --sarif "$output_root/classic/check.sarif"

  dotnet tool run cs-assay -- verify \
    "$sample_root/refined/Gof.Refined.csproj" \
    --policy "$sample_root/refined.policy.json" \
    --profile compat \
    --json "$output_root/refined/verify.json" \
    --sarif "$output_root/refined/verify.sarif"
}

run_reports "$generated_root"
run_reports "$repeat_root"

cmp "$generated_root/classic/check.json" "$repeat_root/classic/check.json"
cmp "$generated_root/classic/check.sarif" "$repeat_root/classic/check.sarif"
cmp "$generated_root/refined/verify.json" "$repeat_root/refined/verify.json"
cmp "$generated_root/refined/verify.sarif" "$repeat_root/refined/verify.sarif"

python3 eng/assert-gof-crosswalk.py
echo "GoF JSON and SARIF are byte-for-byte deterministic across two runs."
