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

The toolchain is pinned by `global.json`: .NET 11 RC1 and C# preview, including
native unions. Use Linux/POSIX orchestration for this preview. The Python process
runner and investigation budget lock have not been qualified on Windows.

```sh
./eng/prepare-assay.sh ../CSharpAssay
./eng/run-all-samples.sh
./eng/run-application-workflow.sh
```

The all-sample gate evaluates all 29 projects, builds the 25 runnable sample
projects and shared/test projects, and runs all 301 tests. Six actual invalid
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
into a complete business application. The current runner expects the starter's
16 consumer executions and six infrastructure checks. General worker adapters,
HTTP hosting, classic `.sln` intake and automatic legacy migration are outside
this preview.

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

The engine's separate performance suite still fails: the final isolated p95 was
148.641 ms against the unchanged 50 ms budget. All twenty samples exceeded 50 ms.
The assertion retains raw timings; no threshold or rule was weakened. The full
engine run passed 137 of 138 tests. This open issue prevents claiming complete
engine release qualification, even though the bounded application workflow is
ready for review.

See [the consolidated local report](../evidence/generated/readiness/summary.json)
and [the executed application qualification](../evidence/generated/application-workflow/strict-net11/qualification.json).
The reports are generated local artifacts and are intentionally ignored by Git.
