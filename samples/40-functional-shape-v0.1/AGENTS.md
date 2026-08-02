# Shape v0.1 agent contract

Work only inside this sample unless the human explicitly expands scope.

## Required architecture

- Domain is the pure core and references no framework or shell.
- Application is framework-free orchestration, owns effect ports, and references
  Domain only.
- Infrastructure implements Application-owned ports.
- API translates transport input once, composes the workflow, and maps both
  Result cases to transport responses.
- `Result<TValue,TError>` has only `Success` and `Failure`.
- `Option<T>` has only `Some` and `None`.
- No case carries null; null is not another domain case.

## Prohibited shortcuts

Do not weaken `.csassay.json`, move a project out of scope, disable analyzers,
add `NoWarn` for a CSharpAssay rule, introduce `null!`, put effects in Domain,
serialize Domain objects directly, add source project references to
CSharpAssay, or auto-fix an advisory finding.

Public representation, dependency direction, policy strength, analyzer scope,
package version, or shell/core ownership changes require human review.

## Required commands

```text
dotnet restore Shape.slnx --locked-mode
dotnet build Shape.slnx --no-restore --configuration Release
dotnet test Shape.slnx --no-build --no-restore --configuration Release
../../../eng/run-functional-shape.sh
```

The evidence script uses the published 0.1.2 tool and analyzer packages and
runs real `check` and `verify` commands with `--json`, `--sarif`, and `--html`.

Do not claim success for skipped or zero tests, unloaded projects, compiler or
workspace errors, missing evidence, tool failures, incomplete rules,
non-authoritative evidence, changed expected counts, or nondeterministic JSON
or SARIF. LLM judgment is advisory; compiler, tests, CSharpAssay, hashes, and
human review are authoritative.
