# Polly resilience consumer

This sample compares a direct original-style Polly consumer with an immutable,
native-union domain adapter using the same pinned upstream Polly 8.6.5 packages.
It does not rewrite or certify Polly. Package provenance and the upstream license
are retained in `upstream.json` and `licenses/`.

Eight independently specified scenarios execute on both pipelines: success,
transient recovery, permanent failure, exhausted retry budget, per-call deadline,
total deadline across retries, cancellation before work and cancellation during
work. A controlled clock avoids real sleeps. Six additional real-package checks
exercise breaker opening, concurrent half-open probes, recovery, concurrency
rejection, permit release and explicitly degraded fallback.

```sh
./eng/run-application-workflow.sh
```

The gate runs 39 workflow tests, authoritative native assessment and six exact
production mutations. Five are expected to be killed and one equivalent-looking
retry predicate remains a survivor requiring review. All execution states and
findings remain in ignored evidence. The generated standalone app and an actual
requirement change are checked by the same command.

See [the application workflow](../../docs/application-workflow.md) for API
contracts, new-app commands, evidence boundaries and current limitations.
