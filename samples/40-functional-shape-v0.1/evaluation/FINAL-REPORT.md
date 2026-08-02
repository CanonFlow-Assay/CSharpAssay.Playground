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
| Normative contract | `35b2e9ae935eae758834a70ae4140faaaa280a53` | `8ef9a70b1b354130b52c1eff8e4b57afef2c1700` |
| Playground reference | `426c543ef8610d9ba601037c899885d870993c4f` | `b1e4a17a95b91b5ac077406ad66decd1aa6b50e8` |
| Builder event | `b1e4a17` | `948531bc234c03dfc30087c7eacac81f00c89017` |
| Source binding and first red-team event | `948531b` | `519aa46eec84d7d227a4f4b1c09e109f87eb194d` |
| Completed mutation matrix | `519aa46` | `96de74ad7ee99ef345595fcd49b6c958ee763f25` |

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

Release build completed with zero warnings and errors. Tests passed 16/16:

- Behavior: 10 passed, 0 failed, 0 skipped.
- Architecture: 6 passed, 0 failed, 0 skipped.

## CSharpAssay evidence

Published 0.1.2 loaded all six projects, completed all seven required rules,
ran all 16 configured tests, reported no missing evidence or tool failure, and
returned `pass`, exit 0, `authoritative:true`.

No blocking finding remains. Three shell advisories are retained:

| Rule | Count | Adjudication |
|---|---:|---|
| `CSAN0003` | 1 | Test intentionally passes transport `null` to prove one-time conversion to `Option.None`. |
| `CSAN0004` | 2 | Nullable API request property and constructor parameter remain shell concerns. |

The harness clears only this sample's generated build trees before evidence
collection. Two complete clean-harness invocations, each containing two
no-source-change verification runs, produced byte-identical artifacts:

- JSON: `9a498712ec75f93eec95cbe146f4131c55c4a84848cf600b5977b7095eef9f3f`
- SARIF: `dad047c97d217562bbbe38b9d6961a1bfbc142ef99083ff2456a58405ab5a44f`

The policy, package-lock, analyzer assembly, contract, and expected-evidence
hashes are recorded in `evidence-manifest.json`. Evidence source paths and
SHA-256 values are checked against the current non-generated C# inventory.

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

Open human questions include direct null-case tests, successful API response
characterization, public construction paths for domain values, binding finding
fingerprints rather than counts, and whether standalone freshness checks must
also bind policy/project files.

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
