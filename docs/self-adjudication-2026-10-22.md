# Agent self-adjudication, 22 October 2026

This review covers the merged CSharpAssay engine and Playground/DAA workflow.
It is agent self-review, not independent human adjudication, live-provider
evaluation or production approval. The starting merged commits were engine
`8fa9bbd` and Playground `dc39920`. Both feature worktrees were clean. The older
RuntimeProfile worktree was clean; the original Playground worktree retained its
five existing edits and was not reset or advanced behind those edits.

## Findings

| ID | Severity | Reproduced weakness | Correction |
| --- | --- | --- | --- |
| SA-E01 | High | Engine TRX summaries and incomplete caller-supplied test records could authorize Pass | Reconcile individual outcomes, counts, identities, runner counters and exits; missing evidence removes authority. The paired engine has 12 new regression cases. |
| SA-W01 | High | Investigation freshness covered toolkit binaries but omitted compiled application binaries | Record hashes for the test/worker output trees after build; check them after verification and before/after a probe. Replacement, addition and deletion are refused before probe selection or budget use. |
| SA-W02 | High | A manifest could omit required configuration files from its declared input list; the manifest itself was not separately pinned | Always include the explicitly referenced solution, policy, requirements, test project, contract baseline and mutation corpus. Pin the canonical parsed manifest separately and reread it at completion. |
| SA-W03 | Medium | Traces from other scenarios could hide one missing selected scenario trace | Every trace-selected scenario must supply a nonempty trace before aggregation. |
| SA-X01 | Medium | Both engineering CLIs omitted InvalidDataException from their handled failures, causing aborts on expected validation errors | Handle the explicit exception type; add one process-level rejection test in each repository. |

Five freshness cases and one partial-trace case reproduced the coordinator gaps
before correction. Their failing reports remain under
`evidence/generated/self-adjudication/red-freshness/` and `red-worker/`.
The engine's failing reports are under its own `artifacts/self-adjudication/`.
All 20 new regression cases concern demonstrated evidence failures. Existing
test selections, admitted rules, strict toolchain settings, findings, advisories
and unresolved mutation survivors remain intact. Tooling test minima increase
from 108 to 115, including the worker group from 18 to 19. CLI rejection failures
are retained under `red-cli/`; the engine's first completed 154-test qualification
is retained under `artifacts/self-adjudication/engine-first-qualified/` before the
final qualification that includes its additional CLI case.

Old application receipts without manifest and application-artifact pins require
a fresh verification before investigation. This deliberately tightens reuse of
local evidence; it does not silently upgrade old receipts. The
`manifestSha256` field hashes canonical JSON, while application artifact entries
hash the actual file bytes. These are local integrity bindings, not signatures
or protection against an actor rewriting both a receipt and all its evidence.

## Executed qualification contract

The fix producer inputs must be committed before SDK preparation. Refresh the
paired qualified engine archives and package-consuming lock files, then run the
complete source-bound 14-gate qualifier into a fresh directory:

```sh
./eng/tooling qualify-csharp-migration --output evidence/generated/self-adjudication/qualified
```

That receipt, not this prose, records actual completion, exact commits, gate
states and evidence hashes. The inventory exercise additionally substitutes a
worker binary and changes its manifest; both must be refused without consuming
budget, after which the unchanged registered contract investigation must still
run and reproduce the wire differences. Engine qualification retains all seven
release gates and the original performance workload/budget.

Prior generated evidence and engine package bytes were copied before the audit
to `../brainstorm/assay-reimagined/self-adjudication-before-20261022/`, outside
both Git repositories. New raw failures and qualification remain ignored local
evidence. No GitHub writes, public publication or automatic human approval are
part of this review. New fixes use separate review branches; the prior merged
qualification continues to identify its original commits.

## Adjudication of existing observations

The engine's first corrected self-assay was authoritative Pass with no missing
evidence or failures and exactly the previous 41 advisory fingerprints. Their
distribution was 29 in
the qualification shell, four in analyzer tests, three in reporting, two each in
runner/workspaces, and one in the analyzer. In particular, 26 null-forgiving and
two null-value observations in qualification code remain shell maintenance debt.
The remaining observations concern array exposure, exception handling,
loop readability and Boolean state modeling. They were not suppressed, newly reclassified
or treated as proof of correctness. This audit fixes the demonstrated evidence
failures; it does not assert that every advisory has been eliminated.

The final CLI regression test adds one `CSAF0002` advisory at
`tests/CsAssay.Runner.Tests/QualificationExitTests.cs:35`, bringing the engine's
final total to 42. All original fingerprints remain and neither declared core
has a finding. The new loop appends process arguments in order through
`ProcessStartInfo.ArgumentList`; its effects belong in the test shell. Retaining
that explicit loop is a contextual choice, rather than replacing it with a
pipeline for side effects. The advisory remains visible and unsuppressed.

- Controlled rule-matrix findings remain intentional positive controls.
- The upstream Gilded Rose nullable finding remains a true observation within
  that legacy fixture; ordinary mutable classes remain a known detector limit.
- Classic GoF findings remain contextual advisories. Their refined derivative
  does not establish universal superiority over the original patterns.
- Shape and CsvHelper mutation survivors remain unresolved review items.
  Neither equivalence nor a waiver is inferred from this self-review.
- CodeRisk ordinals remain advisory and uncalibrated; unknown coverage remains
  unknown. Contract and resilience checks cover declared members/outcomes and
  bounded traces, rather than arbitrary serializer or distributed behavior.
- Ten offline decision scenarios remain draft expectations. No provider-quality
  inference follows from them or from this audit.

## Remaining work

Independent human review, a held-out consumer, explicit live-provider evaluation,
Windows-host validation and production operational qualification remain open.
Source inventories still require deliberate declaration of other build inputs;
the six mandatory inputs do not discover every possible MSBuild import or asset.
Runtime environment, database state and external services are not immutable
binary identities. The inventory sample remains an application exercise with no
production authentication, outbox dispatcher or migration lifecycle.
