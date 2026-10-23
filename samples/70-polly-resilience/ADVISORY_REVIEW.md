# Agent advisory review

These are agent judgments pending independent human review. Findings remain in
the native engine receipt, with their existing authority and severity. No rule is
suppressed, promoted, or removed by this review.

The current shared workflow domain produces six advisories, also visible in the
refined Playground gate:

| Rule | Count | Review |
| --- | --- | --- |
| CSAD0002 | 3 | `ModuleEvidence` records independent requiredness and authority metadata. `RiskObservation` records independent change and coverage observations. `TraceContract` records independent fallback and side-effect policies. These flags are protocol observations/configuration; they do not select exclusive business outcomes. Retained for further API review. |
| CSAF0002 | 3 | Contract comparison explicitly accumulates removed/changed members, new required members, and outcome differences, then sorts deterministically. Pipeline alternatives may improve readability; the current differences remain directly testable. |

The Polly consumer adds three advisories:

| Rule | Count | Review |
| --- | --- | --- |
| CSAD0002 | 1 | `FetchScenario` contains independent before/during cancellation fixture controls. Executed registrations use explicit combinations. A future scenario union may make invalid fixture combinations impossible. |
| CSAE0002 | 2 | The controlled execution driver throws when its invocation bound is exceeded or no controlled progress is possible. These are fixture integrity failures, retained as failed evidence rather than projected as successful business outcomes. Domain transport decisions themselves use native outcomes. |

The six registered mutations target permanent-failure retry classification,
retry budget, forwarded cancellation, successful content, fallback labeling and
an equivalent-looking retry predicate. Requirements are registered before these
edits. Expected outcomes are five killed and one survivor; actual results are
recorded separately by execution. A compiler failure is invalid, and timeout is
not a kill. The retry predicate survivor remains in the corpus and denominator.
This review does not establish automatic equivalence or production resilience.

The first executed corruption edit replaced the content with a constant and
failed the existing IDE0059 build rule because its pattern variable became
unused. That invocation remains incomplete with an `Invalid` record. The revised
registered edit appends a corruption marker to the original content, preserving
the same fault intent while allowing assertions to execute. No style rule was
disabled and the invalid execution was not counted as a kill.
