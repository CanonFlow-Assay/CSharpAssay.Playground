# Policy v1

The immutable `Policies.Demo` value is the policy authority for this exercise.
USD is the company base currency. All monetary thresholds apply **after** dated
FX conversion and two-decimal midpoint-away-from-zero rounding. Exactly 25,
50, 100, 200 and 1,000 remain on the lower side of their respective thresholds.
Policy version, evaluation date, normalized amount, applied rate/source, prior
daily meals and duplicate observation remain in the decision snapshot.

Invalid input/date or a personal category rejects the claim. Duplicate suspicion
holds it. Missing FX, receipt or meal itemization asks for information. Finance
threshold requires manager then distinct finance; remaining amount/category
excess requires manager. Otherwise the policy engine approves. Precedence is
intentional: duplicate suspicion wins over a missing receipt.

Daily meals reserve previously assessed, non-rejected claims for that employee
and expense date, including claims awaiting information/review or held. Rejected
and draft claims are excluded. The current claim is excluded from its own facts.
Receipt hash match OR trimmed case-insensitive merchant + original amount and
currency + expense date triggers suspicion; it is not a fraud determination.
Submitted matches also count. If both claims are submitted before either
assessment, both are conservatively held. If one is already approved before
the match arrives, the later claim is held. At most one may clear; exactly one
approval is not promised under concurrent submission.

Hotel `Units` means nights; Software `Units` means months. Limits are per unit,
while the manager/finance thresholds apply to the entire claim. Category limits
route to review rather than acting as absolute reimbursement caps.

The shell's configured `ExpenseGuard:Rates` entries are explicit dated
`ExchangeRate` inputs. No default cross-currency rate is invented. No configured
matching rate means NeedsMoreInfo. `treasury-demo-v1` names a fixture source, not
an external treasury service or a claim of current market rates.
