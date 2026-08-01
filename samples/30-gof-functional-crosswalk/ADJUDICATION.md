# GoF crosswalk adjudication

Human review owns every conclusion below. CSharpAssay 0.1.1 reports design
signals; it does not decide that a Gang of Four pattern is wrong.

## Exact classic findings

| Rule | Location and subject | Tool disposition | Human disposition |
| --- | --- | --- | --- |
| `CSAF0001` | `Strategy.cs:3`, `IDiscountStrategy` | Info; heuristic; advise | True shape observation. A delegate is sufficient for this dependency-free calculation. Keep the interface when named DI registrations, identity, dependencies, discovery, or public versioning matter. |
| `CSAF0001` | `Builder.cs:7`, `MealOrderBuilder` | Info; heuristic; advise | True shape observation. An immutable draft and functions are smaller for this three-step construction. Keep the builder for staged validation, complex protocols, framework construction, or a public fluent API. |
| `CSAF0001` | `Visitor.cs:3`, `IShippingQuoteVisitor` | Info; heuristic; advise | True shape observation. Pattern matching is clear because the shipment cases are closed in this sample. Keep Visitor for stable hierarchies with frequently added operations, double dispatch, or framework contracts. |
| `CSAD0002` | `State.cs:3`, `ApprovalWorkflow` (`IsApproved`, `IsSubmitted`) | Info; heuristic; advise | True shape observation. These flags encode mutually constrained states, so one closed state removes invalid combinations. Independent capabilities or framework-bound flags would be valid counterexamples. |
| `CSAI0003` | `Composite.cs:15`, `MenuGroup.Children` | Info; contextual; advise | True ownership risk. Callers can mutate the group through its public `List<IMenuNode>`. The refined tree closes that path; a UI or document model with intentional identity and mutation may retain it. |

Counts are exactly `CSAF0001 ×3`, `CSAD0002 ×1`, and `CSAI0003 ×1`.
There are no suppressions. The implementations are conventional examples, not
degraded specimens manufactured to trigger diagnostics.

## Refined result

The refined project reports zero findings from CSharpAssay 0.1.1. Its
authoritative report has one loaded project, ten passed tests, zero failed or
skipped tests, zero missing required evidence, and zero tool failures.

This is an authoritative pass for the admitted stable compat lane only. The
crosswalk findings are prototype/contextual advisories, so their absence is
recorded observation rather than blocking proof. `CSAU0001`/`CSAU0002` are not
used as authoritative union evidence, and the native-union rules are not
configured for this .NET 10 compat sample.

## Behavior evidence

The shared suite compares both lanes for:

- two seasonal-discount totals;
- one completed order with ordered extras;
- two parcel and two freight quotes;
- successful approval and rejected early approval;
- one nested composite total.

That is ten passing cases. It does not establish a general semantic equivalence
proof or cover side effects, performance, threading, serialization, framework
integration, or all exceptional inputs.

## Determinism and failure posture

`eng/run-gof-crosswalk.sh` produces each JSON and SARIF report twice and compares
the bytes. `eng/assert-gof-crosswalk.py` then checks the package baseline, report
authority, project and test totals, complete required-rule outcomes, exact
finding subjects, JSON/SARIF agreement, and absence of failures or missing
required evidence. A difference fails CI.

## Remaining risk

The functional representations trade named pattern objects and extension seams
for smaller values and functions. That is favorable only while the demonstrated
business boundary stays small. Human review must assess discoverability,
dependency injection, public API evolution, diagnostics, allocations, and
framework constraints before applying the same change elsewhere.
