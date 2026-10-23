# CsvHelper functional consumer instructions

Follow the repository AGENTS.md and `/home/arun/Developer/AGENTS.md`.

This is a bounded functional consumer plus three source-derived converter
rewrites. Preserve the pinned upstream checkout, attribution, and license.
`upstream.json` identifies a source commit and a separately pinned package
oracle; do not claim those artifacts are identical. Default checks must not
fetch/update GitHub or call models. Upstream execution is an explicit separate
assessment, with original source/tests unchanged.

Domain decisions use immutable data and native unions. CsvHelper, IO, mutable
parser state, cancellation, and transport projection stay in Adapter/CLI.
Keep required rules, nullable enforcement, suppressions, test minima, mutation
scope, and all advisories visible. No engine source dependency or rule changes.
The two SmokeTests are the authored before selection, not upstream coverage.
Never exclude a surviving mutant as equivalent automatically. Case/advisory
reviews are agent judgments pending human adjudication.

After changes, run `./eng/run-csvhelper.sh` from the repository root. It performs
locked restore/build, strict execution and coverage checks, isolated mutations,
offline Decision Assist, and repeated authoritative verification. Existing
paired packages/tool are prerequisites. If converter or adapter source moves,
review and update exact mutation spans/line bindings; do not weaken expectations
because execution fails. Retain all findings in expected-evidence.json with an
explicit rationale. A new finding requires review rather than suppression.

Generated reports belong under ignored `evidence/generated/csvhelper/`.
Report upstream test failures and unsupported target frameworks separately from
the sample's own verdict. No whole-library functional, production-readiness,
performance superiority, or provider-quality claims follow from this sample.


The streaming workload's requirements precede its implementation. Preserve the
literal/closed-form expectations and valid-input direct-mapping comparison;
never accept timings with unequal output digests. `eng/tooling run-csvhelper-streaming`
records Release runs with tiered compilation disabled, raw repetitions, source
and artifact identities. No performance threshold gates CI. Workload benchmark
code belongs in the shell; core+adapter coverage keeps its existing two-module
scope. The full gate requires 118 C# tests and 36 C# evidence-boundary tests.

Review metadata is agent-authored. The finite short-domain equivalence test does
not remove the survivor from the corpus or establish general equivalence
inference. Provider comparison defaults offline; an authorized explicit live
comparison has a six-request maximum and stops on failure. The native engine
scope remains .NET 11; upstream compatibility evidence stays separately scoped.


The hefty physical-file benchmark is opt-in via `eng/tooling run-csvhelper-hefty`.
Require a fresh authoritative sample gate, validate complete SHA-256 output and
aggregate equality before ratios, and exclude the default original's fail-fast
prefix from full-work speed comparisons. Reusable data stays under ignored
`evidence/generated/csvhelper/hefty/data/`. Keep generation outside timing,
record warm file-cache scope and requested runtime modes, and distinguish
cumulative allocations from process-lifetime peak RSS. Preserve the original
package binary identity; do not optimize either path mid-comparison.

Follow-up requirements are registered in workloads/follow-up-requirements.json.
Preserve the archived pre-optimization evidence when comparing against new runs.
Allocation stage probes overlap; do not sum them or call them stack/heap traces.
The bounded exporter uses native union outcomes, 1024-character notes, explicit
culture, caller-owned output and source exception propagation. Do not retry
pending buffers during cleanup after failure/cancellation/abandonment. Progress
does not promise durable or atomic writes.

Round-trip physical measurements are opt-in via eng/tooling run-csvhelper-roundtrip;
require exact complete output byte hashes and independent original re-import
before reporting timings. Normal CI keeps the small round-trip tests. Human
review packets/protocols are preparation only: never fill in a reviewer identity
or mark adjudication/usefulness complete from agent-generated judgments.
