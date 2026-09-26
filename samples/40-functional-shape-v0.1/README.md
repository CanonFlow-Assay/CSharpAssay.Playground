# Functional C# Shape v0.1 reference

This hand-authored sample is the executable companion to the normative
Functional C# Profile — Shape v0.1 contract in CSharpAssay. It is not a
`dotnet new` template and does not claim a new executable CSharpAssay profile.

The workflow accepts or rejects an order submission:

```text
API transport -> Domain decision -> Application orchestration
              -> Application-owned port -> Infrastructure effect
```

Domain and Application are the reviewed core. API and Infrastructure are the
imperative shell. The core projects privately consume the locally built
`CsAssay.Analyzers` 0.2.0-rc.1 package; the evidence script freshly installs
`CsAssay.Tool` 0.2.0-rc.1 from the local feed. There are no project references to the
CSharpAssay source repository.

## Reproduce

Use the pinned .NET 11 RC1 SDK. From the Playground root:

```text
./eng/prepare-assay.sh ../CSharpAssay
dotnet restore samples/40-functional-shape-v0.1/Shape.slnx --locked-mode
dotnet build samples/40-functional-shape-v0.1/Shape.slnx \
  --no-restore --configuration Release
dotnet test samples/40-functional-shape-v0.1/Shape.slnx \
  --no-build --no-restore --configuration Release
./eng/run-functional-shape.sh
```

The last command runs CSharpAssay twice and rejects missing projects, compiler
errors, missing tests/evidence, tool failures, incomplete admitted rules,
unexpected finding identities, stale source/policy/project/expectation inputs,
or non-identical JSON/SARIF.

## Evidence boundary

The sample proves only the checked order behavior, dependency graph, closed
Result/Option shape, direct analyzer scope, local package identity, and
configured CSharpAssay evidence. Architecture tests—not a new analyzer—own the
dependency rules. Advisory findings remain visible and require adjudication.

The historical 0.1.2 builder, independent red-team, and non-authoritative judge record
is in [`evaluation/FINAL-REPORT.md`](evaluation/FINAL-REPORT.md).

It does not prove EF, messaging, serialization round-trip, concurrency,
performance, security, universal purity, business correctness, template
installation, or organization-wide suitability. An LLM judgment is advisory
and cannot override deterministic evidence.
