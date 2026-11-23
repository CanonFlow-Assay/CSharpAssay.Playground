# Retained mutation survivor adjudication

The requesting reviewer approved all four equivalence decisions on 2026-11-23.
The decisions apply to the exact registered source replacements and declared
types below. Each mutation counts as a survivor in its corpus.

| Sample and case | Exact change | Approved equivalence argument | Baseline and mutant tests passed |
| --- | --- | --- | --- |
| [Functional Shape `quantity-equivalent`](../../samples/40-functional-shape-v0.1/mutation-cases.json) | `value <= 0` → `value < 1` | `value` is an `int`. Both predicates are true for the same integers, including `int.MinValue`, because no integer lies between zero and one. | 55 and 55 |
| [CsvHelper `flag-equivalent-pattern`](../../samples/60-csvhelper-functional/review-cases.json) | `numeric == 0` → `numeric is 0` | `numeric` is a `short`. Both expressions are true exactly at zero. The existing test exercises all 65,536 values. | 118 and 118 |
| [Polly `retry-equivalent`](../../samples/70-polly-resilience/mutations.json) | `reply.Kind == ReplyKind.Transient` → `reply.Kind is ReplyKind.Transient` | `Kind` is a nonnullable enum. Both expressions evaluate `Kind` once and match the same underlying enum value, including when an unnamed value is supplied. | 62 and 62 |
| [Inventory `equivalent-positive-check`](../../samples/80-inventory-reservation/mutations.json) | `request.Quantity <= 0` → `request.Quantity < 1` | `Quantity` is an `int`. Both checks reject precisely the same inputs before stock or idempotency decisions. | 27 and 27 |

The local selected-case rerun used the pre-publication Playground commit
`6c6099d6991612fd455be1d49b2c8a2cdddc2683`, engine
`0f44a2e4d389c3a629e302a18ae3db7d4979da6c`, and .NET SDK `11.0.100`.
The local receipt records each mutant and baseline source digest, command,
exit, and test log.

- Receipt `evidence/generated/gemini-cherry-pick/canonical-survivors/selected-cases.json`
- SHA-256 `ab28b9b70a0aa6ab7ea054b9f9a453d5acb7a7eeef4c78cc1d6be7e3344fb5a5`
