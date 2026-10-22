#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 /path/to/candidate/eShop /path/to/cs-assay.dll" >&2
  exit 64
fi

playground_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
candidate_root=$(cd "$1" && pwd)
assay_dll=$(realpath "$2")
dotnet_command=${DOTNET_COMMAND:-dotnet}
expected_commit=5e92c725c6b13ff2c9cda6de58228ff04c1fb73f
sample_root="$playground_root/samples/20-eshop-agent-assay"
evidence_root="$playground_root/evidence/generated/eshop-candidate"
results_root="$evidence_root/test-results"
test_project="$candidate_root/tests/Ordering.UnitTests/Ordering.UnitTests.csproj"
domain_project="$candidate_root/src/Ordering.Domain/Ordering.Domain.csproj"
solution_path="$candidate_root/eShop.CandidateAssay.slnx"
created_solution=false

cleanup() {
  if [[ "$created_solution" == true && -f "$solution_path" ]]; then
    rm -f -- "$solution_path"
  fi
}
trap cleanup EXIT

actual_commit=$(git -C "$candidate_root" rev-parse HEAD)
if [[ "$actual_commit" != "$expected_commit" ]]; then
  echo "Expected eShop candidate $expected_commit but found $actual_commit" >&2
  exit 65
fi
if [[ -n "$(git -C "$candidate_root" status --porcelain --untracked-files=no)" ]]; then
  echo "The candidate tracked worktree must be clean before evidence capture." >&2
  exit 65
fi
if [[ ! -f "$assay_dll" ]]; then
  echo "CSharpAssay runner not found: $assay_dll" >&2
  exit 66
fi
if [[ -e "$solution_path" ]]; then
  echo "Refusing to overwrite existing candidate assay solution: $solution_path" >&2
  exit 65
fi

mkdir -p "$results_root"
printf '%s\n' "$actual_commit" > "$evidence_root/candidate-commit.txt"

"$dotnet_command" restore "$test_project" --locked-mode
"$dotnet_command" build "$test_project" \
  --no-restore \
  --configuration Release
"$dotnet_command" test \
  --project "$test_project" \
  --no-build \
  --no-restore \
  --configuration Release \
  --no-progress \
  --output detailed \
  --results-directory "$results_root" \
  --report-trx \
  --report-trx-filename ordering-candidate.trx

"$dotnet_command" new sln \
  --name eShop.CandidateAssay \
  --format slnx \
  --output "$candidate_root"
created_solution=true
"$dotnet_command" sln "$solution_path" add "$domain_project"

set +e
"$dotnet_command" "$assay_dll" check "$solution_path" \
  --policy "$sample_root/.csassay.json" \
  --json "$evidence_root/check.json" \
  --sarif "$evidence_root/check.sarif"
assay_exit=$?
set -e

if [[ $assay_exit -gt 2 ]]; then
  echo "Candidate CSharpAssay failed operationally with exit code $assay_exit." >&2
  exit "$assay_exit"
fi

echo "Candidate evidence captured with Assay exit code $assay_exit."
