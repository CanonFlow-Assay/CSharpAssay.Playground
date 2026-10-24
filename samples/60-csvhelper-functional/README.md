# CsvHelper functional consumer

The selected approach keeps CsvHelper's streaming CSV parser and converts a
useful slice into an explicit functional core. Three converter algorithms
(Int32, Decimal, Boolean) are rewritten with typed native-union outcomes,
immutable Boolean aliases, explicit missing input, and supplied culture/styles.
A real import adapter uses CsvHelper for tokenization, snapshots fields, invokes
pure row decisions, and streams immutable accepted/rejected/error events.
A bounded exporter formats the same schema with explicit culture, then delegates
quoting/escaping to CsvHelper and emits typed progress/failure/cancellation events.

This is a local sample and an alternative to a whole-library fork. It does not
replace CsvHelper's general mapping, writing/configuration, dynamic records, or complete
converter surface. The functional API deliberately replaces converter exception
fallbacks/default-value mapping with explicit problems. Numeric Boolean parsing
uses the supplied culture; the original source uses ambient culture in that
path. No performance or state-of-the-art superiority has been established.

## Provenance and boundaries

[upstream.json](upstream.json) pins source commit
[`33970e5183383bdac1fbce3b3fbcdf46b318ca52`](https://github.com/JoshClose/CsvHelper/tree/33970e5183383bdac1fbce3b3fbcdf46b318ca52)
and hashes the three source files. [FieldConversions.cs](src/CsvFunctional.Domain/FieldConversions.cs)
retains attribution and a modification notice; [LICENSE.CsvHelper.txt](LICENSE.CsvHelper.txt)
preserves the upstream dual license. This derivative selects Apache-2.0.
The integration/differential oracle is separately pinned to NuGet CsvHelper
**33.1.0**, including lock-file content hashes. It is not claimed byte-identical
to the source commit. No upstream contribution or endorsement is implied.

- **Domain:** closed conversion/import/export cases, immutable records/arrays,
  accumulated conversion errors, no CsvHelper dependency or IO. Functions use
  explicit culture arguments; callers must keep those stable during a call.
- **Adapter:** CsvReader/CsvWriter and resource ownership, cancellation forwarding,
  immutable field snapshots, culture snapshot at construction, limits and
  exception-to-outcome conversion. The caller owns the TextReader/TextWriter.
- **CLI:** files, Ctrl+C, explicit JSON transport projections and exit codes.
- **Tests:** differential comparisons with the published converters, explicit
  semantic differences, CSV syntax, resource/cancellation/error/limit cases,
  and an assembly-dependency check.

The fixed schema is `Id,Amount,Active,Note`, exact and ordered, with comma as the
transport delimiter regardless of number culture. Quoting, escaped quotes, and
quoted newlines use the existing parser. Numeric/Boolean errors accumulate in
stable column order; a rejected row does not discard later rows. Blank, missing,
and extra-field records are explicit rejections. A fatal syntax/IO/limit error
or cancellation terminates the stream; previously yielded rows remain visible.
Consumers choose their persistence/transaction policy.

The default cap is 10,000 logical data records. Both CsvHelper's raw-field limit
and a decoded-field guard use 4,096 characters; the tests exercise the unquoted
4,096/4,097 boundary at EOF and with a newline. These are character limits, not a
byte-level memory quota. Error events contain codes/positions rather than raw
input or exception messages.

## Run locally

Use the repository's exact .NET 11 RC2 SDK, `net11.0`, and preview language for
native unions. From the repository root, with matching local Assay packages:

```sh
./eng/prepare-assay.sh ../CSharpAssay
./eng/run-csvhelper.sh

dotnet samples/60-csvhelper-functional/src/CsvFunctional.Cli/bin/Release/net11.0/CsvFunctional.Cli.dll \
  samples/60-csvhelper-functional/fixtures/mixed.csv
```

The mixed fixture emits accepted/rejected/accepted JSON events and exits **2**
because one row was rejected. CLI exit 0 means no rejection/fatal event, 64 means
invalid usage/culture, and 130 means cancellation. An empty header-only stream
is valid. The core and adapter tests cover normal and early disposal ownership.

The gate runs 18 Python evidence-boundary tests, locked restore/build,
coverage of both production core/adapter assemblies, two isolated original
baselines, 19 exact-span isolated mutations, 38 before/after offline advice
packets, and two authoritative verification receipts. Generated files go to
`evidence/generated/csvhelper/`. CI/default demo make no model calls and do not
fetch the upstream GitHub repository. NuGet restore may use the network.

## Initial conversion results, 21 October 2026

| Evidence | Result | Scope |
| --- | --- | --- |
| Functional sample build | Zero warnings/errors | Four .NET 11 projects |
| Authoritative verification | Pass; 79/79 tests | Seven visible advisories; no blocking findings/missing evidence/tool failures |
| Repeatability | JSON/SARIF byte-identical | Two authoritative verification runs |
| Evidence harness | Eight tests passed | Rejects stale/partial/mismatched execution, missing/empty coverage and changed inputs |
| Line coverage | 95/187 (50.80%) → 148/187 (79.14%) | Entire Domain + Adapter, including generated record/union members |
| Branch coverage | 26/62 (41.94%) → 57/62 (91.94%) | Same two assemblies and denominators; CLI exercised separately |
| Mutation detection | 4/19 → 18/19 | Two authored smoke tests → 79 tests; 14 additional defects detected |
| Survivor | `flag-equivalent-pattern` | `numeric == 0` → `numeric is 0`; contract review remains open |
| Decision Assist | Offline advice for every phase | Killed cases request no model; equivalent candidate routes to ReviewContract; verdict remains Inconclusive |
| Original upstream tests | 1,059 passed; four failed; 1,063 total | Unmodified net9 test target, Linux, runtime roll-forward Major |
| Original upstream Assay check | ToolFailure, provisional | Unsupported original TFMs/language profile and missing .NET Framework reference packs |

Coverage uplift is for this new consumer sample, **not** an improvement from
CsvHelper's original 1,063-test suite. The original tests' four failures concern
three newline expectations and one invalid-culture expectation on this host;
the runtime configuration differs from upstream's Windows CI. No mutation
claims are made on that failed upstream baseline.

Seven Assay advisories and their exact fingerprints are retained in
[expected-evidence.json](expected-evidence.json): five heuristic async
accumulation suggestions in tests and two nullable optional arguments in the
published-converter comparison helper. Agent rationale is recorded without
suppression. Case and advisory human adjudication remains pending.

[mutation-cases.json](mutation-cases.json) records each requirement, exact edit,
and agent review. The generated investigation report binds source/configuration,
locks, harness, advice binaries and mutant artifacts; it preserves raw reports
and rejects unexpected compilation/test execution failures. Coverage describes
original-source line execution, not proof of every mutant branch's reachability.

To summarize existing pinned upstream execution, or explicitly reproduce it:

```sh
./eng/tooling assess-csvhelper-upstream
./eng/tooling assess-csvhelper-upstream --run --fetch
```

The latter permits one exact-commit fetch only if the cache is absent and leaves
upstream source/tests unchanged. It selects the net9 target under the pinned SDK
and records `DOTNET_ROLL_FORWARD=Major`; the actual test-host runtime revision
was not observed in this run. The assessment records a failed baseline and
routes it to local HumanReview. `completed-assessment` does not mean passing
tests. The original multi-target/Windows compatibility matrix is unqualified.

The evidence supports continuing this sample as a consumer proving ground.
Next, adjudicate the survivor/advisories, add another CSV workload (writing or
large streaming imports), and measure allocation/throughput and provider value
before making production or superiority claims. Broad support for existing
.NET 8/9 libraries is also a product decision for Assay's current .NET 11-only
engine; a consumer adapter cannot resolve that engine limitation.


## Streaming workload and review follow-up

The next workload's [requirements](workloads/requirements.json) were registered
before implementation. [StreamingTests.cs](tests/CsvFunctional.Tests/StreamingTests.cs)
adds 12 executed cases, bringing the sample to **91 tests over five projects**.
It checks 50,000 valid records, 20,000 mixed records, one-character and larger
read chunks, quoted multiline records, German amounts, first-event demand from
a million-row source, cancellation after 25 records, and the exact 10,000-row
limit. Events are aggregated rather than retained. Closed-form count/identifier/
amount assertions supplement the direct-mapping comparison and content digest.
The workload is agent-authored, not a blind human-adjudicated evaluation.

[review-cases.json](review-cases.json) retains all seven advisory fingerprints
and the survivor. A test exhaustively compares zero equality and a zero constant
pattern for all **65,536 short values**. The agent conclusion is finite-domain
equivalence of those expressions; human adjudication is pending. The corpus
continues to count this case: no automatic equivalence exclusion or rule waiver.

The workload shell compares published CsvHelper record mapping with the
functional adapter on valid generated CSV. Each pipeline processes the same
10,000/100,000-row specifications and identical scalar/content digest sink.
A wrong digest invalidates measurement. Two warmups and five measured runs per
pipeline/size are recorded with raw elapsed times and process-wide managed
allocation deltas. The runner uses **Release** and `DOTNET_TieredCompilation=0`
to avoid tier transitions across short runs; the runtime, architecture, GC mode,
source/configuration and artifact hashes are recorded. Generated reads complete
synchronously; no file/network IO is benchmarked. Allocation is cumulative,
not retained or peak memory. Direct mapping implements fewer failure/limit
guarantees. These are local diagnostics, not a CI speed gate or production/SOTA
qualification. Use `./eng/tooling run-csvhelper-streaming` after preparation, or
the full sample gate, and read `evidence/generated/csvhelper/streaming/benchmark.json`.

The provider comparison selects one assertion gap, one unreached path, and the
survivor for contract review. It defaults to offline operation. An explicitly
requested live run is capped at six requests, with no retries/fallback after a
provider failure:

```sh
./eng/tooling evaluate-csvhelper-advice --live --provider openai --provider jev --max-requests 6
```

Only process-environment keys are presence-checked. Three agent-label action
comparisons cannot establish reviewer usefulness or provider quality; absent
keys are recorded as unavailable with zero calls. Read
`evidence/generated/csvhelper/advice-comparison.json` for the actual run status.

The framework decision for this milestone is a .NET 11 native core gate plus
separate upstream compatibility evidence. Broader .NET 8/9 support needs its own
engine design and qualification; no consumer result grants that support.


Controlled local measurement on 21 October 2026 (`DOTNET_TieredCompilation=0`,
Release, two warmups, median of five measured runs per pipeline/size):

| Rows | Pipeline | Median elapsed | Managed allocated bytes/row |
| --- | --- | --- | --- |
| 10,000 | Direct CsvHelper mapping | 51.254 ms | 484.44 |
| 10,000 | Functional adapter | 58.876 ms | 944.00 |
| 100,000 | Direct CsvHelper mapping | 466.055 ms | 514.97 |
| 100,000 | Functional adapter | 605.262 ms | 978.52 |

At 100,000 rows, the functional path used 1.299 times the elapsed time and 1.900
times the cumulative managed allocation in this run. This measures complete
input generation plus conversion/mapping and an identical digest sink; it does
not isolate native-DU cost. Source inspection identifies per-row conversion
objects, snapshots and error accumulation as allocation candidates; a profiler
and a separate before/after experiment are needed to attribute savings. The
next performance change should target the successful-row allocation path while
preserving typed outcomes and all existing behavioral requirements. Default
runtime tiering may behave differently; these controlled timings are not a
production estimate or an argument that functional code is inherently slower.


The completed follow-up gate passed authoritative verification for **five
projects and 91 tests**, retaining the same seven advisories with zero blocking
findings, missing evidence or tool failures. JSON and SARIF repeats were
byte-identical. All 18 Python evidence-boundary tests passed. Core+adapter line
coverage is now **151/187 (80.75%)**; branches remain **57/62 (91.94%)**, on the
same original scope. Mutation outcomes remain four detected by the two smoke
tests and 18/19 detected by the expanded suite. The exhaustive short-domain
argument strengthens review evidence without removing the survivor.

The explicitly requested six-request OpenAI/Jev comparison found both process
keys absent: **zero requests**, recorded as `unavailable_missing_keys`.
`validation.json` records this availability status separately from the sample's
authoritative Pass. Reviewer usefulness and human adjudication remain pending.


## Hefty physical mocks

`./eng/tooling run-csvhelper-hefty` creates four real UTF-8 CSV files under
ignored `evidence/generated/csvhelper/hefty/data/`: one million narrow rows,
100,000 rows with 1,024-character Unicode/quoted/multiline fields, 250,000 German
ledger rows, and 100,000 rows with 1% deliberately invalid numeric/Boolean data.
[hefty-cases.json](workloads/hefty-cases.json) registers the profiles, a 1 GiB
fixture cap, two runtime modes, two warmups and three measured runs per child.
The original package is CsvHelper 33.1.0. The assayed path is this functional
adapter using the same parser/package; it is not a complete rewritten library.

Run the full sample gate first; the benchmark requires its current authoritative
receipt and source bindings. Use `--reuse-fixtures` to reuse hash-validated mocks.
Generation and input hashing are outside the timed region. Each pipeline/fixture
uses a fresh process; measurements include file open/read, parsing/conversion,
an identical SHA-256 content/aggregate sink and disposal. Files are warmed by
two complete reads. This measures warm-cache file processing, not cold disk
bandwidth or isolated parser cost. Tiered JIT+PGO and non-tiered modes are
requested explicitly and recorded. GC collection counts, cumulative managed
allocation, process-lifetime peak RSS, input sizes/hashes, original library
binary hash and raw samples accompany all results. Peak RSS includes JIT and
warmups and is distinct from live managed heap size.

Valid-input comparisons require equal complete output hashes and totals.
For the dirty file, default `GetRecordsAsync` stops at the first conversion
failure; its exact prefix is verified but receives no full-file speed ratio.
A second original consumer uses CsvHelper's existing `TryGetField` APIs to
continue through every row. That consumer and the assayed adapter must produce
identical complete output, including error categories and rejected-row positions,
before their speed/allocation comparison is accepted. The functional adapter
constructs richer intermediate outcome objects; the ordinary resumable consumer
computes the same benchmark observables directly.

Eight new C# file tests bring the required suite to 99. Eight new Python
comparison-boundary tests bring the sample total to 26. The eighth retained
advisory concerns deliberately sequential awaited fixture generation. Heavy
file generation/measurement is opt-in; normal CI runs the smaller correctness
checks. No provider calls or GitHub access are needed for this comparison.

### Pre-optimization physical-file comparison — 2026-10-21

All four files were generated and all 18 fresh child processes completed: 90
passes including 36 warmups and 54 measured passes. Complete results matched
the reference SHA-256 and aggregate totals; default original mapping on the
dirty file matched its separately declared fail-fast prefix. Inputs and compiled
artifacts were hash-checked again after measurement. Total input is **1,450,000
logical rows / 295,681,325 bytes (281.98 MiB)**. Embedded quoted newlines mean
physical line counts are larger than logical row counts.

| Physical mock | Rows | MiB | Contents |
| --- | ---: | ---: | --- |
| plain-million.csv | 1,000,000 | 38.89 | Valid narrow rows, 16-character notes |
| wide-unicode.csv | 100,000 | 187.38 | 1,024-character notes, Unicode, commas, quotes and newlines |
| german-ledger.csv | 250,000 | 31.60 | German decimal culture, quoted comma decimals, Unicode notes |
| dirty-ledger.csv | 100,000 | 24.12 | Every 100th row has three invalid fields |

Environment: Ubuntu 24.04.5 LTS, Intel Core i7-4770HQ at 2.20 GHz (reported
model), eight logical processors, X64, workstation GC, Release, .NET
11.0.0-rc.1.26425.128. No competing sample builds/tests ran during the heavy
measurements. These are single-machine observations with three measured reads
per child, not confidence intervals or a production qualification.

With **tiered compilation and PGO enabled**, medians were:

| Workload | Original ms | Assayed ms | Assayed time change | Allocated bytes/row original → assayed |
| --- | ---: | ---: | ---: | ---: |
| Million narrow rows | 4,313.828 | 4,621.542 | +7.1% | 292.44 → 700.42 |
| Wide Unicode | 7,763.426 | 7,780.127 | +0.2% | 2,341.37 → 2,750.36 |
| German ledger | 2,068.737 | 2,193.140 | +6.0% | 388.28 → 796.20 |
| Dirty ledger, resumable original | 1,446.323 | 1,326.080 | −8.3% | 635.56 → 924.65 |

With **tiered compilation and PGO disabled**, medians were:

| Workload | Original ms | Assayed ms | Assayed time change | Allocated bytes/row original → assayed |
| --- | ---: | ---: | ---: | ---: |
| Million narrow rows | 5,030.812 | 6,792.621 | +35.0% | 292.44 → 756.43 |
| Wide Unicode | 8,781.447 | 9,113.117 | +3.8% | 2,341.61 → 2,806.56 |
| German ledger | 2,440.951 | 2,767.748 | +13.4% | 388.30 → 852.22 |
| Dirty ledger, resumable original | 1,675.709 | 1,662.744 | −0.8% | 667.26 → 980.60 |

Default original mapping accepted 99 rows and stopped at logical row 100 with a
conversion failure: 16.630 ms in tiered mode and 8.302 ms in non-tiered mode.
Those timings do not measure the complete file and have no full-file throughput
or speed ratio. The resumable original consumer and functional adapter both
accepted **99,000**, rejected **1,000**, and accumulated **3,000** field issues;
their SHA-256 includes rejected-row positions and issue categories as well as
accepted values. Ordinary CsvHelper can support this behavior through consumer
code; the functional adapter supplies typed outcomes and accumulation directly.

The functional adapter's tiered throughput was 216,378 rows/s for narrow data,
12,853 rows/s for wide Unicode, 113,992 rows/s for German data and 75,410 rows/s
for dirty data. Across complete consumers, process-lifetime peak RSS ranged from
120.05 to 129.12 MiB; the functional runs were 120.05–124.93 MiB. This does not
establish a memory advantage: RSS includes runtime/JIT/warmups and is not the
live heap. The functional path allocated more cumulatively in every matched
comparison, by 1.175–2.587 times. On the million-row tiered run, approximately
700 MB was allocated cumulatively even though peak RSS was about 123 MiB.

The valid wide-file tiered ranges overlap (original 7,724.620–7,857.139 ms;
assayed 7,747.539–7,871.979 ms), as do the non-tiered dirty-file ranges. Treat
those small differences as effectively tied in this experiment. The larger
narrow-row difference changes substantially with runtime tiering. The next
performance investigation should profile successful-row snapshots, conversion
outcomes and event allocations, then rerun these unchanged fixtures and output
checks. This experiment does not isolate native-union overhead or show a general
functional-programming speed advantage. Assay verification runs before timing;
its analysis/test execution is not part of importer runtime measurements.

The current authoritative sample gate passed **five projects, 99 C# tests and
26 Python evidence checks**, retaining eight advisories. Repeated JSON and SARIF
receipts are byte-identical, with zero missing evidence/tool failures. Core+
adapter coverage remains 151/187 lines (80.75%) and 57/62 branches (91.94%);
workload-shell code is outside that unchanged coverage scope. Mutations remain
18 detected of 19, with the survivor and human-review requirement retained.

Local generated evidence (ignored by Git):

- [Input manifest, expected outcomes and hashes](../../evidence/generated/csvhelper/hefty/data/manifest.json)
- [Frozen baseline comparison and identities](../../evidence/generated/csvhelper/milestones/before/evidence/hefty/comparison.json)
- [Frozen baseline sample validation](../../evidence/generated/csvhelper/milestones/before/evidence/validation.json)
- Baseline repetitions: `evidence/generated/csvhelper/milestones/before/evidence/hefty/raw/`.

From the repository root, rerun with:

```sh
./eng/run-csvhelper.sh
./eng/tooling run-csvhelper-hefty --reuse-fixtures
```

Omit `--reuse-fixtures` to regenerate the deterministic physical mocks. The
benchmark validates the registered plan, actual input hashes, independent
aggregate formulas and all measured outputs before accepting comparisons.
These commands measure the current checkout; the table above is the archived
pre-optimization run. The follow-up below compares the revised implementation.

## Follow-up implementation and reviewer handoff

[Follow-up requirements](workloads/follow-up-requirements.json) were registered
before implementation. The original hefty run, verified inputs, workload
binaries, raw results and receipts were archived under
`evidence/generated/csvhelper/milestones/before/` before production changes.
The original parser/package and all four physical fixtures remain unchanged.

`RowDecisions.Convert` now constructs its issue builder only on rejection. All
three conversions still execute and errors retain column order. The private
read helper returns `ValueTask<ReadStep>` and is awaited once. Public conversion
outcomes, immutable snapshots, limits and cancellation forwarding remain intact.
Stage allocation probes measured an empty issue builder at 120 bytes and a
complete valid-row decision at 384 bytes before, 264 after. Field inputs (72
bytes) and conversion outcomes (112 bytes) were unchanged. Probes overlap and
must not be added; they are not stack traces or retained-heap measurements.

`CsvExporter.WriteAsync(records, writer, cancellationToken)` streams the same
schema. Pure `ExportDecisions.Format` returns native `ExportRowOutcome`; the
adapter emits native `ExportEvent` cases for written rows, failure and
cancellation. Culture is copied at construction, decimal precision is preserved,
fields are always quoted and records use CRLF. The caller owns the writer.
Notes are bounded at 1,024 characters (including quote expansion safely within
importer limits); the configurable row limit defaults to 10,000.

Cancellation and active output IO failures terminate explicitly. Cleanup
disconnects pending buffers before disposing CsvHelper, avoiding retries after
failure or writes after cancellation/abandonment. Completion flushes the writer;
progress does not promise atomic or durable writes. Source iterator exceptions
remain caller exceptions. Seventeen exporter tests cover formatting, full
precision, Unicode, boundaries, cancellation, ownership and IO/producer failures.
Two physical tests compare output bytes and values under invariant/German
cultures. Five new mutations cover export Boolean values, note/row boundaries,
delimiter and cancellation forwarding; the corpus now has 24 explicit cases.

The full gate prepares `evidence/generated/csvhelper/review/packet.json` with
every mutation requirement and advisory, source excerpts/hashes and measured
outcomes. `response-template.json` is blank for an independent reviewer;
`usefulness-protocol.json` contains a study design with no invented results.
Response validation requires the exact packet identity, one response per item,
a self-reported reviewer and rationale. Feedback cannot change verification or
the mutation denominator. Process independently completed responses with:

```sh
./eng/tooling prepare-csvhelper-review --responses /path/to/reviewer-responses.json
```

The usefulness protocol calls for unseen preregistered cases, counterbalanced
deterministic/provider-assisted arms, hidden provider names and independently
rated investigations. No participants, human timings or usefulness outcomes
have been collected. The writing requirements remain agent-authored and do not
establish independently held-out consumer qualification. Human adjudication is
pending. The framework direction remains native .NET 11 plus separate unchanged
upstream compatibility evidence; older engine targets and the original
Windows/multi-target matrix remain unqualified, separate work.

Live provider comparison needs process keys and the existing six-request cap:

```sh
./eng/tooling evaluate-csvhelper-advice --live --provider openai --provider jev --max-requests 6
```

After the current full gate, opt-in measurements and comparison use:

```sh
./eng/tooling run-csvhelper-hefty --reuse-fixtures
./eng/tooling run-csvhelper-roundtrip
./eng/tooling compare-csvhelper-milestones
```

The writing worker uses 50,000-row Unicode fixtures under each of invariant and
German culture, counterbalancing both pipelines in one process with one warmup
and three measured passes each. Timing includes input open/read/import and
output export/flush/disposal. Generation, output hashing and independent original
re-import validation are excluded. Equal complete values and physical bytes are
required. Its scope differs from the fresh-process import-only benchmark.

## Completed follow-up results — 2026-10-21

All 18 optimized import child processes completed with current source/artifact
bindings and unchanged physical input hashes. All complete output hashes and
default-original fail-fast prefixes matched their registered references. The
archived baseline remains intact. Current medians (three measured reads):

| Runtime | Workload | Original seconds | Assayed seconds | Assayed time change | Allocation bytes/row original → assayed |
| --- | --- | ---: | ---: | ---: | ---: |
| Tiered/PGO | Million narrow | 4.168 | 4.555 | +9.3% | 292.44 → 564.47 |
| Tiered/PGO | Wide Unicode | 7.739 | 7.768 | +0.4% | 2,341.37 → 2,616.76 |
| Tiered/PGO | German ledger | 2.064 | 2.074 | +0.5% | 388.28 → 660.36 |
| Tiered/PGO | Dirty ledger* | 1.402 | 1.283 | -8.5% | 667.23 → 734.16 |
| Non-tiered | Million narrow | 5.051 | 6.205 | +22.8% | 292.44 → 564.48 |
| Non-tiered | Wide Unicode | 8.782 | 9.220 | +5.0% | 2,341.61 → 2,617.00 |
| Non-tiered | German ledger | 2.416 | 2.641 | +9.3% | 388.30 → 660.38 |
| Non-tiered | Dirty ledger* | 1.646 | 1.713 | +4.1% | 667.26 → 790.11 |

*Dirty comparisons use the resumable original consumer. Both paths accept
99,000 rows, reject 1,000 and accumulate 3,000 issues. Default mapping still
stops at row 100 and is excluded from full-work ratios. The near-zero tiered
wide/German differences have overlapping ranges; non-tiered dirty ranges also
overlap. Do not infer significance from three within-process repetitions.

Against the archived assayed implementation, allocations decreased in all eight
matched cases, by **4.9%–25.4%**. Million-row allocations changed from 700.42 to
564.47 bytes/row with tiering, and from 756.43 to 564.48 without tiering. The
stage probe fell from 384 to 264 bytes per valid row (31.25%). These values have
different scopes and are not additive. End-to-end time changed inconsistently:
some workloads improved, others were slightly slower. Original-control timing
also moved; neither memory reduction nor these medians establish a general
speed advantage or native-union cost attribution.

The writing workload completed 16 passes, including four warmups and 12 measured
passes, with identical physical output hashes and successful independent original
re-import on every pass. Medians:

| 50,000-row round trip | Original seconds | Assayed seconds | Allocation bytes/row original → assayed |
| --- | ---: | ---: | ---: |
| Invariant Unicode | 0.694 | 0.621 | 1,412.01 → 1,730.88 |
| German Unicode | 0.623 | 0.620 | 1,412.01 → 1,730.89 |

This secondary worker counterbalances pipelines in one process and performs one
warmup each. Its tiering behavior and scope differ from the import benchmark;
the apparent invariant speed improvement is diagnostic, not qualification. The
German medians are effectively tied and allocation is higher in both functional
round-trip paths.

Current authoritative validation passed **five projects, 118 C# tests and 36
Python evidence checks**. JSON/SARIF repeats are byte-identical, with ten retained
advisories and zero missing evidence/tool failures. The expanded production
scope covers **225/271 lines (83.03%) and 100/112 branches (89.29%)** across both
Domain and Adapter. This includes the new exporter; historical percentages have
different denominators. The unchanged two-test smoke selection detects 4/24
mutations; the expanded selection detects **23/24**. The survivor stays in the
denominator. The review packet contains 24 mutation cases and ten advisories.

Local implementation milestones are complete. Remaining external evidence:

| Milestone | Status |
| --- | --- |
| OpenAI/Jev live comparison | Both process keys unavailable; zero requests |
| Independent human adjudication | Pending; 34-item packet and blank response template prepared |
| Reviewer usefulness study | Not executed; protocol prepared |
| Independently held-out consumer qualification | Pending; these writing requirements are agent-authored |
| Broader engine targets and original Windows/multi-target compatibility | Separate unqualified work; native .NET 11 direction retained |

Current local evidence:

- [Authoritative validation](../../evidence/generated/csvhelper/validation.json)
- [Optimized import comparison and all raw-report hashes](../../evidence/generated/csvhelper/hefty/comparison.json)
- [Frozen before/after allocation and timing comparison](../../evidence/generated/csvhelper/milestones/comparison.json)
- [Writing results and raw samples](../../evidence/generated/csvhelper/roundtrip/comparison.json)
- [Milestone status and evidence hashes](../../evidence/generated/csvhelper/milestones/status.json)
- [Human review packet](../../evidence/generated/csvhelper/review/packet.json)

All work remains local. The original source checkout and paired engine remain
unchanged. No GitHub publication or model-provider request occurred.
