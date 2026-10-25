# Advice is not decision authority

`IExpenseAdvisor` returns an advisory DTO. It has no store, transition or payment
capability. Its output is displayed alongside the actual business evidence.
`DemoAdvisor` is a deterministic offline simulation that recommends Approved
even when the domain requires more information. This is intentional adversarial
advice, not a claim that a real AI evaluated a receipt. The default path calls no provider.

The optional `OpenAiReceiptExtractor` proposes receipt fields, category, anomaly
notes, summary and a state. It requires explicit live configuration and an owner
disclosure confirmation; requests are audited before transmission. Its bounded
protocol is tested offline with an injected HTTP handler. No live quality is claimed.
It cannot access the store, transitions or payment gateway.
An authenticated human must confirm claim/evidence input, after which the
domain evaluates the captured facts. An extraction assertion is not proof that
a receipt is authentic, unique, authorized or eligible. No prompt or model
output can supply the server's policy, FX provenance, ledger facts or actor role.

The development qualifier separately invokes existing DAA APIs using actual
CSAssay, test and mutation evidence. DAA can ask for context/investigation or
survivor review. It cannot approve an expense, grant release authority or decide
automatic mutation equivalence. No live quality evaluation is claimed.

Structural CSAssay verification alone does not detect arbitrary business logic
errors. For the controlled "approve without receipt" change, the business tests
fail; CSAssay's authoritative verifier carries that failure in JSON/SARIF.
For an unhandled native union case, the compiler is the detector. For a mutable
public domain record setter, the packaged CSAssay analyzer is the detector.
