# Local benchmark record

Recorded 30 October 2026 after the eight contract probes completed with no
adapter disagreement. This is a reproducibility note for one strict
feature-flag projection; it is not a general Json.NET, Newtonsoft.Json, or
System.Text.Json benchmark.

## Environment and method

- SDK/runtime: .NET `11.0.0-rc.2.26504.105`; Linux x64, Ubuntu 24.04.5 LTS.
- Oracle package: `Newtonsoft.Json` 13.0.4. The separate local source reference
  is commit `52fa3aef`; see [upstream.json](upstream.json).
- Command: `DOTNET_TieredCompilation=0 dotnet run --project samples/110-newtonsoft-functional --no-build --no-restore -c Release -- --benchmark`.
- Inputs: generated valid feature-flag documents; generation happens before
  timing. Each path parses, maps to the same immutable domain decision, and
  appends canonical output to the same SHA-256 sink.
- Runs: two warm-ups and five alternating measured repetitions per mode/count.
  The reported time is the median of only the five measured repetitions.
- Validity control: every measured digest matched the functional preflight
  digest. A mismatch exits 2 and invalidates the run.

| Documents | Path | Median elapsed ms | Process-wide allocation bytes | Output digest |
| ---: | --- | ---: | ---: | --- |
| 10,000 | `NewtonsoftProjection` (`JObject`) | 85.1490 | 59,819,752 | `939A255F74EAE244CBD21FBD0553AC84B7087CCFD56D1E33842A3E6182E19034` |
| 10,000 | `FunctionalProjection` (`JsonDocument` + immutable decision) | 51.9428 | 18,242,128 | `939A255F74EAE244CBD21FBD0553AC84B7087CCFD56D1E33842A3E6182E19034` |
| 100,000 | `NewtonsoftProjection` (`JObject`) | 761.3037 | 598,178,440 | `78BCFE1A150C210F317D8EB9930208064E0BD67BA95B2EEFEE1474DCEBAA8A18` |
| 100,000 | `FunctionalProjection` (`JsonDocument` + immutable decision) | 559.1983 | 182,393,464 | `78BCFE1A150C210F317D8EB9930208064E0BD67BA95B2EEFEE1474DCEBAA8A18` |

## What this does and does not show

The observed numbers describe this host, this runtime, this generated input,
and this intentionally narrow contract. They include parsing, adapter mapping,
the shared pure decision, and a common digest sink. They do not compare the
libraries' feature sets, custom converters, streaming, object materialization,
date/reference behavior, error compatibility, security posture, retained
memory, peak memory, or production throughput.

The functional path uses `JsonDocument`; the lower measurement therefore cannot
be attributed solely to immutability or to a functional style. The real result
is behavioral: for the eight declared probes, either adapter produced the same
explicit acceptance/rejection outcome. Re-run with representative fixtures and
an independently chosen contract before making a library or architecture choice.
