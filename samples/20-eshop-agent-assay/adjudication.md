# Baseline adjudication

| Evidence | Initial judgment | Required proof |
| --- | --- | --- |
| `CSAN0001` compilation nullable context disabled | true project-policy gap | nullable-enabled compiler probe and migration warning inventory |
| `Order.BuyerId` / constructor `buyerId` | contextual domain-state finding | order lifecycle, EF mapping, and tests around payment verification |
| `Order.PaymentId` / constructor `paymentMethodId` | contextual domain-state finding | order lifecycle, EF mapping, and tests around payment verification |
| constructor defaults `buyerId = null`, `paymentMethodId = null` | true null-state introduction, not mechanically fixable | explicit pending/verified state design and persistence qualification |
| `Entity` equality `object.Equals(..., null)` | detector false positive | 0.1.1 null-observation regression specimen |
| `ValueObject` equality `ReferenceEquals(..., null)` | detector false positive | 0.1.1 null-observation regression specimen |

The 14 raw findings represent three investigation areas, not fourteen proposed
edits. This ledger must be revised when executable framework evidence changes a
judgment.
