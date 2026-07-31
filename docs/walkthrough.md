# From impure metal to reviewed evidence

This walkthrough demonstrates what CSharpAssay can prove today and keeps that
claim narrower than “the program is correct.” It uses a controlled rule matrix
for detector coverage and a pinned public kata for behavior-preserving
refinement.

## Prerequisites

- .NET SDK `10.0.301` (pinned by `global.json`)
- Python 3 for the dependency-free evidence assertion
- a built CSharpAssay runner DLL

Run the entire gate from the repository root:

```bash
./eng/run-assay.sh /path/to/cs-assay.dll
```

The script performs locked restore, warning-clean release build, 9 refined
tests, 8 upstream characterization cases, two provisional observations, one
authoritative verification, provenance validation, and exact report checks.

## Stage 1: calibrate the instrument

`samples/00-rule-matrix/impure/PoisonedProfile.cs` intentionally contains one
or more examples for every admitted stable rule. Its expected inventory is:

| Rule | Signal | Findings |
| --- | --- | ---: |
| `CSAI0001` | mutable record setters | 2 |
| `CSAI0002` | mutable collection on an immutable carrier | 1 |
| `CSAN0001` | nullable analysis disabled | 2 |
| `CSAN0002` | null-forgiving erases evidence | 1 |
| `CSAN0003` | null/default introduced in core | 2 |
| `CSAN0004` | nullable public core contract | 2 |
| `CSAP0001` | unauthorized suppression | 2 |

The command is deliberately `check`, not `verify`. It reports 12 findings but
returns a provisional pass. Therefore CI asserts report contents; it never
mistakes the process exit code for release approval.

## Stage 2: observe pinned public code

The first public specimen is Emily Bache’s Gilded Rose Refactoring Kata,
pinned to commit `3e0085bfd0da7ca9cc2db23129eb882b9d7184fa`. The two imported production
files are unchanged except for BOM normalization. Their upstream blobs and
local SHA-256 hashes are recorded in `provenance.json`.

Before refinement, the characterization executable freezes eight representative
behaviors: normal inventory before and after expiry, the quality floor, Aged
Brie, Backstage passes, Sulfuras, and the starting implementation’s Conjured
behavior.

The upstream Assay check reports one fact: nullable analysis is disabled. It
does not report the mutable `Item` class because `CSAI0001` and `CSAI0002`
currently target immutable carriers. That limitation is adjudicated rather
than hidden.

## Stage 3: refine without changing behavior

The reviewed derivative uses:

- an immutable `readonly record struct` inventory item;
- `ImmutableArray<T>` at the core boundary;
- a pure update function returning a new array;
- explicit closed `Missing` and `Present` domain states instead of `null`;
- nullable analysis and warnings-as-errors throughout.

The Conjured requirement remains a separately recorded feature gap. A
structural refactor must not silently change business behavior.

## Stage 4: release authority

`verify Playground.Refined.slnx` loads both refined projects and their test
shell using `.csassay.refined.json`. The accepted result is exact:

```text
Pass (authoritative)
Projects: 3  Findings: 0  Tests: 9  Missing: 0  Failures: 0
```

`eng/assert-evidence.py` also confirms every required rule completed, no tool
failure or missing evidence was recorded, and provenance hashes still match.
Generated JSON and SARIF live under `evidence/generated/` and are uploaded by
CI rather than committed.

## Reading the result honestly

The demonstration establishes a repeatable gate for the configured project,
target framework, boundaries, rules, and tests. Read `adjudication.md` for the
decision method and `blind-spots.md` before applying broader labels.
