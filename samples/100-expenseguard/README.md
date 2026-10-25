# ExpenseGuard — CSAssay Playground application

AI advises. The domain decides. Evidence states exactly what ran.

Five application projects and one test project implement a bounded expense
approval/reimbursement product sample. The exact .NET 11 RC2 SDK and native C#
unions are mandatory; the language feature remains preview-qualified.

## Product flow

Open `/` or `/claims/{id}/evidence`. The workbench provides identity connection,
scoped claim/review inboxes, draft creation/revision, receipt upload, submission,
assessment, manager/finance actions, audit holds, reimbursement queueing,
historical receipt download and JSON evidence export. Keys stay in page memory;
no browser storage is used. Identity and role come from server configuration.

* Above 25 USD after FX needs a receipt. Exactly 25 is exempt.
* Meals above 50 per employee/day need itemization and manager review.
* Transport above 100/claim, hotel above 200/night and software above 100/month
  require manager review. `Units` means nights or subscription months.
* Above 200 requires manager review. Above 1,000 requires manager then distinct
  finance approval. Self-approval is blocked.
* Receipt hash OR merchant + original money + date match creates an audit hold.
* Missing approved, dated FX means NeedsMoreInfo; no default rate is invented.
* Finance queues Approved claims. The actual worker rechecks approval, audit
  integrity and stored receipt bytes before recording **simulated** payment.

Payment references begin `SIMULATED-`; no money moves. No email delivery is
implemented. See [requirements](docs/requirements.md), [policy](docs/policy.md),
[state machine](docs/state-machine.md), and [AI boundaries](docs/ai-advice-boundaries.md).

## Run locally

Use the pinned SDK, PostgreSQL, and normal .NET configuration/environment.
Configure separate random tokens of at least 32 characters. Do not commit secrets
or put them in command arguments.

```text
ConnectionStrings__Expenses           local PostgreSQL connection string
ExpenseGuard__Identities__0__Token    employee token
ExpenseGuard__Identities__0__Employee alice
ExpenseGuard__Identities__0__Role     Employee
Additional identity rows             separate Manager / Finance / Auditor identities
ExpenseGuard__Initialize              true (local schema initialization)
ExpenseGuard__ReceiptDirectory        absolute private path, SAME for API and worker
ExpenseGuard__EvidenceRoot            successful qualification directory
ASPNETCORE_URLS                       http://127.0.0.1:5188
```

From this sample directory:

```bash
dotnet run --project src/ExpenseGuard.Api -c Release
# Another terminal, same database and receipt-directory configuration:
dotnet run --project src/ExpenseGuard.Worker -c Release
```

The worker requires initialized tables. External deployment needs authenticated
TLS ingress and enterprise identity. This key adapter demonstrates one-company
authorization, not multi-tenant identity.

## Persistence and receipt trust

PostgreSQL atomically commits current state, actor/time/snapshot hash-chain
receipts and the payment outbox. A transaction advisory lock serializes ledger
writes so duplicate and daily-total checks see consistent facts. Expected
versions reject stale commands. Corrupt audit chains block subsequent writes
and payment processing.

This favors understandable bounded correctness over throughput: it reads the
ledger and validates its chains. Hash chains are not signatures against a
privileged database rewriter.

PDF/PNG/JPEG uploads are capped at 3 MiB. The server computes SHA-256, uses
claim-bound opaque references, stores bytes outside the web root and creates
owner-only files on Linux. Downloads require authorization and matching hashes,
and use attachment disposition. Historical snapshots retain receipt references.
Draft revision clears current receipt/policy; authorized historical download
still works. Missing/changed stored bytes block approval progression/payment.

External references/hashes remain claimant assertions: remote bytes are never
fetched or verified. File magic/hash checks do not establish authenticity,
itemization, malware safety or eligibility. Itemization remains a human assertion.

Database and filesystem are separate resources. Rejected uploads are cleaned
up, but process crashes can leave orphan files. Retention, backups, orphan
reconciliation, scanning and storage operations remain deployment work.

## Optional receipt extraction

The default advisor runs offline and deliberately suggests Approved despite
missing evidence, making its lack of authority observable.

The OpenAI receipt-extraction adapter is implemented and tested with controlled
HTTP responses. It follows the official [structured-output format](https://developers.openai.com/api/docs/guides/structured-outputs),
[PDF inputs](https://developers.openai.com/api/docs/guides/file-inputs) and
[image inputs](https://developers.openai.com/api/docs/guides/images-vision?api-mode=responses).

It is disabled by default. Owner-controlled opt-in uses:

```text
ExpenseGuard__EnableLiveExtraction       true
ExpenseGuard__OpenAiModel                explicit compatible model ID
ExpenseGuard__OpenAiApiKey               configured secret
ExpenseGuard__MaximumExtractionRequests  1..8 (default 3 per process)
```

The claimant explicitly authorizes sending the stored file to OpenAI. The
application records the request before transmission. The endpoint is fixed to
`https://api.openai.com/v1/responses`; redirects, retries and provider fallback
are disabled. A deadline, bounded response, strict local validation and
per-process request budget reject malformed, refused, incomplete or restricted
output. Budget counters reset on restart. `store=false` is requested; a provider
privacy review is still required.

The result proposes fields, category, itemization, anomaly notes and a state.
Nothing is applied automatically. A human confirms any draft revision, after
which deterministic assessment decides. No live provider quality/privacy
evaluation is claimed by this offline qualification.

## Reproduce the evidence

From the Playground root, after normal engine package preparation, with the
pinned SDK on PATH and Docker available:

```bash
export ASSAY_RUNNER=/absolute/path/to/qualified-engine/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll
./eng/run-expenseguard.sh --output "$PWD/evidence/generated/expenseguard/run-001"
```

The source-bound qualifier requires clean committed source and a fresh output
directory. Missing PostgreSQL fails, never skips. It performs:

1. Locked restore/build and all 159 named tests, including real HTTP/PostgreSQL,
   the actual worker, file authorization/integrity, concurrency, cancellation,
   audit corruption, rollback, retry and offline provider-protocol controls.
2. Authoritative CSAssay `verify` and `assess`, with JSON and SARIF.
3. Twelve separately built business/persistence/integrity mutations with the
   identical test selection. Compile/incomplete execution is never a kill.
4. A missing-receipt shortcut through `verify`: required business tests cause Fail/1.
5. Packaged-analyzer rejection of a mutable record and compiler rejection of
   an unhandled union case, with detector identities kept separate.
6. Existing typed DAA `AssessmentRules` / `ApplicationAssistance` over observed
   modules. This bounded coordinator is separate from generic `daa verify`
   coverage/worker/contract qualification.

Inspect `qualification.json`, `baseline.json`, `verify.json`, `verify.sarif`,
`assessment.json`, `mutations.json`, `daa-assessment.json` and retained logs.
The finite mutation set is agent-authored; independent adjudication remains
open. Test counts imply neither a coverage percentage nor provider quality.

The viewer displays Pass only for a hash-bound authoritative report matching
the running domain assembly. Missing, stale, malformed or provisional evidence
displays NotAvailable. This local binding assumes trusted evidence storage;
it is not a signed remote attestation.

## API

Every `/api` endpoint needs `X-ExpenseGuard-Key`. State-changing requests need
the expected version returned by the preceding operation.

| Method/path | Purpose |
| --- | --- |
| GET `/api/me`, `/api/claims` | Authenticated identity and scoped inbox |
| POST `/api/claims` | Draft: amount, currency, category, merchant, spentOn, units |
| PUT `/api/claims/{id}` | Revision plus expectedVersion; clears receipt/policy |
| POST `/api/claims/{id}/receipt-file?expectedVersion=1&itemized=true` | Raw PDF/PNG/JPEG bytes |
| GET `/api/claims/{id}/receipt-file?sequence=2` | Current or authorized historical download |
| POST `/api/claims/{id}/receipt` | External reference, asserted sha256, itemized, expectedVersion |
| POST `/api/claims/{id}/commands` | submit/approve/reject/hold/reimburse, expectedVersion, optional reason |
| POST `/api/claims/{id}/assess` | Assessment; appends two policy receipts |
| POST `/api/claims/{id}/receipt-extraction` | expectedVersion + confirmExternalDisclosure; proposal only |
| GET `/api/claims/{id}/advice` | Offline advisory DTO |
| GET `/api/claims/{id}/evidence` | Policy, receipt status, audit chain and build evidence |

Required constructor fields must be present; enum values use names, with numeric
defaults rejected. Extraction output uses exact schema field names. See the
[JSON constructor contract](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties).

Client actor/role, policy, ledger/duplicate/FX facts and payment references are
never accepted. Invalid/stale transitions return 409; unauthorized claims return
404. Operational/storage/commit failures report no success. Refresh state rather
than assuming an error response proves no commit occurred.

## Milestones and release boundary

All six declared milestones have implementations: domain, policy engine,
CSAssay gate, API/PostgreSQL, advisory/extraction adapters, and workbench/evidence
viewer. CI retains read-only repository permissions, compiler diagnostic
annotations and SARIF artifacts. A hosted run is not claimed before publication.

Independent human business/security review, live extraction quality/privacy,
real payments/reconciliation, enterprise identity, tenant isolation, receipt
scanning/authenticity, database/storage operations, load/platform acceptance and
browser visual/accessibility review remain separate from this bounded sample.
No browser surface was available for visual automation; HTTP UI delivery and
JavaScript syntax were checked. No production/security/compliance certification.
