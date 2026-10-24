# C# preview evidence depth

Scope: CSharpAssay, Playground and DAA only. Other language assays are deferred.
This undertaking begins at published Playground main `401886c`; new work stops
at local Forgejo draft review. CsvHelper feature enhancements remain paused.

This records the earlier depth undertaking. The current boundary/output overhaul
uses a separately qualified .NET 11 RC2 SDK; original RC1 executions remain
historical and are not relabeled. Follow `global.json` for the current exact pin.

## Registered obligations

1. Preserve the exact .NET 11 RC1 toolchain, seven admitted rules, test minima,
   all prior advisory findings and unresolved mutation survivors.
2. Detect blank or duplicate contract identities/outcomes and missing type
   descriptions. Distinguish invalid evidence from an observed contract break.
3. Reject incomplete traces, unknown event kinds, orphan outcomes, attempt gaps
   and events attributed to an unobserved attempt. Preserve pre-admission
   cancellation/rejection/timeout and legitimate Polly retry traces.
4. Validate risk input identities, measured percentages and nonnegative counts.
   Unknown coverage remains unknown. Ordinals remain uncalibrated advice.
5. Preserve explicit module states through the CLI, application assessment and
   registered investigation. Missing/unknown state cannot default to Passed.
6. Run regression checks against real Polly and inventory workers, then the
   existing source-bound release qualification. Retain failures and raw evidence.

## API changes

`ContractAssay.Evaluate` and `ResilienceAssay.Evaluate` return
`PreviewCheck<T>` with `State` and `Issues`. `CodeRiskAssay.Evaluate` returns
`RiskEvaluation` with state, priorities and issues. Valid compatible/compliant
evidence yields Passed; observed violations yield Failed; malformed/incomplete
evidence yields Invalid. DAA projects Invalid to Incomplete and RequestContext.
No preview becomes an admitted rule or gains release authority.

The existing `Compare`, `Check` and `Rank` observation helpers remain available.
Use `Evaluate` when completeness matters; a ranking is not a verification verdict.
An empty risk observation set is Invalid, including in worker probes that do
not request risk analysis; those probes consume only their selected module.

The `assay-preview-module-results/0.1` envelope gains additive `contractState`,
`riskState`, `riskIssues` and `resilienceState` fields. Existing issue/priority
arrays are retained. The coordinator requires explicit states and refuses older
results without them. Freshness checks already require reassessment when the
toolkit changes.

Inventory replay/conflict terminal rows previously attributed the final result
to attempt one after observing attempt two. Their instrumentation now reports
attempt two. The first committed side effect retains its actual attempt-one
identity. Application behavior, requirements and pinned wire contract are intact.

## Evidence and limits

Ten regression controls failed against the original contract/trace helpers;
`evidence/generated/csharp-depth/red-preview.log` retains that result. New checks
also distinguish invalid evidence from real violations, accept unknown coverage
and reject malformed risk observations. Focused green tests do not constitute
the complete 14-gate qualification; its exact clean source receipt is required.

Three additional controls reproduced cancellation being replaced by fallback,
transient failure or timeout even when the declared trace had already cancelled.
Their original Passed results are retained in `red-cancellation.log`. Cancellation
is now terminal for every event kind; allowing fallback never waives cancellation.

This is agent-authored verification, not independent human adjudication. General
OpenAPI/serializer compatibility, distributed/load resilience, calibrated risk,
held-out/provider decision quality, Windows and production readiness remain open.
SecurityAssay and other language assays are deferred by the owner's C#-only
scope. No security verdict is claimed here.

When qualifying against an isolated engine checkout, set `ASSAY_RUNNER` to its
`src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll` and pass the same checkout
through `qualify-csharp-migration --engine-root <checkout>`. The qualifier checks
that runner and declared checkout agree before accepting its seven-gate receipt.
This preserves the prior audit worktree and its original qualification identity.

Forgejo and GitHub have separate squash histories with identical baseline trees.
Local review branches therefore apply only this undertaking onto Forgejo main.
Review reports must bind both exact commits and prove tree equality with the
tested source; a mapped review commit is not a newly executed qualification.
