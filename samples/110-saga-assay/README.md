# Cross-border payment saga assay

This sample runs a $100,000 USD-to-EUR transfer through simulated FX,
compliance, ledger, treasury escrow and SWIFT replies. A pure C# 15 reducer
returns the next state, an action and evidence. The demo records them in order
with a SHA-256 journal. All partner replies are scripted.

In the late-credit scenario, the timeout at 30 seconds leaves the payout
pending and requests reconciliation using its original key. The SWIFT credit
observed at 35 seconds leads to settlement and escrow release. A definitive,
correlated nonpayment result leads to escrow release, then a refund of the
original debit. An unknown result preserves the pending state. Contradictory
settlement observations request review.

## Run the comparison

Use SDK 11.0.100 from the repository's global.json and a matching local
CSharpAssay runner built in Release.

    ./eng/run-saga-assay.sh \
      ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll \
      evidence/generated/saga-assay

Use a fresh output directory for each run. The command makes the negative
controls in disposable copies, retains their exact diffs, commands and exits,
and compares them using unchanged tests. The candidate also runs an independent
Release build and test run.

| Control | Observable result |
| --- | --- |
| Mutable record with a nullable FX rate | The compiler builds it and all 17 tests pass. Assay verify fails on CSAI0001 and CSAN0004. |
| Refund omitted after confirmed nonpayment | Assay check passes provisionally. Verify runs the required tests and fails. |
| Invented typed FX rate of 1.0 | Assay check passes provisionally. Verify fails the missing-quote test. |
| Refund requested at the SWIFT timeout | Assay check passes provisionally. Verify fails the reconciliation test. |
| SanctionsHold timeout arm removed | The native-union compiler reports CS8509, enforced by warnings-as-errors. |
| Catch-all hides the SanctionsHold timeout arm | The build succeeds. CSAU0004 appears as a note. Verify fails the behavioral test for timely sanctions review. |

Open assess.html for the candidate's Assay result. The assess.json envelope
records the source, analyzer, policy and test evidence for three projects and
seven required rules. Open refund-on-timeout/verify.html for a red result and
its failing test. The SARIF for mutable-draft gives an editor or coding agent
exact locations for the two admitted rule violations.

The simulation writes its own runtime-traces.json with partner replies, the FX
quote, correlation keys, decisions and the journal head for each path. Journal
verification against a known head detects content edits, missing entries and
reordering. The Assay envelope records software verification. The journal
records the simulated execution.

## Scope

The example uses one currency corridor, bounded illustrative rate and amount
inputs, pre-debit sanctions screening, and serial event delivery. It relies on
the gateway contract that definitive nonpayment is final for that payout key.
Transfer initiation supplies a distinct transfer ID. The sample exercises
duplicate and out-of-order messages through the reducer. Durable dispatch and
recovery, actual bank idempotency and terminality, distributed concurrency,
and AML/KYC obligations require production integration evidence.
