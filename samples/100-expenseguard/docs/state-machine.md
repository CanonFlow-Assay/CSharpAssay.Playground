# Explicit transitions

```mermaid
stateDiagram-v2
  Draft --> Submitted: claimant submits
  Submitted --> PolicyChecked: captured policy facts
  PolicyChecked --> Approved: within policy
  PolicyChecked --> Rejected: ineligible input
  PolicyChecked --> NeedsMoreInfo: missing evidence
  PolicyChecked --> ManagerApprovalRequired: amount/category limit
  PolicyChecked --> AuditHeld: duplicate suspicion
  NeedsMoreInfo --> Draft: attach evidence
  NeedsMoreInfo --> Submitted: resubmit
  ManagerApprovalRequired --> Approved: manager-only route
  ManagerApprovalRequired --> FinanceApprovalRequired: high-value route
  ManagerApprovalRequired --> Rejected: manager rejects
  FinanceApprovalRequired --> Approved: distinct finance approves
  FinanceApprovalRequired --> Rejected: finance rejects
  Approved --> Reimbursed: worker confirms simulated payment
  Approved --> AuditHeld: auditor holds before payment
```

Auditors may also hold other non-draft, non-reimbursed, non-held states. Held,
rejected and reimbursed claims have no ordinary resubmission/release command.
Audit investigation/recovery is deliberately outside this first version.
Draft and NeedsMoreInfo also support a revision command. Revision clears the
current receipt and assessment and returns Draft; historic snapshots and
authorized receipt downloads remain available. File integrity is checked by
the shell before progression/payment. Audit-chain failure blocks mutations.
Reimbursement is a Finance-queued outbox operation, not a public Reimburse
domain-command endpoint. The worker rechecks Approved under the ledger lock.

All ten state variants appear in the exhaustive `StateNames.Of` and persistence
mapping switches. A controlled missing case must fail compilation. Business
transitions use explicit allowed-state patterns; a complete 10-state approval
and reimbursement matrix is exercised by tests.
