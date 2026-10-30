# Portable playground convergence — phase 1

The CSharpAssay Playground is a reference for *evidence shape*, not a source
template for other languages. Each assay keeps its own policy, receipt, toolchain
and verdict authority. This phase creates a common map before adding samples.

## Questions answered

| Question | Decision |
| --- | --- |
| Must every playground contain the same five examples? | No. Reuse a boundary only when its language and current application surface make it meaningful. |
| Does a shared example make verifier verdicts comparable? | No. A local sample test, a static assay receipt and application acceptance remain distinct evidence. |
| Should policy configuration become a general rules DSL? | No. Keep reviewed assay policy formats authoritative; use a tiny DSL only for a bounded application domain. |
| Does source generation transfer across languages? | Only as an explicit compiler-supported serialization boundary, never by adding a framework or dependency to achieve superficial parity. |

## Reusable boundaries

1. **Configuration firewall** — environment/file/framework input becomes a
   validated immutable setting or an explicit rejection; no silent defaults.
2. **Versioned ingress** — HTTP/event/receipt adapters establish trust, version
   and idempotency facts; pure code routes an accepted fact to a closed outcome.
3. **Small rules language** — parse application-owned text to an immutable AST;
   keep parsing failures separate from business decisions.
4. **Time decision** — isolate host-clock/time-zone facts; reject skipped local
   times and require an explicit choice for ambiguous ones.
5. **Generated serialization boundary** — validate the wire contract before
   compiler-generated projection, and keep domain range/policy decisions pure.

## Family map and next safe increments

| Assay | Existing useful shape | Next candidate | Deliberate exclusion |
| --- | --- | --- | --- |
| TypeScript / JavaScript | Reservation decoder, immutable union domain, real mutation harness | configuration firewall for application intake | treating TypeScript compilation as runtime proof |
| F# | Domain/Application/Shell M5 and closed outcomes | versioned transport facts in a new bounded shell test | changing FsAssay's Inconclusive authority |
| Python / Rust / Swift / Kotlin | ExpenseGuard functional core, requirements, retained mutation evidence | key/value settings boundary before another domain feature | claiming lexical checks prove semantics |
| Dart | M1 Flutter reference order app, sealed outcomes and pure core | document only until its human-gate workflow authorizes a new increment | a framework, analyzer or state-management expansion |
| HTML5 / STE | browser-local preview and explicit non-authoritative investigations | schema/manifest validation at the browser/CLI boundary | browser preview as a verifier or server simulation |
| Security | declared input manifest, versioned receipt and offline advice | receipt ingress/freshness controls only | webhooks, ambient configuration or live scanner claims |

## Phase sequence

Phase 1 is this map, linked from every family playground. Phase 2 adds one
small, language-native configuration or ingress boundary only where the listed
candidate can be run with existing pinned tooling. Phase 3 considers application
rules/time/serialization only after Phase 2 has retained positive, negative and
freshness evidence. Every phase must state its nonclaims and must not alter
existing verifier rule admission, package publication or release authority.

The per-repository `CONVERGENCE.md` files record the local command and limit for
that work. They are a roadmap, not a cross-language qualification or approval.
