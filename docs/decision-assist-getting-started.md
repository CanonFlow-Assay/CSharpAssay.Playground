# Getting started with CSharpAssay Decision Assist

CSharpAssay helps developers and coding agents follow a chosen functional C# policy. Its Playground adds optional advice about the next investigation after a surviving mutation. Deterministic evidence and verification remain independent of that advice.

The current audience is developers evaluating native C# unions on the exact .NET 11 RC1 toolchain. This is a development preview with a bounded demonstration, not an automatic application migration or a general mutation engine.

## Run the demonstration

You need Bash, Python 3, the exact SDK in the repository's `global.json`, and a matching CSharpAssay source checkout. Preparation restores NuGet packages, so a first run may need network access. No API key is required.

From this Playground checkout:

```sh
./eng/try-decision-assistance.sh ../CSharpAssay
```

The command prepares local packages, builds the solution, runs the primary C# tests and Python evidence/gateway tests, then executes six amount cases, ten hypothetical scenarios, and the 26-case order-sample coverage/mutation comparison. It calls no model providers. If the SDK or paired checkout is missing, the command explains the prerequisite and exits. The complete order corpus takes longer than the small amount demonstration.

Read these generated files:

| File under `evidence/generated/decision-assistance/` | Meaning |
|---|---|
| `experiment.json` | Source identities and observed execution of the bounded mutation experiment |
| `advice.json` | Deterministic investigation and unchanged experiment verification status |
| `firm-advice.json` | The same action with firmer wording |
| `evaluation-none.json` | Offline policy behavior across the synthetic scenario set |
| `mutation-cases.json` | Six executed cases, reviewed expectations, outcomes, and aggregate completion/failure status |
| `cases/<id>/experiment.json` and `advice.json` | Per-case execution evidence and deterministic investigation |

The real experiment changes `amount > 0` to `amount >= 0`. Positive and negative tests let the mutation survive; adding the zero-boundary probe distinguishes the original and mutated implementations. This demonstrates a specific missing assertion. It does not establish that a production project's tests are sufficient.

The separate scenario set changes hypothetical observations, such as an incomplete run or missing coverage evidence. Those scenarios test routing policy; they are not additional executed mutations. Their expected labels are drafts for review.

The executed corpus adds an unexercised policy path, an integer-equivalent candidate for contract review, a killed mutation, an invalid expression, and a failed original baseline. Its explicit requirements and agent review notes are in `samples/50-decision-assistance/corpus/mutation-cases.json`; human adjudication remains pending. Inspect the [case review and evidence limits](../samples/50-decision-assistance/README.md#executed-case-review). Direct-call counts cover this fixture's dispatch only. Always check the aggregate completion status before using per-case results.

For a consumer workflow spanning domain validation, storage, and transport, read
the [order sample investigation workflow](../samples/40-functional-shape-v0.1/README.md#order-assurance-and-decision-assist).
Its generated results are in `evidence/generated/shape-orders/`. It compares the
unchanged original 16 behavior tests with 55 tests and measures all four production
assemblies. Its packet metadata carries the declared order requirement, so advice
does not describe every finding as a zero-amount problem.

## Use a model explicitly

A provider key alone never enables model calls. Choose a provider and supply its environment variable through your usual secret-management workflow. Do not put secrets in repository files, prompts or command arguments.

For one existing experiment:

```sh
dotnet run --project samples/50-decision-assistance/src/DecisionAssist.Cli --no-build --no-restore -c Release -- evidence/generated/decision-assistance/experiment.json --provider=jev
```

Replace `jev` with `openai` to use OpenAI Decisions. Jev reads `TYPESAFE_API_KEY`; OpenAI reads `OPENAI_API_KEY`. The [provider matrix](../samples/50-decision-assistance/README.md) covers Clef, Laya, local Qwen, trained SetFit, structured-chat servers and custom advisor services. Protocol support does not imply equivalent model quality.

Advice may differ from the baseline. Inspect both and the evidence. Unknown context without a supported investigation is handled locally as review, before inference. Tone affects wording, not authority.

## Compare identical inputs

After the offline demonstration passes, an explicitly requested comparison can run:

```sh
python3 eng/evaluate-decision-assistance.py --provider jev --provider openai --live --max-requests 8
```

This uses the same frozen scenarios, instructions and allowed investigations for each selected provider. Four scenarios currently permit model inference, so two providers require at most eight eligible calls. The other six scenarios are handled locally. The script checks the request budget before any provider call and stops the evaluation on unavailable advice or execution failure, without retry or provider fallback. A comparison is an explicit request to test both providers, not a fallback chain.

Generated `evaluation-jev-openai.json` records scenario/input identities, CLI and implementation digests, requested model, end-to-end CLI time, expected and observed actions, review status, and verdict preservation. Provider model revisions and token usage are not currently captured by the CLI. Timing includes process overhead; it is not server latency. No confidence threshold or provider quality gate is applied. Model disagreement is reported without changing Assay verification.

Review labels before drawing conclusions, use more varied held-out evidence, and assess reviewer effort as well as correct routing. Do not rank providers from the earlier unequal smoke tests. SetFit becomes a useful candidate after reviewed labels and a stable input representation exist.

## Give an agent a concrete task

An example task:

> Read AGENTS.md. Run the offline Decision Assist demonstration. Explain the difference between the mutation execution record, hypothetical routing scenarios, and an authoritative verification receipt. Improve one identified behavior without changing the verification verdict or weakening policy. Run the relevant checks and report the remaining gaps. Keep all work local and make no model calls.

Repository instructions help an agent find the workflow; tests and verification check its output. Instructions do not guarantee compliance. The root `AGENTS.md` and `.github/copilot-instructions.md` provide entry points; different agent tools discover instruction files differently. [GitHub's custom-instruction guidance](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions) explains its repository mechanism.

## Report a useful result

Record whether a new user could set up the pinned toolchain, reproduce the demonstration, interpret missing evidence, and identify the next investigation. Feedback about a misleading diagnostic or unhelpful suggestion is more useful than a claim that AI has verified correctness.

The broader mutation, risk, contract and resilience roadmap remains separate. Current work is the Decision Assist experience and its evaluation.


## Real CSV consumer

The [CsvHelper sample](../samples/60-csvhelper-functional/README.md) adds a real
library boundary: typed converter rewrites, immutable row decisions, streaming
adapter and CLI. Run `./eng/run-csvhelper.sh` after package preparation for 118 C#
tests, 36 evidence boundary tests, 24 isolated mutations and authoritative
sample receipts. The two authored smoke tests form its before selection; they
are separate from the original upstream test suite. The default try command
runs the new offline investigations and controlled streaming benchmark too.

Original upstream failures and unsupported target frameworks are retained in a
separate assessment. The sample's verification pass cannot grant upstream
qualification or equivalence. Read `evidence/generated/csvhelper/validation.json`
and `upstream-assessment.json` for their respective scopes. Model calls remain
optional and unqualified; this gate uses deterministic Decision Assist only.
