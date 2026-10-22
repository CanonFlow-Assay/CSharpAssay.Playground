#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 /path/to/eShop /path/to/cs-assay.dll" >&2
  exit 64
fi

playground_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
eshop_root=$(cd "$1" && pwd)
assay_dll=$(realpath "$2")
dotnet_command=${DOTNET_COMMAND:-dotnet}
expected_commit=9b4f9434f46fdc5c1a6e9e936af2868340cdbc48
sample_root="$playground_root/samples/20-eshop-agent-assay"
evidence_root="$playground_root/evidence/generated/eshop-agent"
solution_path="$eshop_root/eShop.Assay.slnx"
project_path="$eshop_root/src/Ordering.Domain/Ordering.Domain.csproj"

actual_commit=$(git -C "$eshop_root" rev-parse HEAD)
if [[ "$actual_commit" != "$expected_commit" ]]; then
  echo "Expected eShop commit $expected_commit but found $actual_commit" >&2
  exit 65
fi
if [[ -n "$(git -C "$eshop_root" status --porcelain --untracked-files=no)" ]]; then
  echo "The eShop tracked worktree must be clean before evidence capture." >&2
  exit 65
fi
if [[ ! -f "$assay_dll" ]]; then
  echo "CSharpAssay runner not found: $assay_dll" >&2
  exit 66
fi

if [[ ! -f "$solution_path" ]]; then
  "$dotnet_command" new sln \
    --name eShop.Assay \
    --format slnx \
    --output "$eshop_root"
  "$dotnet_command" sln "$solution_path" add "$project_path"
fi

mkdir -p "$evidence_root"
echo "$actual_commit" > "$evidence_root/upstream-commit.txt"

"$dotnet_command" restore "$project_path"
"$dotnet_command" build "$project_path" --no-restore
"$dotnet_command" "$assay_dll" doctor

set +e
"$dotnet_command" "$assay_dll" check "$solution_path" \
  --policy "$sample_root/.csassay.json" \
  --json "$evidence_root/check.json" \
  --sarif "$evidence_root/check.sarif"
assay_exit=$?
set -e

if [[ $assay_exit -gt 2 ]]; then
  echo "CSharpAssay failed operationally with exit code $assay_exit." >&2
  exit "$assay_exit"
fi

echo "Provisional eShop evidence captured with exit code $assay_exit."
