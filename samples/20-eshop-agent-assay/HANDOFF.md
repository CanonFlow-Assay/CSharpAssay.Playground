# Agent handoff

## Objective

Produce a reproducible, evidence-backed plan for improving the functional
integrity of eShop `Ordering.Domain` without changing externally observable
behavior or inventing framework guarantees.

## Fixed inputs

- upstream: `dotnet/eShop`;
- commit: `9b4f9434f46fdc5c1a6e9e936af2868340cdbc48`;
- scope: `src/Ordering.Domain/Ordering.Domain.csproj`;
- policy: this directory's `.csassay.json`;
- CSharpAssay: version `0.1.1`, exact source commit
  `f5fb8e7dd27da20f6d5c26306dc0e56823e37962`;
- first pass: observation only.

## Required sequence

1. Prove the exact upstream commit and a clean tracked worktree.
2. Restore and build `Ordering.Domain` without CSharpAssay changes.
3. Run the focused provisional assay and retain JSON/SARIF.
4. Group diagnostics by underlying domain concept; do not count duplicated
   property/accessor/constructor evidence as independent design problems.
5. Run a nullable-enabled compiler probe without committing the property.
6. Add `Ordering.UnitTests` and the smallest required infrastructure mapping
   tests before proposing payment-state changes.
7. Create a patch preview only. A human must approve source mutation.
8. Rebuild, run behavior tests, assay the diff, and report every remaining gap.

## Mutation prohibitions

- do not edit generated code;
- do not add `NoWarn`, `.editorconfig` severity `none`, or unfingerprinted
  suppressions;
- do not replace nullable payment identifiers mechanically with zero/default;
- do not assume EF serialization or hydration behavior;
- do not rewrite unrelated projects;
- do not call provisional evidence authoritative.

## Acceptance gates

- upstream behavior tests pass before and after;
- `Ordering.Domain` builds warning-clean under its declared repository policy;
- CSharpAssay has zero missing evidence and zero tool failures;
- every changed finding maps to a reviewed source diff and behavior test;
- EF mapping for any changed state representation is executed, not inferred;
- JSON/SARIF and a human adjudication ledger are retained;
- unresolved questions remain explicit.

## Fork decision

Do not create a long-lived fork for baseline observation. Once a reviewed patch
passes the gates, create `CanonFlow-Assay/eShop` as an attributed GitHub fork and
push only branch `csassay/ordering-domain-purity`. Add a prominent experimental
assay notice and preserve the upstream MIT license. Never replace fork `main`.
