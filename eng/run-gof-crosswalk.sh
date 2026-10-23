#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sample_root="samples/30-gof-functional-crosswalk"
generated_root="evidence/generated/gof"
repeat_root="evidence/generated/gof-repeat"

cd "$repo_root"

gof_scratch="$(mktemp -d "${TMPDIR:-/tmp}/csassay-gof.XXXXXX")"
trap 'rm -rf "$gof_scratch"' EXIT
export NUGET_PACKAGES="$gof_scratch/nuget-packages"
export DOTNET_CLI_HOME="$gof_scratch/dotnet-home"
mkdir -p "$NUGET_PACKAGES" "$DOTNET_CLI_HOME" "$gof_scratch/tools"
dotnet tool install CsAssay.Tool --version 0.2.0-rc.1 \
  --tool-path "$gof_scratch/tools" --configfile NuGet.config --no-cache
gof_tool="$gof_scratch/tools/cs-assay"
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

  "$gof_tool" check \
    "$sample_root/classic/Gof.Classic.csproj" \
    --policy "$sample_root/classic.policy.json" \
    --profile native \
    --json "$output_root/classic/check.json" \
    --sarif "$output_root/classic/check.sarif"

  "$gof_tool" verify \
    "$sample_root/refined/Gof.Refined.csproj" \
    --policy "$sample_root/refined.policy.json" \
    --profile native \
    --json "$output_root/refined/verify.json" \
    --sarif "$output_root/refined/verify.sarif"
}

run_reports "$generated_root"
run_reports "$repeat_root"

cmp "$generated_root/classic/check.json" "$repeat_root/classic/check.json"
cmp "$generated_root/classic/check.sarif" "$repeat_root/classic/check.sarif"
cmp "$generated_root/refined/verify.json" "$repeat_root/refined/verify.json"
cmp "$generated_root/refined/verify.sarif" "$repeat_root/refined/verify.sarif"

./eng/tooling assert-gof-crosswalk
echo "GoF JSON and SARIF are byte-for-byte deterministic across two runs."
