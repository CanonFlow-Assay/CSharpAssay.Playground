# Build an application with CSharpAssay and Decision Assist

This preview provides a repeatable coding-agent workflow: declare requirements,
establish a baseline, implement a change, collect evidence, and request a typed
next investigation. The first qualified application shape is a bounded document
fetcher using the real pinned Polly 8.6.5 packages. It is not a port of Polly.

Use this DecisionAssist worktree as the development toolkit. The original sibling
Playground checkout contains separate local work and is not automatically merged
or overwritten. CsvHelper enhancements remain paused; its gate is a regression
check for shared changes.

## Prepare and qualify

The toolchain is pinned by `global.json`: .NET 11 RC2 and C# preview, including
native unions. Orchestration is implemented in C#; process-tree cancellation and
exclusive probe-budget locking use .NET APIs. Executed host qualification remains
Linux; Windows execution is not claimed.

The Playground uses xUnit 4.0.2 and Coverlet.MTP 10.1.0. Microsoft collector
17.14.2 crashed twice; 18.12.0 passed one isolated run but also crashed in the full
workflow. Microsoft static-mode experiments ran tests but produced empty
coverage and were rejected. These failures remain recorded. Coverlet's managed
collector passed two same-binary 25-test PostgreSQL/HTTP runs, measured all three
inventory production assemblies and restored every original DLL/PDB. This is
bounded execution evidence, not a universal runtime guarantee. See
[the collector's documented integration](https://www.nuget.org/packages/coverlet.MTP/10.1.0)
and [Microsoft's platform compatibility guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage#version-compatibility).

Coverage profiles explicitly include the declared production assemblies, clear
attribute exclusions and retain auto-properties/generated union and record code.
Empty, ambiguous or incomplete module reports cannot pass. Before/after coverage
comparisons rerun both selections with this same collector; historical percentages
from another collector are not presented as a measured improvement.

The shared test host copies its profile after SDK default items are evaluated.
Its source-map target decodes the toolkit's explicit compiler `PathMap` against
the application's own physical root, including an independent starter nested
inside another Git checkout. It rejects a conflicting mapping. CodeRisk binds
relative coverage filenames only through explicit roots to the declared source
snapshot; ambiguous or foreign candidates remain unknown.

```sh
./eng/prepare-assay.sh ../CSharpAssay
./eng/run-all-samples.sh
./eng/run-application-workflow.sh
```

On a clean disposable checkout, use
`./eng/prepare-assay.sh ../CSharpAssay --bootstrap-locks` for the preparation step.
Squashing or changing the paired engine revision changes package identities.
This explicit mode commits only refreshed package lock files locally: producer
locks before SDK preparation, then consumer locks afterwards. It refuses a dirty
checkout and does not push either generated commit. CI uses this mode and runs
the subsequent checks with locked restore. Ordinary preparation still requires
producer inputs to be committed deliberately; it does not commit user work.
These generated bootstrap commits identify rebuilt packages, not a new release
qualification of the original publication commit.

The all-sample gate evaluates all 51 listed projects: 45 sample projects plus
six shared or test projects, including real PostgreSQL/HTTP inventory tests.
Docker is required; no fake database or skipped test fallback is used. Six actual invalid
MSBuild configurations must fail with their corresponding mandate diagnostics.
Three intentional negative/upstream fixtures retain reviewed compiler-policy
exceptions; the SDK, target and language mandate still applies to them.

The application workflow command builds and packs local preview SDKs, checks the Polly consumer,
executes six declared mutations, generates an independent application consuming
the packages, and verifies it. It then registers a blank-content requirement,
retains two failing acceptance tests, implements the requirement, and re-verifies
against the original baseline. It also checks that net8 intake remains unsupported
without editing or retargeting that project. Every invocation retains a new app
directory rather than overwriting a previous one.

Evidence is under ignored `evidence/generated/application-workflow/`. Inspect
`qualification.json`, each `run.json`, the underlying `engine-assessment.json`,
raw test/coverage reports, consumer execution and mutation records. A running or
failed qualification is not a completed result. The engine's own performance
suite remains a separate check; this workflow does not waive its budget.
Use a fresh `--output` directory for each qualification; previous completed or
failed qualification evidence is never overwritten.

## Start the next application

After preparation:

```sh
./eng/daa new --output /absolute/path/MyApplication --name MyApplication
./eng/daa intake --manifest /absolute/path/MyApplication/assay.app.json
./eng/daa verify --manifest /absolute/path/MyApplication/assay.app.json \
  --output /absolute/path/MyApplication/evidence/generated/baseline
```

The generated projects are Domain, Adapter, Worker and Tests. Domain contains
immutable records, an explicit native `FetchOutcome` union, and pure decisions.
Adapter owns Polly pipelines, controlled time, transport and cancellation. Tests
consume `CanonFlow.DecisionAssist` and the adapter consumes
`CanonFlow.Assay.Workflow`, both version `0.1.0-preview.1`. The app has no source
project references into the toolkit or engine. Its local NuGet feed points to
this checkout's `.packages` folder; packages are prepared locally, not published.

The scaffold is a working document-fetch starter. A different business domain
needs deliberate requirements, domain implementation, tests, effect adapters,
worker probes and a suitable policy. This command does not turn arbitrary prose
into a complete business application. The starter retains its 16 consumer
executions and six infrastructure checks. The coordinator now accepts declared
scenario/check identity sets and trace contracts. The inventory application
demonstrates a different worker, seven scenarios, three infrastructure checks,
HTTP hosting and PostgreSQL. Classic `.sln` intake and automatic legacy migration
remain outside this preview.

## Implement a requirement

Write a JSON change request:

```json
{
  "schema": "assay-change-request/0.1",
  "description": "Reject successful replies containing only whitespace.",
  "acceptanceCriteria": [
    "Empty and whitespace content produce a permanent failure.",
    "Nonempty content is preserved.",
    "The public response remains compatible."
  ]
}
```

```sh
./eng/daa plan --manifest /absolute/path/MyApplication/assay.app.json \
  --requirement /absolute/path/change-request.json \
  --output /absolute/path/MyApplication/evidence/generated/change
```

Give `work-item.json` to the coding agent with the application. The work item
binds the request, source and registered requirements and states implementation
obligations. The agent adds acceptance tests, records failures, changes the domain
and adapters, and raises test minima when tests are added. Reassess the candidate:

```sh
./eng/daa verify --manifest /absolute/path/MyApplication/assay.app.json \
  --baseline /absolute/path/MyApplication/evidence/generated/baseline/run.json \
  --output /absolute/path/MyApplication/evidence/generated/candidate
```

The candidate must retain the old named test selection, its recorded test count
minimum, and the old contract pin. A different project, changed baseline artifact,
or changed inputs during execution invalidates completion. Changing a public
contract intentionally requires an explicit reviewed baseline migration; there
is no automatic waiver flag.

## APIs and evidence boundaries

The engine adds `cs-assay assess` with the same inputs as `verify`. Its versioned
`csassay-assessment/0.1` envelope embeds the original verification JSON receipt.
Existing `verify` output remains compatible. Provisional evidence stays
provisional.

`Assay.Workflow.Domain` exposes `Assessment`, `ModuleEvidence`, `Requirement`,
`RegisteredProbe` and `AssessmentRules.Evaluate`. The Decision Assist domain API is:

```csharp
ApplicationDecision next = ApplicationAssistance.Decide(assessment, registry, 3);
ApplicationDecision selected = ApplicationAssistance.Select(
    assessment, registry, 3, proposedProbeId, proposalSnapshotSha256);
```

`ApplicationDecision` is a native union of `ProbeRequested`, `ContextRequired`,
`SurvivorReview`, `NoInvestigation`, and `BudgetExhausted`. A proposal can only
select an eligible registered action for an observed failing module on the same
snapshot. Failed baselines, invalid or incomplete execution, and unsupported
scope require local repair/context. Models cannot execute commands, change source,
grant Pass, or remove survivors.

The JSON/stdio API uses the same C# implementation:

```sh
dotnet src/Assay.Workflow.Cli/bin/Release/net11.0/Assay.Workflow.Cli.dll decide packet.json
dotnet src/Assay.Workflow.Cli/bin/Release/net11.0/Assay.Workflow.Cli.dll modules packet.json
```

Generated `decision-input.json` and `module-input.json` are concrete request
examples. Packets reject duplicate or unknown fields and integer enum values.
Receipts carry source, requirements, toolkit and artifact hashes. These are local
integrity bindings, not signed attestations from an untrusted remote party.

`daa investigate --manifest ... --run .../run.json --probe contract --output ...`
executes only an eligible registered probe. It checks freshness and consumes a
locked three-probe budget. Advice and execution remain separate. Default runs
make zero model-provider requests. Existing optional provider adapters retain
their own eligibility rules; live provider quality is not established here.

## Preview module scope

| Module | Executed evidence | Limit |
| --- | --- | --- |
| CSharpAssay | Original authoritative native receipt, compiler/rule/test evidence | Existing admitted policy only; findings remain visible |
| MutationAssay | Isolated exact source edits after a passing original baseline | Six registered cases; one survivor retained; no universal score or automatic equivalence |
| CodeRiskAssay | Change, line coverage, lexical branch count, surviving mutations | Ordinal heuristic with reasons, not defect probability; unknown coverage stays unknown |
| ContractAssay | Pinned public response properties and emitted native outcome projection | Limited response compatibility, not all serializers, endpoints or binary APIs |
| ResilienceAssay | Real Polly controlled scenarios, attempt/deadline/cancellation traces, breaker/concurrency/fallback assertions | Bounded consumer configuration; not exhaustive Polly verification or production load testing |

`ReadyForReview` means required workflow checks passed. It is not release approval,
business acceptance, independent human review, provider qualification, or a waiver
of any failed engine suite. Agent-authored scenarios and equivalent-looking
survivors remain review material.

## Application-independent worker and requirement contracts

The shared SDK exposes immutable `WorkerExecution`, `WorkerScenario` and
`WorkerCheck` records using `assay-worker-execution/0.1`. A manifest declares the
exact scenario keys (`id/variant`), infrastructure check IDs, trace variants and
trace contract. Missing, duplicated or unexpected identities fail closed. Counts
alone cannot establish completeness. Explicit failed observations remain failed.
An isolated compatibility adapter supports previously generated v0.1 fetch apps.

`assay-project/0.2` additionally requires acceptance criteria mapped to actual
executed named tests, with optional scenario keys. These are checked mappings,
not semantic proof that the criteria completely express the business requirement.
Probe registrations bind a unique ID to a requirement and module. The supported
execution pairs are `tests/Behavior`, `worker/ContractAssay` and
`worker/ResilienceAssay`; worker investigations recompute the selected module
comparison. A successful worker process alone cannot clear a contract difference.
Source, toolkit and prior artifact changes invalidate investigation eligibility.
Application runs also pin the canonical manifest and all files under the test
and worker output directories. Changed, added or removed runtime artifacts
require reassessment before any probe consumes budget. Explicit solution, policy,
requirements, test-project, contract-baseline and mutation-corpus inputs are
always snapshotted even if omitted from `inputs`. Older receipts without the
new pins require fresh verification. See the [agent self-adjudication](self-adjudication-2026-10-22.md).
The locked three-execution budget remains separate from immutable evidence hashes.

The manifest's `testReportProtocol` selects `xunit-mtp-v2` for current xUnit XML
options. Existing manifests without the field retain `xunit-mtp-v1`; unknown
protocols cannot select a test command. Both adapters validate individual named
executions and totals rather than trusting process exit or summary counts alone.

Local SDK preparation requires committed producer inputs and records their last
changing commit in `.packages/sdk-source.json`. Assembly, package and SourceLink
identities all use that exact producer commit. Consumer-only commits do not
change the package identity. Unsigned local SDK ZIP metadata is normalized without
changing entry contents so repeat packing retains the archive hash. Rebuilds deliberately invalidate only checkout-local
preview package caches; consumers refresh locks explicitly and verify in locked
mode. These packages remain unpublished.
Only the three SDK producer projects import the generated local revision props;
ordinary application builds retain their own source metadata. A non-incremental
all-project build is used to verify that ordinary consumer builds preserve exact
toolkit binary identities. Package preparation bootstraps the SDK feed before
restoring package-only consumers.

For the second application and its deliberate red/green and contract regression:

```sh
./eng/run-inventory-maturity.sh --output evidence/generated/maturity/final/inventory
```

See [the inventory sample](../samples/80-inventory-reservation/README.md). The
21 October checkpoint below is historical; current execution results belong in
[the maturity report](maturity-results.md) rather than being inferred from it.

## Local qualification checkpoint: 21 October 2026

| Scope | Executed result |
| --- | --- |
| Complete project inventory | 25 runnable sample projects, 29 projects total; all built under the pinned SDK |
| C# suite | 301 passed, zero failed/skipped; eight upstream characterization checks passed |
| Strict mandate rejection | All six incorrect property configurations rejected |
| Polly workflow | Authoritative Pass, 39 tests; nine retained advisories; five mutations killed and one survivor |
| Generated app | Authoritative 14-test baseline, then 16 passing tests after two failing requirement tests; old named tests and contract pin retained |
| Generated candidate mutations | Five killed, one survivor retained; Decision Assist requests survivor review |
| Existing samples | Primary 72, GoF 10, Shape 62 and CsvHelper 118 tests passed in their authoritative scopes |
| Before/after mutation regression | Shape 12 to 25 of 26 killed; CsvHelper 23 of 24 killed; all survivors retained |

Current source and toolkit bindings were checked against the completed workflow
receipts. All model-provider requests were zero. Independent case/advisory review
and held-out/live provider evaluation remain pending.

At that historical checkpoint, the engine's separate performance suite failed: the isolated p95 was
148.641 ms against the unchanged 50 ms budget. All twenty samples exceeded 50 ms.
The assertion retains raw timings; no threshold or rule was weakened. The full
engine run passed 137 of 138 tests. That checkpoint did not establish complete
engine release qualification. Later optimized-CLI qualification is recorded
separately in the maturity and self-adjudication reports.

See [the consolidated local report](../evidence/generated/readiness/summary.json)
and [the executed application qualification](../evidence/generated/application-workflow/strict-net11/qualification.json).
The reports are generated local artifacts and are intentionally ignored by Git.

## Current C# workflow

The application coordinator is now `eng/Assay.Tooling/ApplicationWorkflow.cs`;
execution/evidence parsing, scaffolding and registered worker protocols are
implemented beside it in C#. `eng/daa` launches the .NET executable. To use the
same entry point without Bash after building the tooling project:

```sh
dotnet /path/to/toolkit/eng/Assay.Tooling/bin/Release/net11.0/Assay.Tooling.dll daa intake --manifest /path/to/app/assay.app.json
```

The tooling test project contains the 102 migrated evidence boundaries, six
migration guards and seven self-adjudication regression cases. Source links, cancellation, explicit false live
flags, changed named test selections, and unsigned/signed package handling are
covered. Full source-bound migration qualification is a separate C# command:
`./eng/tooling qualify-csharp-migration --output evidence/generated/csharp-migration/qualified`.
Prepare and commit producer inputs and refreshed package locks before running
it. It invokes the existing Linux build launchers; the application coordinator
itself invokes .NET processes directly and requires no interpreter.
