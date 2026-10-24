# CSharpAssay Playground

CSharpAssay helps developers and coding agents follow a chosen functional C# policy with verifiable rules and optional decision assistance. This Playground provides runnable examples and evidence, now **strictly .NET 11 RC2**. Both repositories pin SDK `11.0.100-rc.2.26504.105`, target `net11.0`, and enable C# preview for native unions. Older targets are rejected. SDK go-live support and preview-language capability are qualified separately from the boundary/output overhaul.

The playground consumes locally packed `CsAssay.Tool` and `CsAssay.Analyzers` `0.2.0-rc.1`, preserving package-consumer testing without publication or source project references to CSharpAssay.

The [application workflow preview](docs/application-workflow.md) adds reusable
assessment and typed Decision Assist APIs, bounded Mutation/CodeRisk/Contract/
Resilience checks, a real Polly consumer, and standalone application scaffolding.
The [inventory reservation application](samples/80-inventory-reservation/README.md)
adds a second domain with real HTTP and PostgreSQL, declared worker contracts,
named acceptance criteria and registered investigations.
The [maturity exercise report](docs/maturity-results.md) records qualification,
preview boundaries, retained failures and remaining evidence.
Run `./eng/run-application-workflow.sh` after package preparation to qualify a new
app and a tested requirement change. `ReadyForReview` does not grant release approval.

## Try Decision Assist

With the pinned SDK, Bash, and the matching CSharpAssay source checkout:

```sh
./eng/try-decision-assistance.sh ../CSharpAssay
```

This prepares local packages and runs the offline demonstration and synthetic routing scenarios. NuGet restore may need internet access; no model API key is needed and no provider is called. Read the [getting-started guide](docs/decision-assist-getting-started.md) for agent instructions and explicit Jev/OpenAI comparisons. Model advice stays separate from verification, and incomplete or insufficient context is reviewed locally. The scenario labels are draft policy expectations, not a qualified benchmark.

## Local gate

`./eng/run-all-samples.sh` evaluates and builds the complete 34-project inventory,
including all 30 runnable sample projects, then executes every test project.
Docker is required for the inventory tests; the gate starts an isolated, pinned
PostgreSQL container and removes it afterward.
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
- [`80-inventory-reservation`](samples/80-inventory-reservation/README.md): immutable native decisions, HTTP hosting, transactional PostgreSQL stock/reservation/outbox writes, concurrency and idempotency tests, a registered requirement change and an actual wire-contract regression investigation.
- `20-eshop-agent-assay`: historical external .NET 10 case study, excluded from the current gate. The separate eShop repository is outside this migration.

Impure samples must produce their expected findings. Only complete `verify` evidence on refined projects is release authority. Historical evidence retains its original SDK and package identity.

The preserved impure matrix and upstream Gilded Rose harness deliberately disable
nullable analysis. Their provisional checks now retain active CSAP integrity
findings, explicit compiler-evidence gaps and exit 1. `eng/run-assay.sh` requires
those exact negative outcomes and rejects unrelated execution failures. A shell
declaration or grant cannot turn those fixtures into complete verification.

Reviewed CSV observations retain exact comparison of the original finding fields.
The consumer validates additive scope/identity context separately, so new metadata
does not require refreshing the reviewed findings or discarding their identities.

CI defaults to the paired engine assessment commit `238b56a99b1d7be92e9523262bd55cb9f4b26202`. Repository variable `CSHARP_ASSAY_REF` or workflow input `assay_ref` can select another reviewed revision containing .NET 11 and the `assess` API.

Follow the [local Forgejo review workflow](docs/local-review-workflow.md): local tests, commits and PRs precede consolidated external publication.

See the [migration report](docs/net11-migration.md).

Run `./eng/tooling run-decision-assistance` after the Release build to reproduce the offline decision-assistance experiment. CI runs this slice without credentials or model requests.

The workflow and evidence harnesses are C# applications under
`eng/Assay.Tooling`; Python is no longer a prerequisite. `eng/tooling` and
`eng/daa` are small Bash launchers for the same .NET executable. Run the DLL
with `dotnet` directly when Bash is unavailable. The pinned SDK, Docker for the
real inventory database, and a matching engine remain required. Host evidence
is Linux-scoped. The optional Python SetFit bridge has been retired; external
provider protocols remain advisory and do not grant verification authority.

The [22 October agent self-adjudication](docs/self-adjudication-2026-10-22.md)
records corrected evidence gaps, regression cases and the fresh qualification
contract. Its completion receipts distinguish executed checks from review work
that remains open.
