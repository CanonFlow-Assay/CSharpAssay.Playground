#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sample_root="samples/40-functional-shape-v0.1"
generated_root="evidence/generated/functional-shape-v0.1"
repeat_root="evidence/generated/functional-shape-v0.1-repeat"
scratch="$(mktemp -d "${TMPDIR:-/tmp}/csassay-shape.XXXXXX")"
trap 'rm -rf "$scratch"' EXIT

cd "$repo_root"

# CSharpAssay inventories compiler inputs. Remove only this sample's generated
# build trees so stale Debug/Release inputs cannot change artifact bytes.
find "$sample_root" -type d \( -name bin -o -name obj \) \
  -prune -exec rm -rf -- {} +

mkdir -p "$scratch/dotnet-home" "$scratch/nuget-packages" "$scratch/tools"
export DOTNET_CLI_HOME="$scratch/dotnet-home"
export NUGET_PACKAGES="$scratch/nuget-packages"
# Candidate provenance is recorded separately. Excluding the Git suffix from
# generated AssemblyInfo avoids a recursive artifact-hash/candidate-SHA cycle.
export IncludeSourceRevisionInInformationalVersion=false

dotnet tool install CsAssay.Tool \
  --version 0.1.2 \
  --tool-path "$scratch/tools" \
  --configfile NuGet.config \
  --no-cache
tool="$scratch/tools/cs-assay"

dotnet restore "$sample_root/Shape.slnx" \
  --locked-mode --configfile NuGet.config
dotnet build "$sample_root/Shape.slnx" \
  --no-restore --configuration Release
dotnet test "$sample_root/Shape.slnx" \
  --no-build --no-restore --configuration Release

rm -rf "$generated_root" "$repeat_root"
mkdir -p "$generated_root" "$repeat_root"

"$tool" check "$sample_root/Shape.slnx" \
  --policy "$sample_root/.csassay.json" \
  --profile compat \
  --json "$generated_root/check.json" \
  --sarif "$generated_root/check.sarif" \
  --html "$generated_root/check.html"

run_verify() {
  local output_root="$1"
  "$tool" verify "$sample_root/Shape.slnx" \
    --policy "$sample_root/.csassay.json" \
    --profile compat \
    --json "$output_root/verify.json" \
    --sarif "$output_root/verify.sarif" \
    --html "$output_root/verify.html"
}

run_verify "$generated_root"
run_verify "$repeat_root"

cmp "$generated_root/verify.json" "$repeat_root/verify.json"
cmp "$generated_root/verify.sarif" "$repeat_root/verify.sarif"

python3 eng/assert-functional-shape.py
sha256sum \
  "$generated_root/verify.json" \
  "$generated_root/verify.sarif"
echo "Shape v0.1 JSON and SARIF are byte-for-byte deterministic."
