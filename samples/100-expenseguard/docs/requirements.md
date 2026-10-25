# ExpenseGuard acceptance requirements

This sample implements one-company, single-ledger expense approval. The company
base currency is USD. Inputs are two-decimal USD/EUR/GBP amounts up to 1,000,000.
Money converts using a positive dated rate from the configured source; conversion
rounds to two decimals, midpoint away from zero. Missing or mismatched rates
require more information. No live rates are fetched.

* EG01: Above 25 base-currency units, a receipt is required. Exactly 25 is exempt.
* EG02: Meals above 50 per employee per UTC expense date require an itemized
  receipt and manager approval. Count previously assessed non-rejected claims.
* EG03: Transport above 100/claim, hotel above 200/night and software above
  100/month require manager approval. `Units` is nights or subscription months;
  default is one. Category excess is reviewable, not automatic rejection.
* EG04: Above 200 requires manager approval. Above 1,000 requires manager then
  distinct finance approval. Neither approver may be the claimant.
* EG05: Same receipt hash OR normalized merchant + original money + expense date
  on another non-draft/non-rejected claim creates an audit hold. Concurrent
  submissions must not both clear duplicate detection.
* EG06: Future dates and dates older than 90 days are rejected, as are personal
  expenses and invalid amounts/categories/units. Missing FX is never approval.
* EG07: Only approved claims may be reimbursed. Finance queues reimbursement;
  a worker uses a stable idempotency key. Hold-before-send cancels pending work.
* EG08: Commands require server-authenticated identities, expected versions and
  valid states. Missing-info claims may attach evidence and resubmit. Rejected,
  reimbursed and held claims cannot silently re-enter approval.
* EG09: Every accepted change appends an actor/time/snapshot hash-chain receipt
  atomically with state. Hash chains detect changes relative to the stored chain;
  they are not signatures or protection against a privileged database rewriter.
* EG10: Advisory output has no authority to transition state. The offline advisor
  deliberately recommends approval to demonstrate this boundary. No live AI
  extraction, notification delivery or real payment is claimed.
* EG11: The evidence viewer separates business outcome, behavioral checks,
  structural CSAssay verdict and advisory output. Missing/stale build evidence
  must not display Pass. Native unions and runtime remain preview-qualified.
* EG12: Authenticated claimants can upload bounded PDF/PNG/JPEG receipt bytes.
  The server computes SHA-256 and stores an opaque claim-bound reference outside
  the web root. Read/download and approval/payment advancement verify stored
  bytes. Historical receipts remain accessible to authorized claim reviewers.
  Corrupt audit chains block all ledger writes and payment processing. Draft
  revisions clear current receipt and policy, preserving prior audit snapshots.
* EG13: Optional OpenAI receipt extraction is disabled by default, requires an
  explicit owner disclosure confirmation and a bounded per-process budget, and
  returns only a locally validated proposal. Record the request before sending.
  Refusal, restriction, incomplete/oversized/invalid output cannot approve or
  modify a claim; no automatic retry or provider fallback is allowed.
* EG14: Application-test evidence binds Domain, Application, Infrastructure,
  API and Worker assemblies plus declared verification/build configuration.
  A changed/missing component invalidates application freshness. Deployment
  secrets/environment/provider configuration remain explicitly unqualified.
* EG15: After handing a stored upload to a database command, retain its bytes
  even when an exception/cancellation makes commit outcome uncertain. Orphan
  reconciliation must inspect current AND historical references independently.
* EG16: A failed payment item cannot starve another due item. Persist bounded
  retry scheduling and an explicit ManualReview outbox state, preserving the
  stable idempotency key. Receipt/audit integrity failures never send payment.
* EG17: Default/null-backed receipt, state, command, policy and rate unions
  cannot approve or transition; return explicit invalid-evidence outcomes.
* EG18: External assertions alone cannot authorize automatic approval. Require
  server-verified stored bytes and reassessment for an attached assertion.

CSAssay's seven admitted checks cover selected nullability, immutable-carrier
and suppression rules. Compiler exhaustiveness and business tests are separate
gates. A missing-receipt shortcut must be killed by tests; CSAssay does not infer
that arbitrary business rule from source. Determinism is tested for captured
inputs, not proven universally.
