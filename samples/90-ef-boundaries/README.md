# EF boundary specimen

This bounded .NET 11 specimen uses EF Core SQLite **11.0.0-rc.2.26504.105**.
The immutable Domain has native outcomes and decisions, with no EF or
Infrastructure reference, tracked entity or IQueryable API. Infrastructure owns
the real SQLite context, tracked mutable rows, query, transaction and mapping.
The nullable `OrderRow.Memo` column intentionally remains visible CSAN0004 shell
advice; Domain represents absence with `NoMemo | MemoText`.

Eight tests execute SQLite persistence, native missing/invalid outcomes,
mapping and a Domain dependency/API architecture check. This does not qualify
other database providers, concurrency, production operations, NativeAOT or
trimming. Runtime RC2 support does not make native union preview syntax stable.

After preparing exact paired engine/toolkit packages and committing source:

```sh
./eng/run-all-samples.sh
./eng/tooling qualify-ef-boundary --output evidence/generated/ef-boundary/review-1
```

The qualification retains a passing baseline with advisory findings, an isolated
blocking core counterexample, a valid actual-fingerprint grant, expired and
mismatched grant failures, and completed mapping/persistence test failures under
the same shell declaration. Required test minima and original tests stay intact.
Core/grant identity discovery uses provisional `check`; it cannot authorize a
release. Passing baseline and valid-grant cases use complete `verify`.

`documented-real-grant.json` contains the generated, executable example alongside
its actual finding identity and receipt. The source path, rule and message are
part of that identity: review and regenerate when they change. An owner string
is a policy declaration, not authenticated human approval. No blanket ignore
attribute is added.
