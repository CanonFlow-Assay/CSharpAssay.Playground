# CSharpAssay 0.1.1 published baseline

Version 0.1.1 is the frozen package baseline for this Playground evidence. New
sample work uses the published packages; it does not rebuild CSharpAssay from a
project reference or expand the rule set.

## Release summary

- Published packages: `CsAssay.Tool` 0.1.1 and `CsAssay.Analyzers` 0.1.1.
- Supported evidence lane here: .NET SDK 10.0.301, `net10.0`, C# 14, and the
  `compat` profile.
- Stable admitted lane: null safety, immutable record carriers, and suppression
  integrity as named by the seven required rules in the policies.
- Prototype/contextual findings are review guidance. Their exact observed
  counts may be pinned as reproducible evidence, but they are not promoted to
  release-blocking proof.
- From this baseline, product changes should be limited to bug fixes and
  false-positive corrections unless a separate roadmap decision is reviewed.

## Quick start

```text
dotnet new tool-manifest
dotnet tool install CsAssay.Tool --version 0.1.1
dotnet tool run cs-assay -- help
dotnet tool run cs-assay -- check MySolution.sln --profile compat
```

For build-time diagnostics:

```xml
<PackageReference Include="CsAssay.Analyzers"
                  Version="0.1.1"
                  PrivateAssets="all" />
```

Use lock files and `dotnet restore --locked-mode` for reproducible CI.

## Known limitations

- 0.1.1 recognizes selected design risks; it does not cover every mutable API,
  effect, state machine, hierarchy, or domain invariant.
- Advisory findings require human context and must not be treated as automatic
  refactoring instructions.
- `cs-assay --help` has a known alias defect in 0.1.1; use `cs-assay help`.
- `cs-assay explain <RULE>` emits a repository-relative documentation path in
  0.1.1. Resolve it under the CSharpAssay repository documentation tree.
- Zero findings is not a correctness, security, performance, or purity proof.
- Native union prototypes require a compatible preview lane and are not used as
  authoritative evidence by this sample.

> CSharpAssay 0.1.1 is a published C# design-assessment and CI enforcement tool
> with reproducible evidence. It identifies selected non-functional design
> risks and guides human-controlled refinement. It is not an automatic
> functional-C# converter or a correctness proof system.
