# Shape v0.1 independent red-team evaluation

Evaluation date: 2026-08-02  
Accepted reference commit: `948531bc234c03dfc30087c7eacac81f00c89017`  
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

## Independent judgment

The Shape reference is honest when read together with this evaluation: its
deterministic authority invariants work, and the tested stale-source paths are
now rejected. Direct hidden-effect enforcement is not present. An LLM may use
the evidence to reject incomplete runs, but it must not infer universal Domain
purity from an authoritative Pass.
