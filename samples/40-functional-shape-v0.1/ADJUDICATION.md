# Shape v0.1 adjudication

The accepted reference requires zero admitted blocking findings in Domain and
Application. It does not require zero advisory findings.

The expected finding inventory is machine-readable in
`expected-evidence.json`. Every observed finding must be listed there and
classified before the Draft PR is eligible for human review.

## Standing human decisions

- `Result<TValue,TError>` is closed to non-null `Success` and `Failure` cases.
- `Option<T>` is closed to non-null `Some` and payload-free `None` cases.
- Domain is pure core.
- Application is framework-free core orchestration through Application-owned
  ports.
- API and Infrastructure are the imperative shell.
- Deterministic compiler, test, CSharpAssay, provenance, and hash evidence
  outrank LLM judgment.

## Known boundaries

## Observed 0.1.2 findings

| Rule | Count | Location | Disposition | Human adjudication |
|---|---:|---|---|---|
| `CSAN0003` | 1 | Behavior test constructing a transport request with `null` | Advisory retained | Purposeful shell-boundary characterization: `null` is translated immediately to `Option.None` and never enters a domain case. |
| `CSAN0004` | 2 | `OrderRequest.CustomerNote` property and constructor parameter | Advisory retained | The nullable value belongs to the HTTP transport contract. `OrderEndpoint.ToSubmission` owns the single conversion into the non-null core representation. |

All three findings are advisory under the declared shell boundaries. They are
not described as fixed or as core-clean evidence. The required core rules
completed with no admitted blocking finding.

The Domain project suppresses compiler design guideline `CA1716` for the
preapproved public name `Option<T>` and `CA1034` for its intentionally nested,
closed cases. Neither suppression is a CSharpAssay diagnostic suppression.

## Independent coverage gaps

Disposable negative tests established three properties that Shape v0.1 does
not enforce:

- a direct `DateTime.Now` read in Domain is not detected;
- a nonexistent path added to `boundaries.coreProjects` is omitted without
  preventing `authoritative:true`;
- a `JsonPropertyName` serialization concern on a Domain value is not detected.

These are limitations, not accepted design practices. Human review owns them.
The complete commands and results are preserved under `evaluation/`.

- `IOrderStore` is an honest one-method Application port. If `CSAF0001`
  advises a delegate, the interface may remain preferable for DI ownership,
  discoverability, cancellation semantics, and later adapter evolution.
- Nullable API input and transport collections are shell concerns. They must
  convert once and must not be presented as clean core evidence.
- In-memory storage proves effect count only. It is not persistence,
  transaction, concurrency, or round-trip evidence.
- Switch exhaustiveness over ordinary C# class hierarchies is reviewed and
  tested; CSharpAssay 0.1.2 does not make it a compiler proof.
