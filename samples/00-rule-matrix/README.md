# Controlled rule matrix

`impure` contains deliberately isolated specimens for every admitted stable
rule. `refined` demonstrates immutable carriers and explicit domain cases with
none of those findings.

The impure matrix exposes mutable queues, stacks, and a concurrent dictionary
on a core record. Its shell exercises three synchronous task consumption patterns.
`CSAI0002` is a required rule and `CSAA0002` contributes prototype advisories.
The refined matrix uses immutable collections in its core and awaits tasks in
its shell. The primary evidence gate checks the exact finding counts for both.

`CSAE0003` reports the impure core's `DateTime.UtcNow`, `Guid.NewGuid()` and
`Environment.GetEnvironmentVariable()` calls. `CSAE0004` reports its file,
console and HTTP calls. The refined shell captures the same inputs and performs
the I/O. Its core receives explicit data. The gate expects three advisories per
prototype in the impure matrix and zero in the refined gate.

This lane answers “did the detector fire exactly where expected?” It is not a
realistic application and must not be used to estimate defect prevalence.
