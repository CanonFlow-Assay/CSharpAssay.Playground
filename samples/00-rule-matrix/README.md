# Controlled rule matrix

`impure` contains deliberately isolated specimens for every admitted stable
rule. `refined` demonstrates immutable carriers and explicit domain cases with
none of those findings.

The impure matrix exposes mutable queues, stacks, and a concurrent dictionary
on a core record. Its shell exercises three synchronous task consumption patterns.
`CSAI0002` is a required rule and `CSAA0002` contributes prototype advisories.
The refined matrix uses immutable collections in its core and awaits tasks in
its shell. The primary evidence gate checks the exact finding counts for both.

When a core-effect rule is available, the ambient-input controls will cover
`DateTime.UtcNow`, `Guid.NewGuid()`, and `Environment.GetEnvironmentVariable`.

This lane answers “did the detector fire exactly where expected?” It is not a
realistic application and must not be used to estimate defect prevalence.
