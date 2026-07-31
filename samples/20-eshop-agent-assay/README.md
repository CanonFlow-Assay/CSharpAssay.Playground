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

The local CSharpAssay 0.1.1 candidate rerun reports exactly nine findings:
`CSAN0001` × 1, `CSAN0003` × 2, and `CSAN0004` × 6, with zero missing evidence
and zero tool failures. The five equality false positives are gone. This is
still provisional evidence until the package is published and CI reproduces
`expectations.json` from the final commit.

No production transformation is admitted until Ordering behavior and EF
mapping are executable in the focused harness.

Run `eng/run-eshop-agent-assay.ps1 -EShopRoot C:\path\to\eShop` from the
Playground repository. Read `HANDOFF.md` before authorizing source mutation.
