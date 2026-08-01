# Gang of Four functional-C# crosswalk

This bounded sample compares five honest, conventional object-oriented patterns
with smaller functional-first C# representations of the same business outcomes.
It demonstrates review guidance, not automatic conversion and not a claim that
either representation is universally superior.

The sample consumes the published `CsAssay.Tool` and `CsAssay.Analyzers` 0.1.1
NuGet packages. It has no project reference to the CSharpAssay repository.
The local tool manifest, package lock files, and machine assertions pin that
baseline.

## Crosswalk

| Pattern | Classic representation | Functional-first representation | Classic finding | When classic remains preferable |
| --- | --- | --- | --- | --- |
| Strategy | One-method `IDiscountStrategy` and an implementation | `Func<decimal, decimal>` passed to checkout | `CSAF0001` ×1 | Named strategies improve DI registration, discovery, versioning, and per-strategy dependencies. |
| Builder | Stateful fluent `MealOrderBuilder` | Immutable draft plus `with`-style transformation functions | `CSAF0001` ×1 | A builder can enforce long construction protocols, hide framework-specific setup, and offer a familiar fluent API. |
| Visitor | Double-dispatch `IShippingQuoteVisitor` over shipment objects | Closed shipment data plus pattern matching | `CSAF0001` ×1 | Visitor is useful when the object hierarchy is stable, new operations arrive often, or framework contracts require double dispatch. |
| State | Two visible flags and guarded transitions | One closed `ApprovalState` value and pure transitions | `CSAD0002` ×1 | Stateful objects fit long-lived identity, lifecycle hooks, observable transitions, and transitions coupled to effects. |
| Composite | Mutable `MenuGroup.Children` tree | Recursive records with `ImmutableArray<MenuNode>` and a fold | `CSAI0003` ×1 | Mutable composites fit UI/document trees with identity, incremental edits, parent links, or framework-owned collections. |

Classic findings total `CSAF0001 ×3`, `CSAD0002 ×1`, and `CSAI0003 ×1`.
The refined lane has zero observed findings. The shared tests exercise ten
business cases and compare classic and refined outputs directly.

## Reproduce

Use the repository-pinned .NET 10 SDK:

```text
dotnet tool restore
dotnet restore samples/30-gof-functional-crosswalk/Gof.Crosswalk.slnx --locked-mode
dotnet build samples/30-gof-functional-crosswalk/Gof.Crosswalk.slnx \
  --no-restore --configuration Release
dotnet test samples/30-gof-functional-crosswalk/tests/Gof.Crosswalk.Tests.csproj \
  --no-build --no-restore --configuration Release
./eng/run-gof-crosswalk.sh
```

The last command runs the complete sequence twice and requires byte-for-byte
identical JSON and SARIF. It also rejects changed package versions, unexpected
findings, missing required evidence, tool failures, changed test totals, or an
incomplete admitted rule.

## Evidence boundary

The seven admitted 0.1.1 stable rules are required by both policies. The
prototype/contextual rules still execute and their observed findings are pinned
by `expected-findings.json`, but they are not misrepresented as admitted release
authority. Native-union prototypes are not configured as proof in this compat
sample.

Read [ADJUDICATION.md](ADJUDICATION.md) before drawing a design conclusion.
Generated JSON and SARIF are CI artifacts rather than reviewed source files.

## Limitations

- Ten examples establish only the stated pricing, construction, quote,
  transition, and aggregation outcomes.
- The tests do not prove equivalence for concurrency, allocation, performance,
  serialization, dependency-injection behavior, reflection metadata, or every
  invalid input.
- Zero findings means zero observations from the configured 0.1.1 rules; it is
  not proof of correctness, purity, maintainability, or pattern superiority.
- The examples were selected because the business behavior admits both honest
  representations. They are not a benchmark of all GoF patterns.
- CSharpAssay does not rewrite or automatically convert any implementation.

> CSharpAssay 0.1.1 is a published C# design-assessment and CI enforcement tool
> with reproducible evidence. It identifies selected non-functional design
> risks and guides human-controlled refinement. It is not an automatic
> functional-C# converter or a correctness proof system.
