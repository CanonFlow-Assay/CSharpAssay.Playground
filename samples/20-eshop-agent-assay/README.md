# eShop Ordering.Domain agent assay

This case study assays one bounded domain project from Microsoft's eShop
reference application. It does not call eShop impure, vendor its source, or
claim that functional taste overrides EF Core and DDD persistence constraints.

The upstream checkout is pinned by `provenance.json`. The initial human run on
CSharpAssay 0.1.0 loaded one project completely and reported 14 findings with
zero missing evidence and zero tool failures:

- one real nullable-compiler policy gap;
- eight diagnostics representing two optional payment-identity concepts;
- five false positives where equality code observes null through
  `object.Equals` or `ReferenceEquals` rather than introducing domain state.

The pinned CSharpAssay 0.1.1 release commit rerun reports exactly nine findings:
`CSAN0001` × 1, `CSAN0003` × 2, and `CSAN0004` × 6, with zero missing evidence
and zero tool failures. The five equality false positives are gone. CI rebuilds
the exact CSharpAssay and eShop commits and checks the report against
`expectations.json`. That baseline result remains provisional because the pass
does not execute the required characterization and persistence tests.

Candidate evidence is now separately pinned to
`CanonFlow-Assay/eShop@5e92c725c6b13ff2c9cda6de58228ff04c1fb73f`, based on
upstream `9b4f9434f46fdc5c1a6e9e936af2868340cdbc48`. CI checks out both commits.
For the candidate it restores and builds `Ordering.UnitTests`, retains a TRX
with exactly 50 passed and 0 failed, and requires all four EF runtime-model
characterization tests to pass. It also records a provisional candidate Assay
report with the same nine remaining findings; the persistence-metadata patch
does not claim those findings were fixed.

The candidate proof establishes that six domain `[Required]` attributes are
absent and that the reviewed EF runtime model still reports equivalent
requiredness. Removing the attributes intentionally changes DataAnnotations
reflection metadata. Database round-trip behavior, migration/snapshot
compatibility, alternate DbContexts/model builders, serialization consumers,
and upstream suitability remain outside this proof.

Run `eng/run-eshop-agent-assay.ps1 -EShopRoot C:\path\to\eShop` from the
Playground repository. Read `HANDOFF.md` before authorizing source mutation.
The candidate remains isolated behind a Draft PR in the fork and must not be
merged into fork `main` or submitted upstream as part of this evidence change.
