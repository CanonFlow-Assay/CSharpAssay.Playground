#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
if (( $# != 2 )); then
  echo 'Usage: ./eng/run-saga-assay.sh <cs-assay.dll> <new-evidence-directory>' >&2
  exit 64
fi
runner=$(realpath -e -- "$1")
runner_dir=$(dirname -- "$runner")
sample="$root/samples/110-saga-assay"
mkdir -p -- "$root/evidence/generated"
mkdir -p -- "$(dirname -- "$2")"
mkdir -- "$2"
out=$(realpath -e -- "$2")
inputs=("$sample" eng/run-saga-assay.sh .github/workflows/playground.yml AllSamples.slnx
  .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.config)
git rev-parse HEAD >"$out/playground-commit.txt"
git status --porcelain --untracked-files=all -- "${inputs[@]}" >"$out/sample-status.txt"
if [[ -s "$out/sample-status.txt" ]]; then
  echo "Qualification requires clean committed sample, policy and build inputs. Inspect $out/sample-status.txt." >&2
  exit 2
fi
minimum=$(jq -er '.release.tests[0].minimumExpectedTests' "$sample/.csassay.json")
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

runner_files() {
  find "$runner_dir" -type f -print0 | sort -z | xargs -0 -r sha256sum
}

(
  cd "$sample"
  project_files | sort -z | xargs -0 -r sha256sum
  sha256sum .csassay.json Saga.slnx
) >"$out/sample-inputs.sha256"
sha256sum "$(realpath -e -- "$(command -v dotnet)")" "${inputs[@]:1}" >"$out/build-inputs.sha256"
runner_files >"$out/runner-directory.sha256"
capture 0 "$out/dotnet-info.log" dotnet --info

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
jq -e --argjson minimum "$minimum" '
  .verdict == "fail" and
  any(.evidence.tests[]; .required and .outcome == "passed" and .passed >= $minimum) and
  ([.evidence.findings[] | select(.scope == "core" and .disposition == "block" and (.suppressed | not)) | .ruleId]
    | index("CSAI0001") != null and index("CSAN0004") != null)
' "$lane/verify.json" >/dev/null

prepare_control missing-refund
replace_line \
  '            new Refund(refundKey, debitReference, price.Transfer.Usd), ProgressKind.RefundRequired);' \
  '            new NoAction(), ProgressKind.RefundRequired);'
prove_behavior_failure ConfirmedNonpaymentAndEscrowReleaseRequireOneRefund

prepare_control invented-fx
replace_line '            return Reject(RejectionReason.QuoteUnavailable);' \
  '            return Price(state, new QuoteReceived(state.Transfer.Id, new Quote("invented", "USD", "EUR", 1m, observedAt.AddMinutes(1))), observedAt);'
prove_behavior_failure MissingFxStopsWithoutFallbackRate

prepare_control refund-on-timeout
replace_line \
  '        PayoutPending pending => Reconcile(state, new ReconcileCredit(pending.Funds.PayoutKey), PartnerOperation.Credit, pending.Funds.PayoutKey),' \
  '        PayoutPending pending => Progress(state, new Refund(Key(pending.Funds.Price, "refund"), pending.Funds.DebitReference, pending.Funds.Price.Transfer.Usd), ProgressKind.RefundRequired),'
prove_behavior_failure TimeoutReconcilesBeforeLateCreditWithoutRefund

hold_arm='        SanctionsHold hold => ReviewSanctions(hold, SanctionsStatus.SanctionsHoldNeedsReview, hold.CaseReference)'
prepare_control missing-hold-arm
replace_line "$hold_arm" ''
build_control 1
grep -Fq 'error CS8509' "$lane/build.log"

prepare_control catch-all-hold
replace_line "$hold_arm" '        _ => Ignore(state)'
prove_behavior_failure SanctionsHoldKeepsItsReviewPathAfterTimeout
jq -e 'any(.evidence.findings[]; .ruleId == "CSAU0004")' "$lane/check.json" >/dev/null
grep -Fq 'note CSAU0004' "$lane/check.log"

prepare_control expired-quote-allowed
replace_line '        if (price.Quote.ExpiresAt <= observedAt)' '        if (price.Quote.ExpiresAt < observedAt)'
prove_behavior_failure QuoteExpiryIsInclusiveAndRecheckedBeforeDebit

prepare_control foreign-currency
replace_line '        if (string.IsNullOrWhiteSpace(quote.Id) || quote.From != "USD" || quote.To != "EUR" ||' \
  '        if (string.IsNullOrWhiteSpace(quote.Id) ||'
prove_behavior_failure InvalidRatesAndCurrencyPairsStopBeforeDebit

prepare_control uncorrelated-credit
replace_line '        if (message is CreditConfirmed paid && paid.Key == funds.PayoutKey && HasReference(paid.Reference))' \
  '        if (message is CreditConfirmed paid && HasReference(paid.Reference))'
prove_behavior_failure CrossTransferAndOutOfOrderRepliesCannotAdvanceState

prepare_control unknown-treated-as-nonpayment
replace_line '            ? Progress(state, new NoAction(), ProgressKind.PayoutStillUnknown)' \
  '            ? RecordNonpayment(funds, "invented-nonpayment")'
prove_behavior_failure RepeatedTimeoutsReconcileTheOriginalPayoutKey

prepare_control refund-before-release
replace_line \
  '        return Progress(new Unwinding(funds, reference, releaseKey), new ReleaseEscrow(releaseKey, funds.EscrowReference), ProgressKind.DefinitiveNonpayment);' \
  '        return BeginRefund(new EscrowReleasedAfterNonpayment(funds, reference, releaseKey), funds.Price, funds.DebitReference);'
prove_behavior_failure ConfirmedNonpaymentAndEscrowReleaseRequireOneRefund

prepare_control reverse-contradiction-ignored
replace_line '            return Dispute(new CreditConflict(rejected.Key, credit.Reference, rejected.Reference), rejected.Reference);' \
  '            return Ignore(state);'
prove_behavior_failure CreditThenCorrelatedNonpaymentDisputesBeforeOrAfterSettlement

prepare_control lost-debit-reconciliation
replace_line '        AwaitingDebit debit => Reconcile(state, new ReconcileDebit(debit.DebitKey), PartnerOperation.Debit, debit.DebitKey),' \
  '        AwaitingDebit => Ignore(state),'
prove_behavior_failure TimeoutReconcilesEachPendingOperationUsingItsOriginalKey

lane="$out"
control="$sample"
build_control 0
capture 0 "$out/tests.log" dotnet test "$sample/Saga.slnx" --no-build --no-restore -c Release
capture 0 "$out/simulation.log" dotnet "$sample/src/Saga.Demo/bin/Release/net11.0/Saga.Demo.dll" "$out/runtime-traces.json" "$out/dispatch"
jq -e '
  .PendingStateAfterReopen == "RefundPending" and .FinalState == "Refunded" and
  .AfterLostAcknowledgement == {"Attempts":1,"Effects":1} and
  .AfterRetry == {"Attempts":2,"Effects":1} and (.PendingIntentRemains | not)
' "$out/dispatch/recovery.json" >/dev/null
run_assay assess 0
jq -e --argjson minimum "$minimum" '
  .assessment.schema == "csassay-assessment/0.1" and .assessment.verdict == "Pass" and .assessment.authoritative and
  .receipt.verdict == "pass" and .receipt.evidence.authoritative and
  (.receipt.evidence.projects | length == 3) and
  ([.receipt.evidence.rules[] | select(.required and .outcome == "completed")] | length == 7) and
  any(.receipt.evidence.tests[]; .required and .outcome == "passed" and .passed >= $minimum) and
  (.receipt.evidence.missingEvidence | length == 0) and (.receipt.evidence.failures | length == 0)
' "$out/assess.json" >/dev/null
(
  cd "$sample"
  sha256sum --check --status "$out/sample-inputs.sha256"
)
sha256sum --check --status "$out/build-inputs.sha256"
runner_files >"$out/runner-directory-after.sha256"
cmp "$out/runner-directory.sha256" "$out/runner-directory-after.sha256"
test "$(git rev-parse HEAD)" = "$(cat "$out/playground-commit.txt")"
test -z "$(git status --porcelain --untracked-files=all -- "${inputs[@]}")"
sha256sum "$out/runtime-traces.json" "$out/dispatch/recovery.json" >"$out/runtime-artifacts.sha256"
printf 'Saga assay completed. Evidence is in %s\n' "$out"
