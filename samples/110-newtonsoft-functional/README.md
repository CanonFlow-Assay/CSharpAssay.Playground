# Newtonsoft.Json functional boundary comparison

This local sample asks one narrow question: can two JSON adapters produce the
same strict, immutable feature-flag decision when their parsing libraries and
intermediate representations differ?

It is **not** a rewrite of Newtonsoft.Json. It does not copy or modify upstream
source, compare full public APIs, establish compatibility, assess security, or
claim that either serializer is generally faster. The local reference source is
pinned in [upstream.json](upstream.json) at `52fa3aef`; it is MIT licensed. The
runtime oracle is the separately pinned `Newtonsoft.Json` 13.0.4 package.

## The conversion

Both adapters parse one strict feature-flag document. `NewtonsoftProjection`
uses `JObject` in the framework-facing shell. `FunctionalProjection` uses
`JsonDocument` only to build immutable raw data. Both call the same pure policy
function, which returns a native union: `ProjectionAccepted` or
`ProjectionRejected`.

The contract rejects malformed JSON, non-object roots, duplicates, unknown
properties, non-string keys, non-Boolean `enabled`, non-integral/out-of-range
rollouts, and non-string audiences. There are no defaults. Equal behavior is a
precondition for a timing, not a result inferred from a similar-looking type.

The local CSharpAssay `check` run reports zero errors and warnings, plus two
visible `CSAF0002` advisory notes for small, intentional builder loops. The
native-union validators are exhaustive: a future `JsonField` case must be
classified explicitly. `check` is analysis only; this standalone sample does
not claim an authoritative `verify` receipt or general JSON-library assurance.

## Run locally

```bash
dotnet restore samples/110-newtonsoft-functional/NewtonsoftFunctional.csproj
dotnet build samples/110-newtonsoft-functional/NewtonsoftFunctional.csproj --no-restore -c Release
dotnet run --project samples/110-newtonsoft-functional --no-build --no-restore -c Release -- --verify
DOTNET_TieredCompilation=0 dotnet run --project samples/110-newtonsoft-functional --no-build --no-restore -c Release -- --benchmark
```

`--verify` stops on the first adapter disagreement. `--benchmark` warms both
paths, alternates order, verifies an SHA-256 digest of every output, and emits
raw JSON samples. Redirect the output to an ignored file if it is worth keeping:

```bash
mkdir -p evidence/generated/newtonsoft-functional
DOTNET_TieredCompilation=0 dotnet run --project samples/110-newtonsoft-functional --no-build --no-restore -c Release -- --benchmark > evidence/generated/newtonsoft-functional/benchmark.json
```

Timings are machine/runtime-specific. Allocation measurements are process-wide
managed allocation deltas, not retained or peak memory.

The checked-in [local benchmark record](BENCHMARKS.md) is one completed run on
the maintainer host. It is useful for reproducing this narrow experiment, not a
performance claim about either JSON library.

## Questions before expanding the idea

1. Is reject-unknown the right feature-flag contract, or is forward-compatible
   input more important?
2. Should duplicate properties be rejected, last-wins, or recorded for audit?
3. Does the functional boundary belong in the domain decision, the JSON adapter,
   or both?
4. Are the paths equivalent for your number, date, reference, and error cases
   before a benchmark matters?
5. Would a source-generated `System.Text.Json` contract be a more useful second
   comparator for the production workload? See the separate, strict
   [`160-source-generated-json`](../160-source-generated-json/README.md) boundary
   for one deliberately non-benchmarking answer.
