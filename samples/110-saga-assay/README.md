# Cross-border payment saga assay

This sample runs a $100,000 USD-to-EUR transfer through scripted FX, compliance,
ledger, treasury escrow and SWIFT replies. The pure C# 15 reducer returns a
native-union state, action and evidence. The shell records each decision and
its input in a hash-chained journal.

## Recovery decisions

| Observation | Action |
| --- | --- |
| Debit, escrow hold, credit, escrow release or refund times out | Reconcile the pending operation by its original key. Preserve the state. |
| SWIFT reports an unknown payout | Wait for a definitive reply. |
| SWIFT confirms credit at 35 seconds after the 30-second timeout | Complete settlement and release escrow. |
| SWIFT confirms definitive nonpayment | Request escrow release, then refund the original debit after the release confirmation. |
| Ledger definitively rejects the debit | Reject the transfer before creating escrow. |
| Treasury definitively declines the hold and confirms that it created no escrow | Refund the recorded debit. The flow has issued no payout. |
| Treasury rejects escrow release or ledger rejects the refund | Freeze the pending operation and request human review. |
| Correlated credit and definitive nonpayment conflict in either order | Preserve both references and freeze in Disputed, including after settlement or refund. |
| Pre-debit sanctions review clears, denies or escalates the original case | Recheck quote expiry before debit, reject before debit, or keep the hold for review, respectively. |

A simulated SQLite outbox commits a decision snapshot, journal entry and any
money-moving intent in one transaction. A second SQLite file represents the
partner. The demo closes connections and reconstructs the worker before
dispatch, then again after the partner commits but before the worker
acknowledges. Retrying the same refund key produces two delivery attempts and
one simulated effect. A new dispute blocks queued money intents that the worker
has yet to deliver. An insertion failure rolls back both the intent and the
decision.

## Run the comparison

Use the repository-pinned SDK and a Release CSharpAssay runner.

    ./eng/run-saga-assay.sh \
      ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll \
      evidence/generated/saga-recovery

Use a fresh evidence directory. The script requires clean committed sample,
policy and build inputs. It records the Playground commit, SDK/runtime details,
input hashes and the hashes of every regular file in the runner directory,
then checks the same inputs again. The original Assay receipt includes analyzer
and policy identities. The runner directory inventory covers the on-disk
boundary used for this run.

| Control | What detects it |
| --- | --- |
| Mutable core record with a nullable rate | The compiler builds it and all 36 tests pass. Assay verify fails on CSAI0001 and CSAN0004. |
| Missing refund, invented 1.0 FX rate, or refund at SWIFT timeout | Static check passes provisionally. Verify runs the registered tests and fails. |
| Missing SanctionsHold timeout arm | The native-union compiler reports CS8509, enforced by warnings-as-errors. |
| Catch-all hides the SanctionsHold timeout arm | The compiler builds it. Assay reports an advisory CSAU0004 note. The required sanctions test fails verification. |
| Accepting an expired quote or the wrong currency pair | The required FX tests fail. |
| Accepting foreign credit or treating unknown as nonpayment | The correlation or reconciliation test fails. |
| Refunding before escrow release | The required unwind ordering test fails. |
| Ignoring the reverse contradiction or omitting debit reconciliation | The required dispute or timeout test fails. |

Thirteen targeted negative controls execute in disposable copies and preserve
their diffs, commands, exit codes and applicable receipts. Four seeded
history tests generate 128 serial histories across six planned routes. They
inject foreign correlation keys, replays and timeouts and compare decisions to
an independent expected sequence. Failed assertions identify the seed and
first failing prefix. These controls are an authored corpus awaiting
independent adjudication.

Open assess.html for the candidate verdict and dispatch/recovery.json for the
lost-acknowledgement result. The software assessment binds three projects,
seven required Assay rules and the registered tests. The version 2 runtime
journal records simulated inputs, typed state and evidence payloads, actions
and a chain head for each trace. It orders JSON object keys within payloads
and validates nondecreasing observation times. Edits, removal and reordering
are detected against a previously trusted head.

## Scope

One currency corridor, serial event delivery, pre-debit sanctions screening and
scripted partners define the demonstration. Definitive nonpayment and a
definitive refusal to hold funds carry the explicitly modeled external
guarantees in the table. The SQLite exercise recovers committed dispatch intent
with one worker. Production bank authentication, trusted journal anchoring,
durable saga resumption across real providers, worker concurrency and AML/KYC
obligations require external integration evidence.
