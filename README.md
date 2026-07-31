# CSharpAssay Playground

An evidence-first proving ground for CSharpAssay. It combines deliberately
controlled rule specimens, pinned public refactoring exercises, and manifests
for larger real applications. The aim is to demonstrate measured improvement
without calling unfamiliar production code “bad” or claiming universal purity.

Start with [the walkthrough](docs/walkthrough.md), then read the
[adjudication method](docs/adjudication.md) and [known blind spots](docs/blind-spots.md).

## Repository lanes

- `samples/00-rule-matrix`: exact negative and positive examples for every
  admitted stable rule.
- `samples/10-gilded-rose`: an untouched, pinned upstream slice and a reviewed
  immutable derivative protected by characterization tests.
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

The complete solution includes deliberately impure policy specimens. A
provisional `check` still exits successfully while reporting those findings;
the evidence assertion makes their exact presence mandatory. Only `verify` on
the refined solution is release authority, and it must pass with zero findings.

Here, “refined” means clean under the pinned, admitted policy and protected by
the recorded behavior tests. It does not mean universally correct or pure.
