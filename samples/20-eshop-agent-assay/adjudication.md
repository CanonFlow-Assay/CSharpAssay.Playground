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

## Candidate evidence closure

| Evidence | Candidate result | Interpretation |
| --- | --- | --- |
| Base revision | `dotnet/eShop@9b4f9434f46fdc5c1a6e9e936af2868340cdbc48` | immutable upstream baseline |
| Candidate revision | `CanonFlow-Assay/eShop@5e92c725c6b13ff2c9cda6de58228ff04c1fb73f` | reviewed branch only; fork `main` unchanged |
| Ordering TRX | 50 passed, 0 failed, 0 skipped | focused unit and EF runtime-model suite passed |
| Domain `[Required]` inventory | six at base, zero at candidate | persistence annotations intentionally removed from domain source |
| EF requiredness | four named runtime-model tests passed | required owned Address and affected scalar/relationship mappings remain explicit in this configured model |
| Candidate Assay | provisional Fail; 9 findings, 0 missing, 0 tool failures | no remaining Assay finding is claimed fixed by this patch |

The requiredness correspondence checked by the passing runtime-model tests is:

| Removed domain attribute | Candidate EF runtime-model assertion |
| --- | --- |
| `Order.Address` | owned navigation, ownership, and dependent are required |
| `OrderItem.ProductName` | mapped property is non-nullable |
| `Buyer.IdentityGuid` | mapped property is non-nullable |
| `PaymentMethod._alias` | mapped field is non-nullable |
| `PaymentMethod._cardNumber` | mapped field is non-nullable |
| `PaymentMethod._cardHolderName` | mapped field is non-nullable |

The six removed attributes are a deliberate DataAnnotations reflection-metadata
change. Consumers that inspect `[Required]` no longer observe those members.
The executed EF model proves configured metadata, not a database round trip.
Migration generation, snapshot drift, deployed-schema compatibility,
serialization/validation consumers, alternate model-building paths, and an
upstream contribution decision remain outside the current proof.
