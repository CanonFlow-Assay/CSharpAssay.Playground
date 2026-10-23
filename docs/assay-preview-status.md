# CodeRiskAssay, ContractAssay and ResilienceAssay

All three are implemented in `src/Assay.Workflow.Domain/Modules.cs`, exposed
through the typed SDK and JSON CLI, and integrated into the application workflow.
They remain bounded previews. They do not expand the engine's seven admitted
rules or turn an advisory score into release authority.

The [C# depth undertaking](csharp-preview-depth.md) adds explicit Passed/Failed/
Invalid results, input validation and trace completeness checks. The coordinator
preserves Invalid as incomplete evidence instead of inferring a business failure.

| Module | What runs today | Decision use | Remaining scope |
| --- | --- | --- | --- |
| CodeRiskAssay | Deterministic ranking from changed source, snapshot-bound line coverage, lexical branch count and surviving mutants; reasons accompany every ordinal | Helps prioritize review; advisory only | Calibrated predictive quality, semantic complexity and unmeasured source. Relative paths bind through explicit roots; ambiguous/foreign paths stay unknown. |
| ContractAssay | Compares pinned member identities, types, requiredness and projected outcomes against observed candidate metadata | A required failure can select a registered worker comparison | Full OpenAPI, serializer settings, request/response schemas, endpoints and binary/source compatibility |
| ResilienceAssay | Checks ordered execution traces for attempt/deadline bounds, cancellation, permanent-failure retries, fallback and duplicate side effects; worker checks assert application-specific state | A required failure can select a registered worker comparison | Production load, distributed failures, arbitrary library internals and broader operational qualification |

The Polly consumer supplies real pinned Polly execution, controlled time,
cancellation, breaker, concurrency and fallback assertions. Its registered corpus
has 16 scenario executions and six infrastructure checks. This is consumer
qualification, rather than a functional rewrite of Polly's internals.

The inventory application supplies a second worker with seven scenarios and three
checks against HTTP and PostgreSQL. Its deliberate `remaining` → `available`
wire-name regression demonstrates why passing typed behavior tests alone are
insufficient: ContractAssay observes the compatibility differences and the
registered investigation repeats the comparison. The 22 October
[maturity exercise](maturity-results.md) completed that loop: 25 baseline tests,
two red acceptance tests, 27 passing candidate tests, a reproduced wire failure,
and refusal of unregistered, stale-source and over-budget investigations.
Its mutation corpus retains five kills and one survivor for review.

DAA selects a next investigation or requests context/survivor review. It cannot
grant Pass, suppress findings, execute arbitrary proposed commands or declare a
surviving mutant equivalent. Source/toolkit/artifact freshness, requirement
bindings and an execution budget constrain its registered investigations.

Next maturity work is independent human adjudication and held-out decision
evaluation, followed by deliberately expanded contract/resilience adapters.
Live provider quality and production readiness remain unqualified. Keep the
deterministic baseline visible when evaluating any optional model.
