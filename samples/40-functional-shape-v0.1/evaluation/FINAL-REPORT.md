# Functional C# Shape v0.1 consolidated report

Event date: 2026-08-02

## Outcome

Shape v0.1 is ready for human review as a bounded contract plus reference
exercise. It is not a shipped CSharpAssay feature, template, profile, package,
or proof of universal functional purity. No branch has been merged.

Draft PRs:

- [CSharpAssay normative contract #5](https://github.com/CanonFlow-Assay/CSharpAssay/pull/5)
- [CSharpAssay.Playground reference and evaluation #3](https://github.com/CanonFlow-Assay/CSharpAssay.Playground/pull/3)

## Provenance

| Surface | Base | Event commit |
|---|---|---|
| Normative contract | `35b2e9ae935eae758834a70ae4140faaaa280a53` | `5b3766bf5ff00ed4b8d0ccf06c01ac3ac22d1c14` |
| Playground reference | `426c543ef8610d9ba601037c899885d870993c4f` | `b1e4a17aa92eb1c4047aac5c4b66b4b23fbface6` |
| Builder event | `b1e4a17` | `948531bc234c03dfc30087c7eacac81f00c89017` |
| Source binding and first red-team event | `948531b` | `519aa46eec84d7d227a4f4b1c09e109f87eb194d` |
| Completed mutation matrix | `519aa46` | `96de74ad7ee99ef345595fcd49b6c958ee763f25` |
| Human-review corrections | `2efedbccd1d5af46753176dd4a13e74a53a5683a` | `6946bd5d0afcefe007fd661199b09556f6e017b9` |

The executable reference consumes published `CsAssay.Tool` 0.1.2 and
`CsAssay.Analyzers` 0.1.2. It has no source reference to CSharpAssay.

## Reference and builder result

The reference contains Domain, Application, Infrastructure, API, behavior
tests, and architecture tests. Domain owns closed non-null `Result` and
`Option` cases. Application owns its storage port. API and Infrastructure are
the imperative shell.

The builder LLM added one business rule only: five order lines are accepted;
six are rejected as `TooManyLines(5, 6)`, map to
`too_many_order_lines`, and perform zero storage effects. The builder changed
six sample files and did not alter representations, dependencies, package
pins, analyzer scope, required rules, or CSharpAssay behavior.

Review corrections close public invalid-state construction: validated values
have non-public constructors, defaultable value structs are not used, and only
the Domain assembly can construct `AcceptedOrder` after validation.

Release build completed with zero warnings and errors. Shape tests passed
23/23:

- Behavior: 16 passed, 0 failed, 0 skipped.
- Architecture: 7 passed, 0 failed, 0 skipped.

The complete Playground solution passed 42/42 tests.

## CSharpAssay evidence

Published 0.1.2 loaded all six projects, completed all seven required rules,
ran all 23 configured tests, reported no missing evidence or tool failure, and
returned `pass`, exit 0, `authoritative:true`.

No blocking finding remains. Three shell advisories are retained:

| Rule | Count | Adjudication |
|---|---:|---|
| `CSAN0003` | 1 | Test intentionally passes transport `null` to prove one-time conversion to `Option.None`. |
| `CSAN0004` | 2 | Nullable API request property and constructor parameter remain shell concerns. |

The harness clears only this sample's generated build trees before evidence
collection. Two complete clean-harness invocations, each containing two
no-source-change verification runs, produced byte-identical artifacts:

- JSON: `40cd742763d1e6a69c4823cd03cc3cbda79ec9bfdd8ebb93ef2cd0de27e73f92`
- SARIF: `e4a0ffb14f053953d0fe124484c7168a0ac417625b9f7005ec243bc362794148`

The policy, package-lock, analyzer assembly, contract, and expected-evidence
hashes are recorded in `evidence-manifest.json`. Evidence source paths and
SHA-256 values are checked against the current non-generated C# inventory.
The standalone assertion also binds the policy, sample solution, every project
file, and the exact finding expectation file. Findings are matched by rule ID,
path, disposition, and stable CSharpAssay fingerprint.

Focused mutation checks changed each bound input independently. Policy,
project, and expected-evidence mutations each made the assertion exit 1 with a
specific stale-input error; restoring the reviewed bytes returned exit 0.

## Independent evaluation

The required-test-zero and compiler-incompleteness mutations became
non-authoritative. Every required rule became incomplete for the compiler
case. A forbidden Domain project reference failed the architecture test.
Analyzer disable, analyzer-during-build disable, `NoWarn`, and warning
demotion were rejected explicitly. Reviewed rollback succeeded and is labeled
enforcement-disabled, never Pass.

The evaluator discovered a stale-evidence false-pass in the first assertion.
A bounded source-inventory/hash check was added; independent retests proved a
changed existing source and a new unrecorded source now both fail.

Three mutations remain explicit coverage gaps:

1. `DateTime.Now` in Domain is undetected and verification still reports an
   authoritative Pass.
2. A nonexistent path in `boundaries.coreProjects` is omitted from project
   evidence and does not prevent authority.
3. A Domain `JsonPropertyName` attribute is undetected by current rules and
   architecture tests.

Therefore `authoritative:true` establishes completeness for the configured
and loaded qualified evidence; it does not establish those three properties.

## Advisory LLM judgment

The final independent judge verdict is `advisory-accept`. The judge first
rejected the event because two required mutations were missing; after those
runs were preserved as explicit coverage gaps, it accepted the evaluation as
complete. The judgment is non-authoritative and cannot override compiler,
test, CSharpAssay, hash, or human evidence.

The review-requested null-case tests, successful API response characterization,
valid-state construction closure, exact finding identities, and standalone
input binding are now deterministic candidate evidence. The earlier judge
record remains non-authoritative historical evidence from before these human
corrections.

## Limits and human action

Shape v0.1 adds no EF Core, messaging, template, analyzer, rule, CLI command,
CLI profile, package behavior, publication, or release. It does not prove
serialization round-trip, concurrency, external persistence, performance,
security, universal purity, or business correctness.

Human action: review the contract PR and reference PR independently. Verify
the closed representations, core/shell mapping, three advisories, three
coverage gaps, builder diff, and CI artifacts. If acceptable, merge each Draft
PR only by a separately authorized human decision. No merge is authorized by
this report.
