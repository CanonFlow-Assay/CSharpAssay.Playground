# CSharpAssay Playground

CSharpAssay helps developers and coding agents follow a chosen functional C# policy with verifiable rules and optional decision assistance. This Playground provides runnable examples and evidence, now **strictly .NET 11 RC1**. Both repositories pin SDK `11.0.100-rc.1.26425.128`, target `net11.0`, and enable C# preview for native unions. Older targets are rejected.

The playground consumes locally packed `CsAssay.Tool` and `CsAssay.Analyzers` `0.2.0-rc.1`, preserving package-consumer testing without publication or source project references to CSharpAssay.

The [application workflow preview](docs/application-workflow.md) adds reusable
assessment and typed Decision Assist APIs, bounded Mutation/CodeRisk/Contract/
Resilience checks, a real Polly consumer, and standalone application scaffolding.
Run `./eng/run-application-workflow.sh` after package preparation to qualify a new
app and a tested requirement change. `ReadyForReview` does not grant release approval.

## Try Decision Assist

With the pinned SDK, Python 3, Bash, and the matching CSharpAssay source checkout:

```sh
./eng/try-decision-assistance.sh ../CSharpAssay
```

This prepares local packages and runs the offline demonstration and synthetic routing scenarios. NuGet restore may need internet access; no model API key is needed and no provider is called. Read the [getting-started guide](docs/decision-assist-getting-started.md) for agent instructions and explicit Jev/OpenAI comparisons. Model advice stays separate from verification, and incomplete or insufficient context is reviewed locally. The scenario labels are draft policy expectations, not a qualified benchmark.

## Local gate

`./eng/run-all-samples.sh` evaluates and builds the complete 29-project inventory,
including all 25 runnable sample projects, then executes every test project.
The build rejects older or mixed framework targets, another SDK, non-preview C#,
and disabled nullable/warnings-as-errors settings in positive samples. The three
reviewed negative/upstream fixture exceptions are listed in
`workflows/sample-toolchain.json`; their expected violations remain visible in
the separate evidence gates. Historical external eShop evidence contains no
current project and is explicitly archived.

With the migrated CSharpAssay repository beside this folder:

```sh
./eng/prepare-assay.sh ../CSharpAssay
dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore -c Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj --no-build --no-restore -c Release
./eng/run-assay.sh ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll
./eng/run-gof-crosswalk.sh
./eng/run-functional-shape.sh
./eng/run-csvhelper.sh
```

The preparation script populates ignored `.packages/`, uses a checkout-local package cache, restores the pinned tool, and refreshes lock files for the selected source revision. Subsequent restores use locked mode. All Assay commands use `native`.

## Samples

- `00-rule-matrix`: controlled positive and negative policy examples.
- `10-gilded-rose`: pinned upstream source, .NET 11 harness, characterization tests, and immutable derivative.
- `30-gof-functional-crosswalk`: behavior-equivalent functional alternatives; shipment and recursive menu use native C# unions.
- `40-functional-shape-v0.1`: domain/application/shell boundaries, 55 behavior and seven architecture tests, measured coverage uplift, 26 before/after mutation cases, Decision Assist packets, and locally packaged .NET 11 evidence.
- `50-decision-assistance`: native union advice, six executed mutation cases with requirements and agent review notes, deterministic investigation feedback, and opt-in provider adapters for OpenAI, Jev, Clef, Laya, local models and trained classifiers. Human case adjudication and model qualification remain pending. See [the slice contract](samples/50-decision-assistance/README.md).
- [`60-csvhelper-functional`](samples/60-csvhelper-functional/README.md): three source-derived typed converter rewrites and real CsvHelper import/export adapters; 118 tests, 24 mutations, independent streaming requirements, hefty physical-file mocks and allocation/throughput comparisons, offline advice and authoritative sample verification. Original upstream failures/unsupported targets remain separate.
- [`70-polly-resilience`](samples/70-polly-resilience/README.md): pinned original Polly packages, native domain outcomes, 16 controlled executions and six state/concurrency/fallback checks, six mutations and the new-app requirement loop. Bounded consumer qualification, with preview modules and survivors kept visible.
- `20-eshop-agent-assay`: historical external .NET 10 case study, excluded from the current gate. The separate eShop repository is outside this migration.

Impure samples must produce their expected findings. Only complete `verify` evidence on refined projects is release authority. Historical evidence retains its original SDK and package identity.

CI selects the paired CSharpAssay revision using repository variable `CSHARP_ASSAY_REF` or workflow input `assay_ref`; default: `main`. That revision must contain the .NET 11 migration.

See the [migration report](docs/net11-migration.md).

Run `python3 eng/run-decision-assistance.py` after the Release build to reproduce the offline decision-assistance experiment. CI runs this slice without credentials or model requests.
