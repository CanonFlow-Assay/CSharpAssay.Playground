# Shape v0.1 LLM-as-Judge advisory

Evaluation date: 2026-08-02  
Reference head reviewed: `96de74ad7ee99ef345595fcd49b6c958ee763f25`  
Builder range reviewed: `b1e4a17..948531b`  
Independent-evaluation range reviewed: `948531b..96de74a`  
Contract-completion follow-up reviewed: `519aa46..96de74a`  
Normative contract: CSharpAssay commit `8ef9a70b1b354130b52c1eff8e4b57afef2c1700`, `docs/FUNCTIONAL-CSHARP-PROFILE.md`  
Contract SHA-256: `54be4be99328e9f56e83358075f0cc112989bbc16e36c46527e74c32ea5771b2`

## Verdict

**advisory-accept**

This is an advisory acceptance of the bounded Shape v0.1 reference and its
completed independent evaluation. LLM judgment is non-authoritative and
cannot override compiler, test, CSharpAssay, provenance, hash, or human-review
evidence.

The accepted baseline evidence is internally consistent: Release build was
clean, 16 tests passed, all six projects loaded, all seven admitted rules
completed, no blocking finding was present, JSON and SARIF were byte-identical
across the recorded repeated runs, and CSharpAssay reported an authoritative
Pass. The three remaining advisories are retained honestly: `CSAN0003` once
and `CSAN0004` twice. See `RED-TEAM.md`, `red-team-results.json`,
`expected-evidence.json`, and `ADJUDICATION.md` at `96de74a`.

## Resolution of the initial advisory rejection

The initial judgment at head `519aa46` was `advisory-reject` because section 11
of the normative contract required two mutation events that had no recorded
run: a missing required project and direct Domain serialization or transport
leakage. That was a valid incomplete-evidence blocker at that point; it did not
reverse the already passing baseline.

Commit `96de74a` resolves that blocker by preserving both omitted probes in
`RED-TEAM.md` and `red-team-results.json`:

- Adding nonexistent `src/Shape.Absent/Shape.Absent.csproj` to
  `boundaries.coreProjects` produced exit 0, `pass`, and
  `authoritative:true`; the path was omitted from the six-project inventory.
- Adding `JsonPropertyName` directly to `Shape.Domain.OrderId.Value` produced a
  clean build, six passing architecture tests, exit 0, `pass`,
  `authoritative:true`, and no serialization finding.

Both are explicit coverage gaps. Contract section 11 permits each mutation to
fail deterministically **or** be recorded as an explicit coverage gap, so the
independent event is now complete. There is no remaining absolute blocker in
the evidence reviewed. Acceptance does not convert either gap into an enforced
property or request a product fix for this v0.1 milestone.

## Scores

Scores use 1 as poor and 10 as strong.

| Dimension | Score | Advisory assessment |
| --- | ---: | --- |
| Contract fidelity | 9 | The reference follows the declared package, project, representation, and evidence boundaries, and every contract-mandated mutation is now rejected or recorded as an explicit gap. |
| Behavior adequacy | 7 | The builder behavior has exact five/six-line boundary coverage and verifies effect counts and error transport mapping. Some representation and response paths remain inspection-backed rather than directly characterized. |
| Boundary clarity | 9 | Core/shell ownership, analyzer scope, port ownership, and non-claims are unusually explicit and supported by project-graph tests. |
| Builder scope discipline | 10 | `b1e4a17..948531b` adds only one bounded line-limit rule, its closed error, API mapping, two tests, and synchronized counts. |
| Finding honesty | 9 | All three advisories remain visible and adjudicated as shell-boundary observations; none is presented as fixed or core-clean evidence. |
| AI-agent safety | 8 | `AGENTS.md` uses real 0.1.2 commands and stop conditions. Source binding was repaired, but finding expectations are count-based rather than fingerprint-bound. |
| Overclaim control | 9 | README, contract, adjudication, and red-team report consistently deny universal purity, business correctness, and template/product qualification. |
| Known-gap disclosure | 10 | Environment failures, stale-evidence false-pass history, hidden-clock, absent-core-path, and serialization gaps are preserved rather than normalized away. |

## Evidence-grounded assessment

### Contract fidelity and boundary clarity

- The contract at `8ef9a70`, sections 1, 6, 7, and 12, limits Shape v0.1 to a
  hand-authored reference using published CSharpAssay 0.1.2. The sample README,
  policy, project files, and architecture tests reflect that boundary.
- `.csassay.json` scopes Domain and Application as core, API, Infrastructure,
  and tests as shell, requires exactly seven admitted rules, and requires 10
  behavior plus 6 architecture tests.
- `ShapeArchitectureTests.cs` checks the production dependency graph, direct
  private analyzer references only in core, the two approved closed-case sets,
  and Infrastructure ownership of the Application port.
- There is no EF, messaging, template, or CSharpAssay product expansion in
  `b1e4a17..96de74a`. No analyzer, rule, CLI, package, policy semantics, or
  release behavior is changed.

### Builder change

The diff `b1e4a17..948531b` is a good bounded agent event. It introduces a
five-line maximum in `OrderDecisions`, represents excess lines as the closed
`OrderError.TooManyLines` case, maps that case at the API boundary, and adds
tests for the accepted boundary and rejected boundary. The rejection test also
proves zero storage effects and a stable `too_many_order_lines` response. Test
minimums and expected totals move from 8 to 10 without weakening policy.

### Deterministic evidence and independent event

- `RED-TEAM.md` records an authoritative baseline Pass with 16/16 tests, three
  advisories, no missing evidence, no tool failure, and hashes
  `74198722...e990e7a` for JSON and `dad047c9...5a44f` for SARIF.
- Required-test zero execution produced `toolFailure` and
  `authoritative:false`; compiler incompleteness produced `inconclusive`,
  `authoritative:false`, and seven incomplete required-rule outcomes.
- Analyzer-disable, `NoWarn`, and unreviewed warning-demotion probes were
  rejected. Reviewed rollback is explicitly labeled enforcement-disabled, not
  a Pass.
- The forbidden Domain project reference failed the intended architecture
  test.
- The missing-declared-core-project probe did not fail: published 0.1.2 omitted
  the nonexistent path from its inventory and still returned an authoritative
  Pass. This is now explicitly recorded as a policy-boundary coverage gap; a
  consumer must not treat `coreProjects` as exhaustive required-project
  inventory.
- The original assertion accepted stale source evidence. Commit `519aa46`
  repairs source binding in `eng/assert-functional-shape.py`; the independent
  retest proved clean evidence succeeds while both an edited source file and a
  newly added source file fail. The evidence assertion source-binding repair
  was therefore implemented and retested, without claiming broader policy or
  project-file freshness coverage.
- A direct `DateTime.Now` read in Domain is undetected: build, all six
  architecture tests, and CSharpAssay verification still pass authoritatively
  with the same three advisories. This is an explicit coverage gap and means
  the evidence must not be interpreted as universal Domain-purity proof.
- A direct `System.Text.Json.Serialization.JsonPropertyName` concern in Domain
  is also undetected: build, architecture tests, and verification pass with no
  new finding. This is an explicit serialization-boundary coverage gap,
  consistent with the contract's exclusion of serialization qualification.

## Dissent and uncertainty

1. `Result` and `Option` constructors visibly reject null, and architecture
   tests prove the case inventory, but behavior tests do not directly exercise
   `Success(null)`, `Failure(null)`, or `Some(null)`. The contract assigns the
   representation requirements to tests and review; a human should decide
   whether source inspection plus current tests is sufficient for v0.1.
2. The API failure response is characterized, including the builder change,
   but the successful `Created<OrderResponse>` mapping is not directly tested.
3. `OrderId` and `AcceptedOrder` have public construction paths that can create
   values outside the decision workflow. The contract's phrase
   "constructor-complete values" may permit this, but the product thesis may
   lead readers to infer stronger always-valid-domain-state guarantees.
4. `expected-evidence.json` binds finding IDs and counts, not fingerprints,
   paths, dispositions, or messages. A different finding with the same rule
   count could satisfy the machine assertion even though its human
   adjudication changed. Source binding reduces stale-source risk but does not
   remove this same-count substitution risk.
5. The repaired source assertion binds non-generated `src/**/*.cs` and
   `tests/**/*.cs`. It does not independently bind current project files,
   policy, or documentation to previously generated evidence when the
   assertion is invoked alone. The full runner regenerates evidence, so this
   is a defense-in-depth question rather than a demonstrated false pass in the
   accepted workflow.

## Exact human review questions

1. Does Shape v0.1 require direct null-payload tests for all approved
   `Result`/`Option` cases before the representation is accepted?
2. Must the success transport mapping receive a behavior test, or is the
   current failure-path characterization sufficient for this bounded event?
3. Is public construction of potentially invalid `OrderId` and
   `AcceptedOrder` states intentional for v0.1, and should that limitation be
   stated explicitly?
4. Should expected findings be bound by fingerprint, path, and disposition so
   a same-rule/same-count replacement forces new human adjudication?
5. Is source-only freshness sufficient for the standalone assertion, or must
   it also bind project, policy, and expected-evidence files?
6. Are the three enforcement gaps—`DateTime.Now`, nonexistent declared core
   paths, and Domain serialization attributes—prominent enough that a human or
   AI consumer will not infer those properties from `authoritative:true`?

## Bounded recommendation

The bounded reference and completed evaluation are suitable for human review.
Do not alter the builder or CSharpAssay product merely to make the three
recorded v0.1 coverage gaps disappear. Preserve all gaps, the three advisory
findings, and the distinction between CSharpAssay evidence authority and rule
coverage. The remaining questions are discretionary human review decisions,
not authority failures.
