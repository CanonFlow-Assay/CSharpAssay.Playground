# Inventory reservation: a second application through DAA

This application uses native C# unions for immutable decisions, a real HTTP
endpoint and PostgreSQL transactions. It consumes the locally packed
`CanonFlow.Assay.Workflow` preview SDK; it has no source reference into the
toolkit or CSharpAssay engine. All five projects target the pinned .NET 11 SDK
with preview C#, nullable analysis and warnings treated as errors.

`Inventory.Domain` validates a request and returns commit, replay, invalid,
insufficient-stock or conflict outcomes. `Inventory.Adapter` owns the database:
an advisory transaction lock serializes each idempotency key, a stock row lock
protects concurrent decrements, and stock, reservation and outbox rows commit
together. `Inventory.Api` projects outcomes to HTTP 201/200/400/409. Reopening a
store preserves receipts. The runnable API uses a persistent schema and does not
reset stock on restart.

The tests and worker execute concurrency, idempotency, conflicting replays,
rollback, cancellation and connection persistence against PostgreSQL. The worker
declares seven scenario identities and three infrastructure checks; all outcomes
must be present and pass. Resilience traces bind actual attempts, elapsed time
and observed outbox side effects to the configured bounds. ContractAssay compares
the pinned response wire members and emitted outcome projection.

From the repository root, after preparing paired engine packages:

```sh
./eng/run-inventory-maturity.sh --output evidence/generated/maturity/final/inventory
```

Docker must be available. The wrapper starts a temporary PostgreSQL 18 image
pinned by digest, generates credentials in memory, binds to loopback, and removes
the container after execution. An explicitly supplied `ASSAY_INVENTORY_DATABASE`
connection is also supported; never print or commit its value. Each test creates
its own schema. Missing infrastructure fails rather than skipping tests.

The qualification reconstructs the original 25-test baseline, registers a limit
of three units for new reservations, records two failing acceptance tests, and
then implements the limit. Historical receipts remain replayable even when their
quantity exceeds the new limit. All 27 tests, previous named tests and the old
contract pin must survive. Six isolated mutations retain five kills and an
equivalent-looking survivor for review.

In a separate retained workspace the qualification renames the wire member
`remaining` to `available`. Typed behavior tests continue to pass; ContractAssay
must fail. DAA selects the registered `inventory-contract` probe and reruns the
worker comparison to reproduce the differences. Unregistered, stale and exhausted
investigations must be refused. Only after the complete exercise succeeds does
the script export the tested limit implementation to this sample.

Inspect the output's `qualification.json`, baseline/candidate `run.json`, raw
coverage and mutation artifacts, deliberate regression and probe receipts.
Failed prior runs remain failed evidence. Deterministic routing is visible;
there are no model-provider calls or independent human-quality claims.

This is a bounded application example. Authentication, operational hardening,
outbox delivery, migration operations, production load and disaster recovery
remain separate work. ContractAssay covers selected response members rather than
the full HTTP/OpenAPI/serialization surface. CodeRisk remains an ordinal heuristic.

Dependencies: Npgsql 10.0.2, PostgreSQL license, package repository commit
`4d57e27b370e2ab2a350ad8bcef78d7ddd97e5ea`; the original package and its lock-file
content hash are retained in the local feed/cache. No Npgsql source is copied.
The PostgreSQL container digest is pinned in `eng/tooling with-inventory-database`.
The authored application follows the repository's Apache-2.0 license.
