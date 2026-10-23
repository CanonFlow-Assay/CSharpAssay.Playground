# CSharpAssay and Decision Assist maturity exercise

This page retains the earlier undertaking's evidence. The subsequent
[agent self-adjudication](self-adjudication-2026-10-22.md) records current fixes
and its separate qualification contract; historical receipts below do not
qualify changed tooling.

This undertaking adds a second application shape, inventory reservations over
real HTTP and PostgreSQL, to the existing Polly document-fetch workflow. The
same typed assessment and decision APIs consume both workers. Native unions and
immutable domain decisions remain enforced through the pinned .NET 11 preview
toolchain and the existing functional policies. Database, HTTP and provider
effects remain in adapters and orchestration.

The engine at `05a5e3b2ce200da1e00372ad105d0766876cdd35` completed all seven
local release-qualification gates and all 142 tests. Its unchanged performance
workload measured 30.1319 ms p95 against the 50 ms limit. The shipped CLI and
performance fixture explicitly disable tiered compilation and use optimized JIT.
That result does not qualify the default editor/compiler host. Earlier failed
performance runs remain retained; a cold-start observation increased from
0.724 to 1.209 seconds and is not a repeated startup benchmark.

## Executed application checks

All five local milestones completed: full paired engine qualification, generic
worker/probe contracts, the real inventory application, a preregistered
requirement change, and an executed contract regression with bounded DAA
investigations. The final inventory and generic starter receipts are under
`evidence/generated/maturity/final/inventory-source-bound/` and
`application-source-bound/`; `final/summary.json` consolidates their checked
hashes, exact source identities and regression evidence. Earlier failed attempts
remain retained.

| Inventory check | Executed result |
| --- | --- |
| Independent package-consuming baseline | 25 passing tests; real HTTP/PostgreSQL; ready for review |
| Preregistered quantity-cap change | Two red acceptance tests, then 27 passing tests; previous named tests and contract pin retained |
| Isolated mutation corpus | Five killed and one unresolved survivor in baseline and candidate |
| Actual response wire-name regression | All 27 typed behavior tests passed; ContractAssay failed |
| Registered DAA investigation | Reproduced removed `remaining` and new required `available` differences |
| Execution safeguards | Unregistered, stale-source and exhausted-budget probes refused |
| Optional providers | Zero requests; deterministic baseline retained separately |

Inventory requirements precede implementation. The registered quantity-cap
change adds two acceptance tests, preserves historical idempotent receipt replay,
and raises the minimum from 25 to 27. The qualifier retains the failing tests,
reassesses the implementation against the original named tests and contract pin,
then exercises a separate response-wire regression.

The contract regression changes the real JSON field `remaining` to `available`
while typed behavior tests can still pass. ContractAssay compares observed
response metadata with the pinned contract. DAA selects the requirement-bound
`inventory-contract` investigation, which reruns the worker and recomputes the
differences. Unregistered probes, stale source and exhausted budgets must be
refused before execution. The model cannot invent or execute shell commands.

The real Polly consumer completed 39 tests, 16 scenario executions and six
infrastructure checks. Its generated package-consuming starter passed 14 baseline
tests, then two failing requirement tests, followed by a 16-test candidate.
Previous named tests and the contract pin were retained. Both candidate and
Polly mutation corpora retain five kills and one survivor. Both applications
reached `ReadyForReview`; their next decision retains survivor review. The
separate .NET 8 intake example remained unsupported with its source unchanged.

## Shared regression results

The primary, GoF, Shape and CsvHelper authoritative gates passed with their
required rules, test minima and reviewed findings preserved. GoF, Shape and
CsvHelper repeated JSON/SARIF receipts were byte-identical. CsvHelper feature
enhancements remain paused; its production source was not enhanced for this
undertaking.

The complete 34-project inventory (30 sample projects) built under the strict
toolchain. After correcting test-profile/source-map targets, affected hosts were
rebuilt, all strict properties were re-audited, and all 328 C# tests plus eight
characterization checks passed again. Six invalid toolchain configurations were
rejected; the three reviewed negative/upstream exceptions remain explicit.

| Scope | Passing tests | Retained findings |
| --- | ---: | --- |
| Primary refined gate | 72 | Six advisories |
| GoF refined gate | 10 | Zero; classic examples retain their expected counterexamples |
| Shape | 62 | Five reviewed findings, unchanged fingerprints |
| CsvHelper consumer | 118 | Ten advisories |
| Python workflow, worker, advice and evidence boundaries | 66 | Invalid, incomplete and stale evidence remains rejected |
| CsvHelper Python boundaries | 36 | Streaming, hefty-workload, review and round-trip boundaries |

Existing mutation and coverage comparisons were re-executed with the same
managed collector for each before/after selection. These remeasure earlier test
improvements; they are not new production-source enhancements or comparisons
between different collectors.

| Comparison | Selected tests | Killed mutations | Line coverage | Branch coverage |
| --- | --- | --- | --- | --- |
| Shape | 16 → 55 | 12 → 25 of 26 | 81.86% → 88.84% | 57.41% → 88.89% |
| CsvHelper | 2 → 118 | 4 → 23 of 24 | 43.35% → 98.27% | 25.41% → 87.70% |

Each comparison retains one unresolved survivor. Shape's four production
assemblies have 215 collector-reported executable lines and 54 branches in both
selections; CsvHelper's two assemblies have 173 lines and 122 branches in both.
The two CsvHelper smoke tests are an authored before selection, not upstream
CsvHelper test coverage. The controlled streaming comparison completed with
validated output equality and no performance threshold gate. Large physical-file
benchmarks were not rerun as part of this shared regression check.

The six-case offline mutation fixture retained its three survivors, one kill,
one invalid mutation and one failed-baseline case. Ten hypothetical routing
scenarios completed offline against draft expectations. Neither fixture is
independently adjudicated decision-quality evidence.

## Preview boundaries and remaining work

[CodeRiskAssay, ContractAssay and ResilienceAssay](assay-preview-status.md) are
implemented bounded previews. CodeRisk supplies an ordinal with reasons; it is
not a calibrated prediction. Its adapter resolves explicit Cobertura source
roots against the declared source snapshot. Relative paths bind when unique;
ambiguous and foreign paths remain unknown. An executed starter prototype binds
all four production source files to measured coverage. Unmeasured sources remain
unknown; source changes and surviving mutations retain their separate reasons.

In the executed inventory candidate, the changed domain file ranks at ordinal
six for its change and surviving mutation. The API host and entry point each
rank at three for measured coverage below the unchanged 80% triage boundary.
The worker retains unknown coverage; the store has no flags in the supplied
inputs. These observations prioritize review and do not change the verdict.

Contract checks cover declared public response members and projected outcomes.
Resilience checks cover registered traces and worker assertions. Broader OpenAPI
and serializer compatibility, production load, security and operational failure
qualification remain open. Inventory has no production authentication, outbox
dispatcher or migration lifecycle. One agent-authored contract regression does
not establish held-out decision quality.

The visible deterministic baseline and typed DAA route are reported separately.
Optional live providers were not invoked. Independent human adjudication and
held-out/provider evaluation remain required before making quality claims.
Surviving mutations and advisories remain review items, without automatic
equivalence declarations or waived evidence.

## Reproduction and review

Use a clean, committed producer graph and the exact pinned SDK. Prepare the
qualified engine archives with `./eng/prepare-assay.sh ../CSharpAssay --qualified`.
SDK producer identity is pinned separately from later consumer-only commits;
normalized unsigned package archives preserve their original entry contents.
Default non-incremental consumer builds have been checked to preserve toolkit
DLL, runtime-configuration and dependency identities.

The regression pass exposed two setup failures that remain retained: Shape's
project input hash still described the old coverage dependency, and the two
standalone sample test hosts did not copy their explicit collector profiles.
The stale hash was refreshed for the reviewed project change. Both hosts now
copy their profiles beside the test DLL. The default extra workload assembly in
the rejected CsvHelper report was not accepted as the declared two-module scope.
The shared test target applies profile metadata after SDK default items exist.
It also decodes this toolkit's explicit compiler `PathMap` using each
application's physical root. The previous generated starter incorrectly used its
enclosing Git checkout as that root and produced empty coverage; that failure
remains retained. The corrected independent prototype passed 14 tests, measured
both required modules and reported source roots inside its own application.
The build rejects a contradictory `PathMap` instead of guessing a source root.
Coverage readers resolve explicit roots and reject missing, ambiguous and
out-of-scope files. Policies, finding fingerprints, production module scope and
test minima remain intact.

Run `./eng/run-inventory-maturity.sh --output <fresh-directory>` with Docker
available, and `./eng/run-application-workflow.sh --output <fresh-directory>` for
Polly and the packaged starter. Both retain failures and require fresh output
directories. Shared sample gates remain regression checks; CsvHelper feature
enhancements are paused. Both selections of each coverage comparison run with
the same managed collector, so historical collector percentages are not treated
as a measured uplift.

Review uses only the two existing local Forgejo draft PRs. Local qualification
statuses describe executed local commands; Forgejo Actions and fresh hosted CI
did not run. No GitHub writes, merges or public package publication are included.

## C# tooling migration undertaking

The owner subsequently requested removal of the Python coordinator and harnesses
before local integration. The results above describe the earlier qualified
revision; its raw evidence is retained. The replacement lives in
`eng/Assay.Tooling` with xUnit boundaries in `tests/Assay.Tooling.Tests`.
Current migration results are recorded separately under
`evidence/generated/csharp-migration/`; do not reuse the historical qualification
as evidence for changed tooling. The Python-only local SetFit bridge is retired;
the C# provider protocol can still address a separately managed endpoint.
