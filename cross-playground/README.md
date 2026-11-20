# Native C# and F# sample parity

Both playgrounds execute the same 95 cases across 28 sample groups using the
[shared catalogue](native-cases.json). Each uses its own domain implementation.
The C# counterparts use records, native unions, and exhaustive switches. The
F# counterparts use discriminated unions, pattern matching, units of measure,
and immutable folds.

| Cases | What runs in both |
| --- | --- |
| Rule matrix, Gilded Rose, GoF, Functional Shape | Optional input, inventory aging, shipment dispatch, and order validation |
| CsvHelper, Polly, inventory, EF, ExpenseGuard | Culture-aware field conversion, retry decisions, reservation and replay, persisted row validation, and expense thresholds |
| Payment saga | Correlated partner replies, timeouts, late credit, sanctions holds, settlement, safe refunds, and conflicting evidence |
| Enterprise samples | Airline, arangetram, banking, gatepass, hospital, kutcheri, layam, migration, mock drill, sangam, trading, urbanclub, webuy, wecar, and fintech wallet rules |
| Functional patterns, wallet, Decision Assist | Accumulated validation, a pricing capability, debit replay and intent conflicts, and offline mutation advice |

The catalogue links each group to the original sample. Both suites call the
existing domain code where the source project has a matching entry point. Cases
derived from SQL exercise native interpretations of the named constraints.
Wallet replay covers decision semantics and the existing F# debit validation.
The C# offline mutation-advice counterpart pairs with the F# routing workflow.

The shared cases establish behavior for these domain scenarios. Workflows with
real PostgreSQL, HTTP, EF, CsvHelper, Polly, and the SQLite payout journal have
separate, sample-specific integration evidence. The snapshot manifest marks
historical eShop and legacy F# source.

## Run the native cases

Use the repository's pinned .NET 11 SDK. From each playground root, run the
command for that checkout.

```sh
dotnet run --project tests/Parity.Tests/Parity.Tests.csproj -c Release
dotnet run --project tests/Parity.Tests/Parity.Tests.fsproj -c Release
```

CI runs the native tests through the repository's normal qualification. C#
also runs CsAssay with `.csassay.parity.json` on the new core. F# registers the
parity suite in `fsassay-policy.lock.json` and verifies the complete solution.

Use `check` to validate the catalogue and both archive digests. Supply the
paired checkout to also compare the native catalogue bytes.

```sh
./cross-playground/playground check
./cross-playground/playground check ../FSharpAssay.Playground
```

When adding a paired case, update the catalogue in both repositories, map its
inputs to each native implementation, and run both suites. Case IDs identify
individual tests in the registered receipts.

## Browse and reproduce the source archives

The [archive manifest](manifest.json) records the original commits, Git trees,
archive SHA-256 values, and Assay producer pins. These are historical snapshots
of the complete original collections with their test harnesses, build policies,
documentation, and licenses.

```sh
./cross-playground/playground list
./cross-playground/playground checkout csharp /tmp/playground-csharp
./cross-playground/playground checkout fsharp /tmp/playground-fsharp
./cross-playground/playground qualify csharp ../CSharpAssay /tmp/playground-csharp-results
./cross-playground/playground qualify fsharp ../FSharpAssay /tmp/playground-fsharp-results
```

The checkout command verifies the archive against the original tree.
Qualification uses a temporary workspace, restores the pinned Assay producer,
and saves the commands, exit codes, logs, reports, and materialized checkouts
in the new results directory. See the extracted READMEs for prerequisites and
sample-specific commands.
