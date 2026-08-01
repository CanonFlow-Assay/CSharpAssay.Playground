# CSharpAssay Playground

[![Refined evidence gate](https://github.com/CanonFlow-Assay/CSharpAssay.Playground/actions/workflows/playground.yml/badge.svg)](https://github.com/CanonFlow-Assay/CSharpAssay.Playground/actions/workflows/playground.yml)

An evidence-first proving ground for CSharpAssay. It combines deliberately
controlled rule specimens, pinned public refactoring exercises, and manifests
for larger real applications. The aim is to demonstrate measured improvement
without calling unfamiliar production code “bad” or claiming universal purity.

Start with [the walkthrough](docs/walkthrough.md), then read the
[adjudication method](docs/adjudication.md), [evidence foundry contract](docs/evidence-foundry.md),
and [known blind spots](docs/blind-spots.md).

## Repository lanes

- `samples/00-rule-matrix`: exact negative and positive examples for every
  admitted stable rule.
- `samples/10-gilded-rose`: an untouched, pinned upstream slice and a reviewed
  immutable derivative protected by characterization tests.
- `samples/20-eshop-agent-assay`: a pinned, external-clone handoff for an
  agent-led assay of eShop's Ordering domain, including separate upstream and
  reviewed-candidate evidence; source is not vendored.
- `samples/catalog.json`: imported and queued public repositories with immutable
  revisions and licensing posture.
- `evidence`: deterministic Assay reports and human adjudication records.
- `eng/run-assay.sh`: one reproducible build, behavior, Assay, and evidence gate.

## Local gate

```text
dotnet restore CSharpAssay.Playground.slnx --locked-mode
dotnet build CSharpAssay.Playground.slnx --no-restore -c Release
dotnet test tests/Playground.Tests/Playground.Tests.csproj \
  --no-build --no-restore -c Release
./eng/run-assay.sh /path/to/cs-assay.dll
```

To include the pinned external eShop Ordering representation assertion used by
CI, provide its checkout without moving its branch:

```text
ESHOP_UPSTREAM_ROOT=/path/to/eshop ./eng/run-assay.sh /path/to/cs-assay.dll
```

The complete solution includes deliberately impure policy specimens. A
provisional `check` still exits successfully while reporting those findings;
the evidence assertion makes their exact presence mandatory. Only `verify` on
the refined solution is release authority, and it must pass with zero findings.

Here, “refined” means clean under the pinned, admitted policy and protected by
the recorded behavior tests. It does not mean universally correct or pure.
