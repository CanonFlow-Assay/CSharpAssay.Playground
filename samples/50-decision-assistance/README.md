# Decision assistance slice 1

This .NET 11 RC1 sample demonstrates useful investigation feedback without allowing model advice to change a verification result. Its domain uses native unions, immutable records, and explicit Present/Absent/Unknown observations. Effects live in the CLI shell.

## Run offline

After the repository's normal package preparation and Release build:

```sh
python3 eng/run-decision-assistance.py
```

The harness copies the amount policy to a temporary isolated project, verifies the exact SDK, and changes exactly one `amount > 0` expression to `amount >= 0`. It compiles both source versions, runs the declared weak and strong probes, and checks their exact test counts, failure counts and exit codes. No package feeds are used by these temporary projects.

The original passes positive/negative tests and the additional zero test. The mutant survives the weak suite but fails the zero test. The source mutation is real, while the example is synthetic and deliberately bounded. This is not a general mutation engine or production mutation score.

Generated experiment and advice records are under `evidence/generated/decision-assistance/`. They bind the original, mutant and probe source hashes. Coverage is Unknown: this slice runs no coverage instrumentation. The assertion-gap observation describes the known weak fixture, not arbitrary source analysis.

The deterministic baseline recommends inspecting the missing boundary assertion and stays visible even when optional provider advice is unavailable. Advice is recorded separately with an Inconclusive verification status because this experiment does not itself perform authoritative Assay verification. The repository's existing refined verification gate covers the new domain and its registered tests.

## Optional Jev suggestion

Only when explicitly requested, with a user-provided `TYPESAFE_API_KEY` in the environment:

```sh
dotnet run --project samples/50-decision-assistance/src/DecisionAssist.Cli --no-build --no-restore -c Release -- evidence/generated/decision-assistance/experiment.json --jev
```

Add `--firm` for an attention message; it works in offline mode too. Concise, coaching and firm text share the same action and authority; none insults the developer or suppresses a survivor.

The adapter sends only synthetic experiment metadata to the [official TypeSafe endpoint](https://docs.typesafe.ai/introduction/quickstart). It has a five-second deadline, disables redirects, limits response bytes and accepts only four registered investigations. It makes one attempt, with no retries or provider fallback. Missing credentials, transport errors, rate limits and malformed responses yield unavailable advice. Refusal and unknown actions require review. Server error bodies and keys are not printed.

The `jev-latest` alias and response fixtures are an integration preview. The requested model alias is recorded; its resolved revision remains NotObserved and live advice is not reproducibly qualified. Offline contract tests do not establish live API compatibility, model quality or calibrated confidence. No confidence score is invented or used as a gate. CI never uses this adapter's live path.

## Evidence boundary

The original Assay verdict stays independent of advice. A provider cannot return Pass, discard a mutant as equivalent, choose shell commands, or waive required tests. Provider suggestions remain labeled unqualified. Compare them with deterministic and human triage on held-out cases before extending their role.

OpenAI Decisions, Clef, local Laya, trained classifiers, arbitrary source ingestion and generated test patches remain later slices. Existing project gates and rule admission are unchanged.
