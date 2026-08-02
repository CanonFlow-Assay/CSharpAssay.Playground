# Shape v0.1 independent red-team evaluation

Evaluation date: 2026-08-02  
Accepted reference commit: `948531bc234c03dfc30087c7eacac81f00c89017`  
Contract-completion follow-up commit: `519aa46eec84d7d227a4f4b1c09e109f87eb194d`
Normative contract SHA-256: `54be4be99328e9f56e83358075f0cc112989bbc16e36c46527e74c32ea5771b2`  
Consumer packages: `CsAssay.Tool` 0.1.2 and `CsAssay.Analyzers` 0.1.2  
Toolchain: .NET SDK 10.0.301, runtime 10.0.9, MSBuild 10.0.301, Roslyn 5.6.0.0

## Verdict

The authority, required-test, analyzer anti-bypass, and dependency-graph gates
rejected the tested mutations honestly. No absolute blocker was found.

Two findings are material and must remain visible:

1. A direct `DateTime.Now` read in Domain is not detected by CSharpAssay 0.1.2
   or the six architecture tests. The mutated solution still returns an
   authoritative Pass. This is a known human-review boundary, not proof of
   Domain purity.
2. The initial `eng/assert-functional-shape.py` validated the contents of
   existing evidence but did not compare its recorded source hashes with the
   current source tree. It returned 0 after a source mutation. A narrow,
   branch fix now rejects both a changed source and a new source;
   the original false-pass remains preserved below.

The contract explicitly permits a mutation to be retained as a recorded gap.
The hidden-effect result is not being converted into a false product claim.
The source-binding correction remains subject to normal human review.

Two follow-up probes also produced explicit coverage gaps. An absent path added
to `boundaries.coreProjects` is not treated as required project inventory by
CSharpAssay 0.1.2, and a `System.Text.Json` transport attribute in Domain is not
detected by the current architecture tests or analyzer rules. Both mutations
returned authoritative Passes and must not be represented as enforced
properties of Shape v0.1.

## Environment setup observations

The first two baseline invocations were environment failures, not CSharpAssay
verdicts:

| Command | Exit | Classification |
| --- | ---: | --- |
| `./eng/run-functional-shape.sh` | 127 | `dotnet` absent from `PATH`; no build or tool execution |
| `PATH=/root/.dotnet:$PATH ./eng/run-functional-shape.sh` | 131 | restore/build and 16 tests passed, then the installed apphost could not locate the runtime because `DOTNET_ROOT` was unset |

The bounded corrected command was:

```bash
PATH=/root/.dotnet:$PATH \
DOTNET_ROOT=/root/.dotnet \
./eng/run-functional-shape.sh
```

It exited 0: build succeeded with zero warnings/errors, all 16 tests passed,
and both verification runs were authoritative Passes. The accepted three
findings were advisory shell/boundary observations: `CSAN0003` once and
`CSAN0004` twice.

Baseline deterministic artifacts:

| Artifact | SHA-256 |
| --- | --- |
| `verify.json` | `74198722054cc9c6ff197cbfd99e37ee28205c0ce82d99f8a11d1cb14e990e7a` |
| `verify.sarif` | `dad047c97d217562bbbe38b9d6961a1bfbc142ef99083ff2456a58405ab5a44f` |

## Disposable mutation results

Each mutation was made in a fresh copy under a `mktemp` directory. No mutation
was retained in the accepted sample.

### Required test executes zero tests

The ten behavior-test methods were excluded with `#if false`; the mutated
solution was restored locked and built successfully before verification.

```bash
cs-assay verify samples/40-functional-shape-v0.1/Shape.slnx \
  --policy samples/40-functional-shape-v0.1/.csassay.json \
  --profile compat --json artifacts/verify.json \
  --sarif artifacts/verify.sarif --html artifacts/verify.html
```

Result: exit 3, `toolFailure`, `authoritative:false`. Architecture tests passed
6/6; behavior tests were `notRun` with zero total and exit 8. Evidence contained
`CSASSAY-REQUIRED-TESTS-NOT-RUN` and
`CSASSAY-TEST-RUN-FAILED`. This is an expected rejection, not an unexpected
tool failure.

Hashes: JSON
`15c816e1160ed50661a4a19034413ac590aeef8a18aa19bfcb6a7e5b0aad38ab`;
SARIF `66f2bbd723740c61db6736ae81be5557a14cd97563fee386ed67cbe5fbc7600d`.

### Missing generated member / compiler incompleteness

The disposable Domain copy referenced nonexistent `GeneratedMembers.NotPresent`
and was verified without rebuilding its previously healthy binaries.

Result: exit 2, `inconclusive`, `authoritative:false`. `Shape.Domain` remained
loaded but contained compiler error `CS0103`; missing evidence included
`CSASSAY-COMPILER-ERRORS`, `CSASSAY-REQUIRED-RULE-INCOMPLETE`, and
`CSASSAY-REQUIRED-TESTS-NOT-RUN`. Every one of the seven required rules reported
`incomplete`, never `completed`. There were no tool failures.

Hashes: JSON
`ea06ad08f01eda46ca5f35d1a6f0735181f2763a8e72f6ad74ea1ebb316d2624`;
SARIF `2c6380a100a90e7f263eb394d68c68e3aa479a597746a66f9c46015affdc597e`.

### Missing declared core project

This follow-up is deliberately distinct from the loaded-project compiler error
above. In a fresh archive of `519aa46e`, only `.csassay.json` was changed by
appending this nonexistent entry:

```json
"boundaries": {
  "coreProjects": [
    "src/Shape.Domain/Shape.Domain.csproj",
    "src/Shape.Application/Shape.Application.csproj",
    "src/Shape.Absent/Shape.Absent.csproj"
  ]
}
```

Published 0.1.2 verification exited 0 with `verdict:pass` and
`authoritative:true`. Its project inventory contained only the six real
projects, all `loaded:true`; the absent path had no project entry. It reported
16 passing tests, no missing evidence, no tool failure, and all seven required
rules as `completed`.

This is a coverage gap: `boundaries.coreProjects` scopes findings but is not
validated as an exhaustive required-project inventory. A consumer cannot infer
that every path named there loaded merely from `authoritative:true`.

The mutated policy SHA-256 was
`3233ecc4dd0b010a039a8943f42b38a3cc19e06b9292a67e96b69e0340c45d19`.
Artifact hashes: JSON
`637ee7df6c81e5c99d3c4543742b5a6f919642cf3488786f8eedf5354f089b96`;
SARIF `2dcf9fdad0cdfbc5a70027604fbb5cffde0356b2a09bbab893aa6001cad315d0`.

### Forbidden Domain project reference

```bash
dotnet new classlib -n Forbidden.Shell \
  -o samples/40-functional-shape-v0.1/src/Forbidden.Shell \
  --framework net10.0 --no-restore
dotnet add samples/40-functional-shape-v0.1/src/Shape.Domain/Shape.Domain.csproj \
  reference samples/40-functional-shape-v0.1/src/Forbidden.Shell/Forbidden.Shell.csproj
dotnet test \
  samples/40-functional-shape-v0.1/tests/Shape.Architecture.Tests/Shape.Architecture.Tests.csproj \
  --configuration Release
```

The test command exited 2. Five architecture tests passed and
`Project_reference_graph_matches_the_contract` failed. This is the intended
architecture rejection.

### Analyzer bypass and reviewed rollback

Each command targeted `src/Shape.Domain/Shape.Domain.csproj` with Release and
`--no-restore`:

| Additional property | Exit | Evidence classification |
| --- | ---: | --- |
| `-p:RunAnalyzers=false` | 1 | `CSASSAY-BUILD-GATE-DISABLED`; names `RunAnalyzers=false` |
| `-p:RunAnalyzersDuringBuild=false` | 1 | `CSASSAY-BUILD-GATE-DISABLED`; names `RunAnalyzersDuringBuild=false` |
| `-p:NoWarn=CSAN0003` | 1 | `CSASSAY-BUILD-GATE-SUPPRESSED`; directs consumer to reviewed fingerprinted suppression |
| `-p:WarningsNotAsErrors=CSAN0003` | 1 | `CSASSAY-BUILD-GATE-SUPPRESSED`; names the demotion |
| `-p:CsAssayEnforceOnBuild=false` | 0 | Reviewed rollback worked; this is enforcement-disabled evidence, not a gate Pass |

### Stale evidence

After adding the hidden-clock source mutation, the unchanged baseline evidence
and assertion script were copied into the disposable tree:

```bash
python3 eng/assert-functional-shape.py
```

It exited 0 and printed `Shape v0.1 evidence ok`. The evidence recorded
`src/Shape.Domain/Orders.cs` as
`161a2ce67d4994f10600e02fb85509ff968b437b83c62e560194f2fcb43c5dbd`,
while the mutated file was
`d9093b75ad87c641ef5ff00f13dc8ecb780a29e0b98745ef14835b964e6bad60`.
Therefore stale evidence was manually detectable, but the accepted assertion
was not source-bound. This is the originally discovered false-pass.

#### Candidate source-binding fix retest

A narrow branch change to `eng/assert-functional-shape.py` constructs the
current inventory of non-`bin`/`obj` `src/**/*.cs` and `tests/**/*.cs` files and
requires exact path and SHA-256 equality with `evidence.sources`.

The independent retest used a fresh `git archive` of accepted commit
`948531bc234c03dfc30087c7eacac81f00c89017`, overlaid only that changed script
and the accepted baseline evidence, and produced:

| Case | Assertion exit | Output |
| --- | ---: | --- |
| Unmodified accepted source and evidence | 0 | `Shape v0.1 evidence ok (CSAN0003=1, CSAN0004=2)` |
| Existing `src/Shape.Domain/Orders.cs` modified after evidence | 1 | `Shape v0.1 evidence assertion failed: evidence source inventory or hash is stale` |
| New `src/Shape.Domain/NewUnrecordedSource.cs` added after evidence | 1 | `Shape v0.1 evidence assertion failed: evidence source inventory or hash is stale` |

This candidate closes both tested stale-source paths. It does not change or
claim to close the separate `DateTime.Now` detection gap.

### Hidden Domain effect

The disposable Domain copy added:

```csharp
public static class HiddenClockProbe
{
    public static DateTime ReadUtc() => DateTime.Now;
}
```

Build exited 0, architecture tests passed 6/6, and CSharpAssay verification
exited 0 with `verdict:pass`, `authoritative:true`, 16/16 tests, the unchanged
three accepted findings, no missing evidence, and no tool failures. No finding
identified `HiddenClockProbe`.

Hashes: JSON
`bb888c5cb79f39728b988f1dabb1c39467dca51968d97356ca75e3ce04bbdd2d`;
SARIF `dad047c97d217562bbbe38b9d6961a1bfbc142ef99083ff2456a58405ab5a44f`.
The SARIF is identical to baseline because the mutation created no finding;
the JSON changed because its source inventory records source hashes.

### Direct Domain serialization concern

In a separate fresh archive of `519aa46e`, the disposable Domain `OrderId`
record received a genuine BCL serialization concern without adding a package:

```csharp
public readonly record struct OrderId(
    [property: System.Text.Json.Serialization.JsonPropertyName("order_id")]
    Guid Value);
```

Locked restore, Release build, and the six architecture tests all exited 0.
Published 0.1.2 verification also exited 0 with `verdict:pass`,
`authoritative:true`, 16 passing tests, the unchanged three findings, no missing
evidence, no tool failure, and all seven required rules `completed`. No finding
identified the attribute or a serialization/transport leak.

This is an explicit architecture-test and analyzer coverage gap, consistent
with the contract's statement that serialization is not qualified. Shape v0.1
still requires human review for direct Domain serialization concerns.

Artifact hashes: JSON
`0561f8adae3975553c41904fe8d933a6778a2ae0c1a6205eb2a04ed79eca74ec`;
SARIF `dad047c97d217562bbbe38b9d6961a1bfbc142ef99083ff2456a58405ab5a44f`.
The unchanged SARIF demonstrates that the mutation produced no finding; the
JSON changed because it records the mutated source hash.

## Independent judgment

The Shape reference is honest when read together with this evaluation: its
deterministic authority invariants work for loaded-project compiler errors and
required tests, and the tested stale-source paths are now rejected. Direct
hidden-effect and serialization-boundary enforcement are not present, and
extra nonexistent `coreProjects` entries are not validated as required
inventory. An LLM may use the evidence to reject incomplete runs, but it must
not infer universal Domain purity or complete boundary-path loading from an
authoritative Pass.
