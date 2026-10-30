# Three changes, three detecting checks

This tutorial uses temporary copies of ExpenseGuard. The real application,
original 189 required tests and twelve existing mutation controls stay intact.
The C# compiler checks language/types; CSAssay runs admitted configured checks
and requires evidence; tests encode particular business expectations. AI review
is advice until an executable check confirms it. None proves every runtime or
business condition.

## Prepare and run

Use the exact `global.json` .NET 11 RC2 SDK, native unions, Docker and the pinned
PostgreSQL image already used by `with-inventory-database`. Follow the engine's
local-feed adoption and qualification instructions first. The tool version here
is development version `0.2.0-rc.1`; public NuGet availability is not assumed.

From a clean disposable Playground checkout, with the qualified engine at the
actual supplied path:

```sh
./eng/prepare-assay.sh ../CSharpAssay-ChangeVerification --qualified-bootstrap-locks
./eng/tooling with-inventory-database -- \
  ./eng/tooling exercise-change-verification \
  --output "$PWD/evidence/generated/expenseguard/change-verification-01"
```

Preparation refreshes local development package locks in this disposable
checkout using the existing explicit bootstrap route. Review that metadata
separately. It does not relax checks or publish packages. The exercise refuses
dirty inputs and existing output directories. Choose a fresh suffix for a new
attempt and retain all previous failures.

`PackageTools.PackedTool` validates a package-scoped tool installation byte for
byte against the local archive. Every stage invokes that real installed CLI,
with the complete fixture solution and its required application tests. This is
not a direct internal-verifier demonstration. No provider request or real
payment is made. Real PostgreSQL tests execute; unavailable infrastructure is a
failure, never a skip fallback.

## A: an admitted structural violation

The isolated copy adds a fixed `ExpenseGuard.ChangeExercise` project and declares
it core **before** the baseline. It starts with:

```csharp
public sealed record Counterexample { public int Value { get; init; } }
```

It has no IDE/build analyzer package: ordinary C# compilation succeeds on both
specimens, and the packaged authoritative verifier performs the admitted checks.
The existing ExpenseGuard Domain project retains its analyzer package and build
enforcement. No existing project is demoted, no severity is reduced, and no grant
is added. The fixture's policy and original required tests stay byte-identical
through baseline, fault and repair.

Temporarily replace `init` with `set`. The expected detector is **CSAI0001**, an
active admitted core blocker: authoritative Fail/1 with all 189 application tests
passed. The JSON records rule, source location, scope and message; HTML/Markdown
include the rule documentation link. `A-explain.log` retains the actual packaged
`explain CSAI0001` result. Restore `init` and run fresh full verification: Pass/0.
This edit is safe for this unused scalar specimen. General set-to-init edits
require caller/contract review and do not imply deep collection immutability.

## B: an established receipt boundary

`PolicyTests.Exact_thresholds` already establishes that exactly 25 base units
without a receipt is Approve, while 25.01 without a receipt is MoreInfo. The
application normalizes currency before comparing the amount to the threshold.
Inspect both boundary values, the FX path and callers of `PolicyEngine.Evaluate`.

Temporarily replace `amount > policy.ReceiptThreshold` with `amount >=
policy.ReceiptThreshold` in `src/ExpenseGuard.Domain/PolicyEngine.cs`. Both forms
compile. The expected result is **required-test Fail/1**, unchanged nonblocking analyzer observations,
and a failure in the unchanged exact-threshold assertion (Approve versus
MoreInfo). The test supplies the business expectation; CSAssay does not infer it.
Restore `>` without editing assertions, expected values, policy or test minima.
Fresh full verification must pass all 189 tests.

## C: required test evidence is absent

In a separate copy, temporarily add a deliberate xUnit `Skip` reason to the
existing `PolicyTests.Missing_fx_never_approves` fact. Its assertion and the
configured required project/minimum 189 remain. The runner executes the other
188 tests and records one NotExecuted result; it is not an empty successful run.

The expected verdict is **Inconclusive/2**, authority false, unchanged nonblocking analyzer
observations, one skipped required test, and
`CSASSAY-REQUIRED-TESTS-NOT-RUN`. This means verification evidence is incomplete;
it does not mean an expense receipt is missing. A missing test project, absent
TRX caused by a failed process, malformed runner output or an unavailable tool
retains its existing tool-failure classification. They are not interchangeable.

Restore the original fact declaration. Full verification must produce fresh
189/189 passing results with no skips or missing evidence.

The full application has existing nonblocking observations; they stay visible.
An additional `C-zero` control analyzes only the fixed pure core specimen under
its own declared policy while requiring the same complete ExpenseGuard test
project, minimum 189 and admitted rule set. Baseline/fault/repair use that same
policy. Its fault must have literally zero findings and still be Inconclusive/2.
This narrowly scoped control does not qualify the whole application; C's final
full-solution repaired run remains mandatory. No application boundary or test
requirement is changed to obtain the full-scope Pass.

## Read and repeat the result

Each `runs/A-baseline` through `runs/C-repaired` directory retains:

- `commands.json`: exact argument arrays and working directory for locked restore,
  build and packaged `verify`; replay arguments without interpreting them as a shell string.
- `inputs.json`: the fixture file inventory and hashes for that stage, including
  uncommitted deliberate edits. A Git commit alone cannot identify those edits.
- `build.log`, `verify.log`, and the actual JSON/SARIF/HTML/Markdown outputs.

`exercises.json` records the starting source, installed tool/archive identities,
stage verdict/exit and report hashes. It is marked completed only if all twelve
stages (including the three supplementary C-zero controls) and source-restoration checks succeed. Earlier failed reports remain
available. Repaired copies must have exactly the baseline source/policy/test
inventory. The final application never contains these deliberate faults.

Do not use a previous stage's pass for changed source, a focused check as a
replacement for full verification, or repeated retries to conceal a failure.
The wrapper returns nonzero when an unexpected outcome occurs. Keep that status
in CI; do not append `|| true`. Existing original qualification remains separate.
