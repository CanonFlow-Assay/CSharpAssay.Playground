# Functional C# Shape v0.1 reference

This hand-authored sample is the executable companion to the normative
Functional C# Profile — Shape v0.1 contract in CSharpAssay. It is not a
`dotnet new` template and does not claim a new executable CSharpAssay profile.

The workflow accepts or rejects an order submission:

```text
API transport -> Domain decision -> Application orchestration
              -> Application-owned port -> Infrastructure effect
```

Domain and Application are the reviewed core. API and Infrastructure are the
imperative shell. The core projects privately consume the locally built
`CsAssay.Analyzers` 0.2.0-rc.1 package; the evidence script freshly installs
`CsAssay.Tool` 0.2.0-rc.1 from the local feed. There are no project references to the
CSharpAssay source repository.

## Reproduce

Use the pinned, separately qualified .NET 11 RC2 SDK. From the Playground root:

```text
./eng/prepare-assay.sh ../CSharpAssay
dotnet restore samples/40-functional-shape-v0.1/Shape.slnx --locked-mode
dotnet build samples/40-functional-shape-v0.1/Shape.slnx \
  --no-restore --configuration Release
dotnet test samples/40-functional-shape-v0.1/Shape.slnx \
  --no-build --no-restore --configuration Release
./eng/run-functional-shape.sh
```

The last command runs CSharpAssay twice and rejects missing projects, compiler
errors, missing tests/evidence, tool failures, incomplete admitted rules,
unexpected finding identities, stale source/policy/project/expectation inputs,
or non-identical JSON/SARIF.

## Evidence boundary

The sample proves only the checked order behavior, dependency graph, closed
Result/Option shape, direct analyzer scope, local package identity, and
configured CSharpAssay evidence. Architecture tests—not a new analyzer—own the
dependency rules. Advisory findings remain visible and require adjudication.

The historical 0.1.2 builder, independent red-team, and non-authoritative judge record
is in [`evaluation/FINAL-REPORT.md`](evaluation/FINAL-REPORT.md).

## Order assurance and Decision Assist

The original 16 `OrderWorkflowTests` are retained as the before selection.
`OrderCoverageTests` adds 39 cases for a total of 55 behavior tests; the seven
architecture tests remain required. The added requirements cover quantity
boundaries, order IDs, line ordering/counts, product normalization, note handling,
all six error codes, DTO round-trips, cancellation propagation, failed storage,
and independent snapshots. Production domain/application/API representations and
the seven admitted Assay rules remain unchanged.

Two new `CSAD0001` advisories identify raw integer quantity parameters in the
boundary tests. These are intentional counterexamples: rejecting zero/negative
input must be tested before constructing a validated `Quantity`. Their exact
fingerprints and agent review rationale are retained in `expected-evidence.json`;
human adjudication remains pending. Together with the original three advisories,
all five remain visible. No suppression, severity reduction, glossary exclusion,
or core/shell reclassification was introduced.

From the Playground root after normal package preparation and Release build:

```sh
./eng/tooling test-shape-investigations
./eng/tooling run-shape-investigations
./eng/run-functional-shape.sh
```

The [mutation manifest](mutation-cases.json) registers 26 exact source edits with
requirements and agent review expectations. They cover five quantity mutations,
three line-limit mutations, five storage/orchestration mutations, four other
validation mutations, and nine API mapping mutations. The harness compiles and
executes every mutation in an isolated copy against both the original 16 tests
and all 55 tests. It rejects failed baselines, failed compilation, unexpected test
counts, skipped tests, process failures, and unexpected final outcomes. The
integer-equivalent quantity mutation remains an unresolved survivor for contract
review. It is never automatically removed from the corpus or a denominator.

Coverage uses `Microsoft.Testing.Extensions.CodeCoverage` 17.14.2, compatible with
the current Microsoft Testing Platform runner. The [Microsoft coverage guide](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)
describes its CLI. `coverage.config` includes all four production assemblies,
including API startup and compiler-generated record members. Both selections use
the same original production sources and coverage scope; tests are excluded.
The pinned collector produced an empty report for the disposable PathMap build
on this host. The harness rejects that result and instead measures the normal
checkout build, then independently checks that both selections pass against the
isolated original before mutating it. The collector report is executable-line
and branch evidence, not proof of requirement completeness.

Read `evidence/generated/shape-orders/investigations.json` only when its
`executionStatus` is `completed`. It binds sources, projects, locks, policy,
manifest, harness, toolchain, and CLI identities. Raw Cobertura and xUnit XML
reports, compiler logs, per-case before/after experiment packets, and advice are
retained beside it. Failed or interrupted work must not be inferred complete
from individual earlier case artifacts.

Decision Assist consumes the executed sample packets and their bounded
requirement descriptions. The legacy `hasBoundaryAssertion` field denotes the
reviewed assertion for that case's declared requirement; it is not automatic
assertion analysis. Line reachability is taken from original-source coverage,
not invented mutant branch coverage. Killed mutations remain local; surviving
cases receive investigation advice with an unchanged Inconclusive experiment
verdict. An authoritative Assay receipt remains separate.

The default try workflow and CI include the offline corpus. An explicitly
requested live comparison can run:

```sh
./eng/tooling run-shape-investigations --provider jev --provider openai --live --max-requests 8
```

This reruns the evidence slice and compares three pre-uplift survivor packets and
one post-uplift survivor packet, up to eight calls across the two providers.
Configure `TYPESAFE_API_KEY` and `OPENAI_API_KEY` through the process environment.
Absent keys are reported as unavailable without model calls; existing credentials
alone never enable inference. Failures stop the live comparison without retry or
fallback. Agent review labels and this small corpus do not qualify provider
quality or prove useful reviewer effort. Human adjudication remains pending.

## Local results on 21 October 2026

| Measure | Original selection | Expanded selection |
|---|---:|---:|
| Behavior tests | 16 | 55 |
| Covered executable lines, four production assemblies | 190/265 (71.70%) | 207/265 (78.11%) |
| Covered branches, same assemblies | 33/60 (55.00%) | 50/60 (83.33%) |
| Mutations detected, fixed 26-case corpus | 12 | 25 |

Thirteen previously surviving mutations were detected by the added tests. The
integer-equivalent quantity mutation remains unresolved; it was not excluded.
All 26 before/after cases completed, producing 52 experiment/advice pairs.
These are targeted sample results, not a general mutation score or proof of all
requirements.

The authoritative Shape gate passed at six projects and 62 tests, retaining all
five advisories with zero blocking findings, missing evidence, or tool failures.
Its repeated JSON/SARIF receipts were byte-identical. The main refined gate also
passed at 72 tests with zero findings; the GoF gate passed its ten tests and
repeatability checks. Across those suites, 144 C# tests, 24 Python evidence/gateway
tests, and eight Gilded Rose characterization checks passed. The existing six
amount cases and ten hypothetical routing scenarios also passed.

Neither Jev nor OpenAI had a configured process key, so their live comparison
recorded zero requests and `unavailable_missing_keys`. A separate five-request
trial used the cached local `qwen3.5:0.8b` model across four pre-uplift gaps from
different areas and the post-uplift equivalent candidate. It agreed with two of
five agent-reviewed routing expectations. In three covered cases with a known
assertion gap it chose reachability instead of assertion inspection. Every
result preserved the Inconclusive experiment verdict. This small trial leaves
the model unqualified; no test or policy was changed to accommodate its answers.
The temporary loopback server was stopped afterward.

The consolidated report is `evidence/generated/order-uplift-validation.json`.
Raw local model outputs and the explicit trial script are under
`evidence/generated/shape-orders/`. Generated records are local and ignored;
their hashes bind the tested working sources, not a published Git revision.

The transport DTO round-trip and cancellation checks cover their declared sample
requirements. EF, messaging, domain serialization, general concurrency,
performance, security, universal purity, business correctness, template
installation, and organization-wide suitability remain unqualified. An LLM
judgment is advisory and cannot override deterministic evidence.
