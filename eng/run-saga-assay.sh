#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
if (( $# != 2 )); then
  echo 'Usage: ./eng/run-saga-assay.sh <cs-assay.dll> <new-evidence-directory>' >&2
  exit 64
fi
runner=$(realpath -e -- "$1")
sample="$root/samples/110-saga-assay"
mkdir -p -- "$root/evidence/generated"
mkdir -p -- "$(dirname -- "$2")"
mkdir -- "$2"
out=$(realpath -e -- "$2")
work=$(mktemp -d "$root/evidence/generated/saga-controls.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT
domain=src/Saga.Domain/PaymentSaga.cs

capture() {
  local expected=$1 log=$2 observed
  shift 2
  printf '%q ' "$@" >"$log.command"
  printf '\n' >>"$log.command"
  if "$@" >"$log" 2>&1; then observed=0; else observed=$?; fi
  printf '%s\n' "$observed" >"$log.exit"
  if [[ "$observed" != "$expected" ]]; then
    echo "Expected exit $expected, received $observed. Inspect $log." >&2
    return 1
  fi
}

project_files() {
  find src tests -type d \( -name obj -o -name bin \) -prune -o \
    -type f \( -name '*.cs' -o -name '*.csproj' -o -name packages.lock.json \) -print0
}

(
  cd "$sample"
  project_files | sort -z | xargs -0 -r sha256sum
  sha256sum .csassay.json Saga.slnx
) >"$out/sample-inputs.sha256"
sha256sum "$runner" "$root/Directory.Build.props" "$root/Directory.Build.targets" "$root/Directory.Packages.props" "$root/global.json" \
  "$root/NuGet.config" "$root/eng/run-saga-assay.sh" >"$out/runner-and-build-inputs.sha256"
git rev-parse HEAD >"$out/playground-commit.txt"
git status --porcelain -- "$sample" >"$out/sample-status.txt"

prepare_control() {
  local name=$1
  lane="$out/$name"
  control="$work/$name"
  mkdir -- "$lane" "$control"
  (
    cd "$sample"
    project_files | xargs -0 -r cp --parents -t "$control" --
    cp .csassay.json Saga.slnx "$control/"
  )
}

replace_line() {
  local old=$1 replacement=$2 source="$control/$domain" diff_status=0
  if [[ $(grep -Fxc -- "$old" "$source") != 1 ]]; then
    echo "Control anchor must match exactly once in $source." >&2
    return 1
  fi
  awk -v old="$old" -v replacement="$replacement" '
    $0 == old { if (replacement != "") print replacement; next } { print }
  ' "$source" >"$source.changed"
  mv -- "$source.changed" "$source"
  diff -u --label "$domain" --label "$domain" "$sample/$domain" "$source" >"$lane/change.diff" || diff_status=$?
  test "$diff_status" -eq 1
}

build_control() {
  capture 0 "$lane/restore.log" dotnet restore "$control/Saga.slnx" --locked-mode
  capture "$1" "$lane/build.log" dotnet build "$control/Saga.slnx" --no-restore -c Release
}

run_assay() {
  local mode=$1 expected=$2
  capture "$expected" "$lane/$mode.log" dotnet "$runner" "$mode" "$control/Saga.slnx" \
    --profile native --json "$lane/$mode.json" --sarif "$lane/$mode.sarif" \
    --html "$lane/$mode.html" --markdown "$lane/$mode.md"
}

prove_behavior_failure() {
  local test_name=$1
  build_control 0
  run_assay check 0
  jq -e '.verdict == "pass" and (.evidence.authoritative | not)' "$lane/check.json" >/dev/null
  run_assay verify 1
  jq -e --arg name "$test_name" '
    .verdict == "fail" and .evidence.authoritative and
    (.evidence.missingEvidence | length == 0) and (.evidence.failures | length == 0) and
    any(.evidence.tests[]; .required and .outcome == "failed" and
      any(.failureDetails[]; tostring | contains($name)))
  ' "$lane/verify.json" >/dev/null
}

prepare_control mutable-draft
cat >"$control/src/Saga.Domain/LegacyDraft.cs" <<'CS'
namespace Playground.Saga.Domain;

public sealed record LegacyDraft
{
    public decimal? Rate { get; set; }
}
CS
cp "$control/src/Saga.Domain/LegacyDraft.cs" "$lane/"
build_control 0
run_assay verify 1
jq -e '
  .verdict == "fail" and
  any(.evidence.tests[]; .required and .outcome == "passed" and .passed >= 17) and
  ([.evidence.findings[] | select(.scope == "core" and .disposition == "block" and (.suppressed | not)) | .ruleId]
    | index("CSAI0001") != null and index("CSAN0004") != null)
' "$lane/verify.json" >/dev/null

prepare_control missing-refund
replace_line \
  '            new Refund(refundKey, state.Funds.DebitReference, state.Funds.Price.Transfer.Usd), "RefundRequired");' \
  '            new NoAction(), "RefundRequired");'
prove_behavior_failure ConfirmedNonpaymentAndEscrowReleaseRequireOneRefund

prepare_control invented-fx
replace_line '            return Reject("QuoteUnavailable");' \
  '            return Price(state, new QuoteReceived(state.Transfer.Id, new Quote("invented", "USD", "EUR", 1m, observedAt.AddMinutes(1))), observedAt);'
prove_behavior_failure MissingFxStopsWithoutFallbackRate

prepare_control refund-on-timeout
replace_line \
  '        PayoutPending pending => new(state, new ReconcileCredit(pending.Funds.PayoutKey), "PayoutOutcomeUnknown"),' \
  '        PayoutPending pending => new(state, new Refund(Key(pending.Funds.Price, "refund"), pending.Funds.DebitReference, pending.Funds.Price.Transfer.Usd), "PayoutOutcomeUnknown"),'
prove_behavior_failure TimeoutReconcilesBeforeLateCreditWithoutRefund

hold_arm='        SanctionsHold hold => new(state, new Review(hold.CaseReference), "SanctionsHoldNeedsReview")'
prepare_control missing-hold-arm
replace_line "$hold_arm" ''
build_control 1
grep -Fq 'error CS8509' "$lane/build.log"

prepare_control catch-all-hold
replace_line "$hold_arm" '        _ => Ignore(state)'
prove_behavior_failure SanctionsHoldKeepsItsReviewPathAfterTimeout
jq -e 'any(.evidence.findings[]; .ruleId == "CSAU0004")' "$lane/check.json" >/dev/null
grep -Fq 'note CSAU0004' "$lane/check.log"

lane="$out"
control="$sample"
build_control 0
capture 0 "$out/tests.log" dotnet test "$sample/Saga.slnx" --no-build --no-restore -c Release
capture 0 "$out/simulation.log" dotnet "$sample/src/Saga.Demo/bin/Release/net11.0/Saga.Demo.dll" "$out/runtime-traces.json"
run_assay assess 0
jq -e '
  .assessment.schema == "csassay-assessment/0.1" and .assessment.verdict == "Pass" and .assessment.authoritative and
  .receipt.verdict == "pass" and .receipt.evidence.authoritative and
  (.receipt.evidence.projects | length == 3) and
  ([.receipt.evidence.rules[] | select(.required and .outcome == "completed")] | length == 7) and
  any(.receipt.evidence.tests[]; .required and .outcome == "passed" and .passed >= 17) and
  (.receipt.evidence.missingEvidence | length == 0) and (.receipt.evidence.failures | length == 0)
' "$out/assess.json" >/dev/null
(
  cd "$sample"
  sha256sum --check --status "$out/sample-inputs.sha256"
)
sha256sum --check --status "$out/runner-and-build-inputs.sha256"
printf 'Saga assay completed. Evidence is in %s\n' "$out"
