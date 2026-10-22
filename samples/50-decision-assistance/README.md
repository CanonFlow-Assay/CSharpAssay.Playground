# Decision assistance: typed provider mappings

This .NET 11 RC1 sample demonstrates useful investigation feedback without allowing model advice to change a verification result. Its domain uses native unions, immutable records, and explicit Present/Absent/Unknown observations. Effects live in the CLI shell.

## Start as a developer or agent

Run `./eng/try-decision-assistance.sh ../CSharpAssay` for package preparation, build, primary tests, the mutation demonstration, and the offline scenario evaluation. Read the [getting-started guide](../../docs/decision-assist-getting-started.md) and root `AGENTS.md` for the complete workflow. Live comparison requires explicit `--live` and a request budget; the default workflow makes no provider calls.

Insufficient context now produces local review before inference: if neither observed absence supports an investigation and both observations are not present, the model is not called. Provider `human_review` answers use review status consistently across Jev and the other mappings. Verification remains independent.

## Run offline

After the repository's normal package preparation and Release build:

```sh
./eng/tooling run-decision-assistance
```

The harness copies the amount policy to temporary isolated projects, verifies the exact SDK, and executes the six cases declared in [the mutation corpus](corpus/mutation-cases.json). Each replacement targets exactly one `amount > 0` expression. It checks compilation diagnostics, declared test and policy-call counts, failure counts, exit codes, and the resulting deterministic advice. No package feeds are used by these temporary projects.

The original passes positive/negative tests and the additional zero test. The mutant survives the weak suite but fails the zero test. The source mutation is real, while the example is synthetic and deliberately bounded. This is not a general mutation engine or production mutation score.

Generated experiment and advice records are under `evidence/generated/decision-assistance/`. The aggregate `mutation-cases.json` must have `executionStatus: completed`. Per-case `cases/<id>/experiment.json` and `advice.json` bind the original, mutant, probe, manifest, harness, build inputs, and CLI identities. The original `experiment.json` and advice files remain aliases for the zero-boundary case.

Reachability is observed with an explicit direct-call counter in the probe's submission gate. It establishes whether that fixture invoked the policy; it is not instrumented line or branch coverage. Assertion observations describe the declared fixture suites, not analysis of arbitrary source. A timeout, crash, restore error, unexpected diagnostic, inconsistent count, or mismatched expectation fails the corpus run and replaces the aggregate success status with `failed`; completed earlier case records do not make that run complete.

## Executed case review

These cases are **agent-reviewed against the explicit requirements in the manifest and await human adjudication**. They are authored fixtures with real compilation and execution. The separate ten-case `scenarios.json` still contains hypothetical observations and draft routing labels. Neither set qualifies provider quality or establishes production test sufficiency.

| Case | Observed workflow | Expected next investigation |
|---|---|---|
| Zero-boundary gap | `>= 0` survives positive/negative probes; adding zero detects it while the original passes | Inspect assertion |
| Unreached policy | Disabled submissions make zero policy calls; enabled zero probe exposes `>= 0` | Inspect reachability |
| Integer-equivalent candidate | `>= 1` survives the suite including zero | Review contract; retain unresolved survivor |
| Reversed comparison | `< 0` fails selected positive/negative assertions | Local review; no survivor inference |
| Invalid expression | `amount >` fails with the pinned C# syntax diagnostic after a passing baseline | Local review; no mutant tests or inference |
| Failing original baseline | A deliberately incorrect zero expectation fails on the original | Stop before building the mutant; fix baseline |

For Int32, `amount > 0` and `amount >= 1` denote the same values without arithmetic overflow. That reasoning is recorded for contract review; passing probes alone do not prove equivalence. The harness never excludes a survivor or assigns an automatic equivalent label. A demonstrated repair is an additional observed probe, and leaves the original selected-suite survivor record intact.

The probe's `broken` suite is deliberately false; its expected baseline failure is checked as a failure boundary, not counted as a passing product test. Invalid mutant compilation is similarly expected only for the declared syntax diagnostic. Unexpected failures stop the harness rather than becoming an accepted case outcome.

Failure-boundary tests run with `./eng/tooling test-mutation-cases`. The standard try command and offline CI include these tests and the complete executed corpus.

The deterministic baseline recommends inspecting the missing boundary assertion and stays visible even when optional provider advice is unavailable. Advice is recorded separately with an Inconclusive verification status because this experiment does not itself perform authoritative Assay verification. The repository's existing refined verification gate covers the new domain and its registered tests.

## Choose a provider

Default execution remains offline. Humans, editor agents and CI can consume the same JSON CLI output. List names without reading credentials or contacting providers:

```sh
dotnet run --project samples/50-decision-assistance/src/DecisionAssist.Cli --no-build --no-restore -c Release -- --list-providers
```

Select exactly one provider explicitly; no credential detection, automatic provider switching, retries or confidence thresholds:

```sh
dotnet run --project samples/50-decision-assistance/src/DecisionAssist.Cli --no-build --no-restore -c Release -- evidence/generated/decision-assistance/experiment.json --provider=openai --firm
```

`--jev` remains a compatibility alias for `--provider=jev`. `--firm` adds an attention message with the same authority. Credentials are environment variables; never put keys in tracked files or CLI arguments. The CLI reads no credential files.

| Provider | Protocol | Configuration |
|---|---|---|
| `openai` | Decisions: `input`, questions array, `choices`; answers array keyed by `name` | `OPENAI_API_KEY`; optional `ASSAY_OPENAI_MODEL` (default `gpt-6-luna`) |
| `jev` | SystemOne: `state`, questions map, `criteria`; answers map | `TYPESAFE_API_KEY`; optional `ASSAY_JEV_MODEL` (default `jev-latest`) |
| `clef`, `clef-flash` | SystemOne payload plus Cloudflare REST response envelope | `CLOUDFLARE_AUTH_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` |
| `clef-local` | SystemOne at the configured local or HTTPS server | `ASSAY_CLEF_ENDPOINT`, `ASSAY_CLEF_MODEL`, optional `ASSAY_CLEF_KEY`; defaults `http://127.0.0.1:30000/v1/systemone`, `Cloudflare/clef-flash` |
| `laya` | SystemOne at the deployed Laya server | `ASSAY_LAYA_ENDPOINT`, `ASSAY_LAYA_MODEL`, optional `LAYA_API_KEY`; defaults `http://127.0.0.1:8000/v1/systemone`, `english` |
| `qwen` | Ollama structured JSON; generated labels, no invented confidence | `ASSAY_QWEN_ENDPOINT`, `ASSAY_QWEN_MODEL`, optional `ASSAY_QWEN_KEY`; defaults `http://127.0.0.1:11434/api/chat`, `qwen3.5:0.8b` |
| `chat` | OpenAI-compatible Chat Completions with strict JSON Schema | `ASSAY_CHAT_MODEL` required, `ASSAY_CHAT_ENDPOINT` (default `http://127.0.0.1:8080/v1/chat/completions`), optional `ASSAY_CHAT_KEY` |
| `setfit` | Fixed trained classifier: request `{model,text}`, response `{label}` | `ASSAY_SETFIT_MODEL` required, `ASSAY_SETFIT_ENDPOINT` (default `http://127.0.0.1:8001/predict`), optional `ASSAY_SETFIT_KEY` |
| `custom` | Versioned normalized advisor service, suitable for a human review queue or agent service | `ASSAY_CUSTOM_MODEL`, `ASSAY_CUSTOM_ENDPOINT` required; optional `ASSAY_CUSTOM_KEY` |

To use a stronger workstation later, change the endpoint and model configuration. HTTP is accepted only for loopback; remote hosts require HTTPS. Endpoints cannot contain embedded credentials, query strings or fragments. Configure authentication and TLS at the remote service. Arbitrary provider SDKs and protocols require an adapter or the custom service; a profile name does not imply universal API compatibility.

All mappings send bounded synthetic metadata only, never source text or the verification verdict. The CLI has a 30-second deadline, disables redirects and bounds response size to 64 KiB. Responses must map to the four registered investigations. Rate limits, malformed responses, outages and missing configuration produce unavailable advice; unknown actions and refusals require review. Server error bodies and credentials are not printed. CI never calls live providers. Requested model IDs are recorded; resolved revision remains `NotObserved`. Provider confidence is not used as a shared gate.

## Small local model and SystemOne gateway

With Ollama installed and running locally:

```sh
ollama pull qwen3.5:0.8b
# Use --provider=qwen in the CLI command above.
```

The small model is an experimental router. Valid JSON is not evidence that the recommendation is correct. Larger generative models can use the same `qwen` or `chat` mapping, but still need task evaluation. This is not Clef: Clef-Flash has a post-trained decision head on Qwen3.5-9B, while Clef uses Qwen3.8-27B. Loading a base Qwen checkpoint does not reproduce that head.

An optional loopback gateway exposes one SystemOne **choice** question over Ollama:

```sh
./eng/tooling serve-local-advice --backend ollama --model qwen3.5:0.8b --port 8001
export ASSAY_CLEF_ENDPOINT=http://127.0.0.1:8001/v1/systemone
export ASSAY_CLEF_MODEL=qwen3.5:0.8b
# Now --provider=clef-local exercises the SystemOne mapping over the Qwen gateway.
```

The response identifies the generative gateway. It supplies no fabricated probabilities or confidence and implements neither scores nor predicates. The `clef-local` profile describes the protocol; the requested model identifies Qwen. Do not label gateway output as a real Clef checkpoint. The development server binds only loopback, handles one request at a time and has no authentication. Keep it local; use a properly managed service for remote deployments.

## SetFit and custom agents

SetFit needs a reviewed, locally trained model with exactly these string labels: `inspect_assertion`, `inspect_reachability`, `review_contract`, `human_review`. It does not answer arbitrary dynamic questions. Train on the same serialized metadata representation the CLI sends, use held-out cases and compare with the deterministic baseline and a simple text classifier. Missing facts should have reviewed human-review labels. No model or probability cutoff has been qualified here.

The former in-process SetFit wrapper was retired during the C# migration.
The C# provider adapter retains the external SetFit HTTP protocol for separately
managed endpoints; this repository installs no SetFit library or model. Training
and live inference remain unqualified.

Custom service input contains `schema: "assay-advice-request/0.1"`, `model`, `state`, `instructions`, and `allowedActions`. Return `{ "schema": "assay-advice-response/0.1", "status": "suggested", "choice": "inspect_assertion" }`, or `status: "review"` for missing evidence/refusal. The selected service can implement a human review queue or agent workflow; this sample does not create either service. Return a synchronous response within the deadline. The CLI validates registered actions and never executes returned commands.

Offline mapping tests run with the repository test project. Gateway tests:

```sh
./eng/tooling test-local-advice
```

## Completion validation on 2026-10-20

The standard offline try command passed with zero build warnings/errors, 71 C#
tests, eight gateway/authorization tests, eight mutation-runner failure tests,
all six executed cases, and all ten hypothetical routing scenarios. The executed
outcomes were three survivors, one killed mutant, one invalid mutant, and one
failed original baseline. Additional probes exposed the boundary and reachability
gaps while passing on the original. The equivalent candidate remained unresolved.
No model-provider calls were made.

The authoritative refined gate also passed: five projects, 71 passing tests,
zero findings, zero missing evidence, and zero tool failures. Eight upstream
characterization checks and pinned provenance assertions passed. Local logs are
`evidence/generated/decision-assist-completion-2026-10-20.log` and
`evidence/generated/decision-assist-verify-2026-10-20.log`; the authoritative
receipt remains `evidence/generated/refined/verify.json`.

The local workflow, case review notes, generated case records, setup guide, and CI
integration complete this bounded development slice. Human case adjudication,
held-out real-consumer evidence, and measured provider utility remain open.

## Earlier local validation on 2026-10-18

The adoption slice passed 71 C# tests, the authoritative refined gate with five projects and zero findings, eight Python gateway/authorization tests, and ten offline routing scenarios. The isolated mutation experiment passed. No policy rule or suppression was weakened. The earlier provider slice also passed eight Gilded Rose checks, ten GoF tests and 23 Functional Shape tests, with repeated GoF and Shape receipts byte-identical.

Live OpenAI Decisions worked through the C# adapter. Qwen3.5-0.8B ran locally on CPU through both Ollama and the SystemOne gateway. Across four completed synthetic routing cases, the direct small-model path matched three expected labels and chose reachability incorrectly for an explicitly covered mutation with a missing assertion. An additional incomplete-run case was handled locally without model inference. These tiny, explicit cases do not qualify the model. The ambiguous unknown-coverage fixture produced advice different from the baseline in both the OpenAI and direct Qwen trials; both suggestions remained visible separately from the unchanged verdict.

Sanitized local trial records and validation are under ignored `evidence/generated/decision-assistance/provider-trials/`. They are generated observations, not authoritative verification receipts. Before the missing-context guard was added, the Jev C# adapter was exercised with five live requests: three of four clear routing cases matched expectations, while the unknown-evidence case suggested reachability instead of human review. The existing unknown-coverage fixture also differed from the deterministic baseline. Observed end-to-end CLI times were 0.59–0.64 seconds; these few sequential calls do not establish latency or provider superiority. The incomplete-run case stayed local. Those historical calls were not repeated for the adoption slice; missing-context routing is now handled deterministically before inference. Actual Clef weights, Laya and trained SetFit were not exercised. Clef-Flash source/configuration were fetched separately into the local provider lab without its weights. No trained SetFit model is supplied.

## Evidence boundary

The original Assay verdict stays independent of advice. A provider cannot return Pass, discard a mutant as equivalent, choose shell commands, or waive required tests. Provider suggestions remain labeled unqualified. Compare them with deterministic and human triage on held-out cases before extending their role.

Only this bounded next-investigation task is mapped. Broader Jev quality evaluation, live Clef and Laya qualification, SetFit training, arbitrary source ingestion, and generated test patches remain future work. Existing rule admission and enforcement are unchanged.

Sources: [OpenAI Decisions](https://developers.openai.com/api/docs/guides/decisions), [Jev](https://docs.typesafe.ai/introduction/quickstart), [Clef](https://huggingface.co/Cloudflare/clef), [Clef-Flash](https://huggingface.co/Cloudflare/clef-flash), [Laya](https://github.com/NandhaKishorM/laya), [SetFit](https://huggingface.co/docs/setfit/index), [Ollama structured outputs](https://docs.ollama.com/capabilities/structured-outputs).
