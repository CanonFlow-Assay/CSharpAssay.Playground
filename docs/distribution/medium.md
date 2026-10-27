# Medium practical tutorial — DRAFT

Audience: C# developers trying the native development line. Claim: C01/C05, not complete functional correctness. Evidence: [recorded examples](../examples/catalogue.json) and [canonical adoption](https://github.com/CanonFlow-Assay/CSharpAssay/blob/717d5d2daff548b580c651c7bc1247964bc826c2/docs/adoption.md). Action: understand and reproduce one finding. Approval: DRAFT; manifest records exact content. Limits: local-feed development packages, exact SDK and selected detectors. AI-assisted draft; editor/canonical-link/AI policy must be checked for a chosen publication before submission.

## Understand one C# finding, then run the real checks

Start with the recorded public-record setter example. A public setter allows a caller to replace the value after construction. If the chosen domain policy expects immutable carriers, that is a concrete code shape to inspect. Select the after variant and read the limit: init restricts assignment, but does not freeze a referenced collection.

This is not an argument for moving all framework code into immutable records. Serializer and persistence contracts may legitimately remain in a declared shell. Domain behavior should not move there merely to obtain a passing result.

### Prepare the actual lane

The current inspected native build is 0.2.0-rc.1 with exact SDK `11.0.100-rc.2.26504.105`, net11.0 and preview language settings. It is not the historical public 0.1.x package line. Review current vendor support before adopting this preserved lane; final-SDK qualification is separate.

Follow the canonical adoption guide to build, qualify and install matching local packages. Do not assume this development version can be installed from NuGet.org. For a clean disposable Playground with the qualified engine as its sibling:

```bash
./eng/prepare-assay.sh ../CSharpAssay --qualified-bootstrap-locks
export ASSAY_RUNNER="$PWD/../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll"
./eng/run-expenseguard.sh --output "$PWD/evidence/generated/expenseguard/tutorial"
```

The engine must already contain its successful exact-source qualification. Docker/PostgreSQL is required for ExpenseGuard integration, and restore may download dependencies. Choose a fresh output path. This is not a one-command install on an unprepared machine.

### Inspect the outcome

Read `verify.md` or `verify.html`, then the complete `verify.json` and SARIF as needed. Identify the requested scope, verification mode, authority, required tests, missing evidence and execution failures. A provisional check is analysis only. A completed required-test failure and an incomplete run mean different things.

To understand a specific diagnostic, run `./eng/tooling packed-assay -- explain CSAI0001` after preparation. It uses the package-byte-verified executable rather than trusting a same-version global resolver entry. The examples and limitations are bundled offline. The current rule explorer derives its examples from those canonical guides and records actual tool observations; it does not implement a second analyzer in JavaScript.

### Try a real counterexample

Follow the first-change tutorial to alter only the receipt threshold from 25 to 30 in a disposable copy. Keep the tests and boundaries unchanged. The named business assertions should expose the requirement disagreement. Restore only the deliberate edit and rerun into a fresh directory, preserving the failed evidence.

Credit the detector correctly: the business tests check the threshold. The compiler handles native-union exhaustiveness. CSAU0004 catch-all advice is a separate prototype. An analyzer finding does not establish all payment, security or concurrency behavior.

### Know what the browser result means

The explorer shows recorded variants with input/evidence identities. Editing text marks it Unverified and blocks exporting it as recorded execution. Use local reproduction for your own code. A historical badge describes its recorded source, not whatever happens to be on main later.

## Fair comparison article outline

Working title: “Compiler, tests, analyzers and evidence: which question does each answer?”

Use named versions/editions and exact enabled configurations on the same fixture/environment. Start with overlapping capability rather than inventing exclusive wins. Include good examples, false positives, unsupported toolchains and setup costs. Keep code checks, business assertions, mutation testing and report authority as different dimensions. Record actual commands/output before publishing any comparative result. No competitor benchmark was executed for this pack.

Use a text diagram unless the target renderer's Mermaid support has actually been checked. Possible text: `source -> compiler + selected policy checks + required tests -> scoped evidence`. A future article for ITNEXT or Better Programming is an editorial proposal, not accepted publication.
