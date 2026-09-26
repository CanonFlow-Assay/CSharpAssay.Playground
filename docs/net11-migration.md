# .NET 11 RC1 migration

Required: SDK `11.0.100-rc.1.26425.128`, `net11.0`, C# preview, and matching local `CsAssay.Tool` / `CsAssay.Analyzers` `0.2.0-rc.1` packages.

Run `eng/prepare-assay.sh ../CSharpAssay` before restoring. It packs the paired source revision, refreshes only the checkout-local Assay package cache, and regenerates locks for those local package bytes. Later restores use locked mode. CI accepts `assay_ref` or `CSHARP_ASSAY_REF` for the paired source revision. The complete implementation report is in the sibling repository at `CSharpAssay/docs/net11-migration.md`.

Shipment and recursive menu examples use native unions; behavior tests compare them with the classic implementations. Domain/application/shell policy and architecture gates remain. The external eShop .NET 10 case study and previous generated evidence are historical.

Validation completed locally on 2026-09-26 with the pinned SDK:

| Check | Result |
| --- | --- |
| Release builds and locked restores | Passed |
| Main playground tests | 9 passed |
| GoF equivalence tests | 10 passed |
| Shape behavior and architecture tests | 16 + 7 passed |
| Gilded Rose characterization | 8 of 8 passed |
| All three evidence scripts | Passed |
| GoF and Shape JSON/SARIF | Byte-identical across repeated runs |
| Older-framework rejection | .NET 10 rejected by the repository guard |

All 42 tests ran with zero failures and zero skips, in addition to the eight characterization checks. Refined playground and GoF evidence is authoritative with zero findings. Shape is authoritative with six loaded projects, 23 passing tests, and the same three expected advisories (`CSAN0003` × 1, `CSAN0004` × 2).

Shape fingerprints were refreshed for the new target framework. Recomputing each finding with its old `net10.0` identity reproduced its prior fingerprint exactly. Expected counts, the seven admitted rules, policy strength, and architecture boundaries are preserved. Project-owned `Result` and `Option` types remain; the migrated GoF examples exercise native unions.

The preparation script passed end to end, followed by all three gates using the resulting local packages and locks. All 15 retained playground lockfiles target .NET 11. Validation logs are retained locally under ignored `artifacts/net11-validation/`. The paired CSharpAssay migration also passed 135 tests, reproducible package checks, fresh installation, native-union compilation, and framework/language rejection checks.

Changes are local and have not been pushed or published. Set the playground CI source reference to the paired CSharpAssay migration revision before running it remotely.
