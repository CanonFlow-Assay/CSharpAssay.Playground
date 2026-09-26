# CSharpAssay Playground

An evidence-driven proving ground for CSharpAssay, now **strictly .NET 11 RC1**. Both repositories pin SDK `11.0.100-rc.1.26425.128`, target `net11.0`, and enable C# preview for native unions. Older targets are rejected.

The playground consumes locally packed `CsAssay.Tool` and `CsAssay.Analyzers` `0.2.0-rc.1`, preserving package-consumer testing without publication or source project references to CSharpAssay.

## Local gate

With the migrated CSharpAssay repository beside this folder:

```sh
./eng/prepare-assay.sh ../CSharpAssay
dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore -c Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj --no-build --no-restore -c Release
./eng/run-assay.sh ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll
./eng/run-gof-crosswalk.sh
./eng/run-functional-shape.sh
```

The preparation script populates ignored `.packages/`, uses a checkout-local package cache, restores the pinned tool, and refreshes lock files for the selected source revision. Subsequent restores use locked mode. All Assay commands use `native`.

## Samples

- `00-rule-matrix`: controlled positive and negative policy examples.
- `10-gilded-rose`: pinned upstream source, .NET 11 harness, characterization tests, and immutable derivative.
- `30-gof-functional-crosswalk`: behavior-equivalent functional alternatives; shipment and recursive menu use native C# unions.
- `40-functional-shape-v0.1`: domain/application/shell boundaries, behavior and architecture tests, and locally packaged .NET 11 evidence.
- `20-eshop-agent-assay`: historical external .NET 10 case study, excluded from the current gate. The separate eShop repository is outside this migration.

Impure samples must produce their expected findings. Only complete `verify` evidence on refined projects is release authority. Historical evidence retains its original SDK and package identity.

CI selects the paired CSharpAssay revision using repository variable `CSHARP_ASSAY_REF` or workflow input `assay_ref`; default: `main`. That revision must contain the .NET 11 migration.

See the [migration report](docs/net11-migration.md).
