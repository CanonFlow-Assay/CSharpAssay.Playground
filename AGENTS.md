# CSharpAssay Playground agent instructions

Read `/home/arun/Developer/AGENTS.md` when available. Respect repository-specific instructions below sample directories. This repository demonstrates an explicitly chosen functional C# policy; its findings do not establish universal architectural correctness.

## Current scope

Improve Decision Assist and its developer/agent workflow. The owner has authorized reusable application intake, assessment/decision contracts, bounded Mutation/CodeRisk/Contract/Resilience previews, a Polly consumer and new-application onboarding. Keep these under their own projects and evidence scopes. CsvHelper enhancements are paused. The paired CSharpAssay engine is a separate repository; change its public evidence integration deliberately, never merely to make Playground checks pass. Existing rule admission, nullability, test minima and authority boundaries remain intact.

## Prerequisites and first run

Use the exact SDK in `global.json`, `net11.0`, and C# `preview`. Native unions depend on this toolchain. Keep a matching CSharpAssay checkout beside this repository or supply its path.

From the repository root:

```sh
./eng/try-decision-assistance.sh ../CSharpAssay
```

This prepares local packages, builds, runs the primary C# tests and Python gateway/evidence tests, executes six isolated amount cases, evaluates ten hypothetical scenarios, and runs the 26-case order coverage/mutation comparison, and the CsvHelper consumer's 24-case comparison and controlled streaming benchmark. NuGet restore may require internet access. The command makes no model-provider calls and publishes nothing.

For the authoritative refined gate after preparation:

```sh
./eng/run-assay.sh ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll
```

For changes affecting the other samples:

```sh
./eng/run-gof-crosswalk.sh
./eng/run-functional-shape.sh
./eng/run-csvhelper.sh
./eng/run-all-samples.sh
```

The all-sample solution includes every authored project. Exact SDK, .NET 11-only
targeting (including multi-target lists), preview C#, nullable analysis and
warnings-as-errors are mandatory. Only the three reviewed negative/upstream
fixtures in `workflows/sample-toolchain.json` retain their deliberate nullable/
warning exceptions; they still require the native SDK/TFM/language. Preserve and
test their violations. The external eShop history contains no runnable project
and its legacy policy is explicitly archived. Do not imply that it was ported.

Use existing repository scripts and inspect their output. A passing build, provisional `check`, model suggestion, or scenario evaluation is not an authoritative `verify` receipt. Impure examples intentionally retain their expected findings.

## Functional boundaries

Keep domain decisions immutable, with explicit outcomes and native unions where cases are closed. Place network, filesystem, environment access and provider configuration in the CLI shell. Preserve compiler evidence and existing behavior. Do not add suppressions, disable nullable checks, reduce rule severity, lower test minima, or reclassify code to hide a finding. A test-count expectation may increase when reviewed tests are added.

## Decision assistance

The deterministic baseline and verification verdict remain visible separately from optional advice. Incomplete runs, invalid mutations, failed baselines, killed mutations, and insufficient context must not trigger model inference. A model may select a registered investigation or request review. It cannot grant Pass, waive tests, declare automatic equivalence, execute commands, or change source.

The scenario set in `samples/50-decision-assistance/corpus/scenarios.json` is synthetic and contains hypothetical states. Its labels are draft policy expectations, not human-adjudicated gold data. Do not claim it establishes provider quality or business correctness.

The separate `corpus/mutation-cases.json` declares executed fixture cases with requirements and agent review notes. Human adjudication remains pending. Check the generated aggregate `mutation-cases.json` completion status and per-case records. Direct-call counts are scoped to the fixture, and an equivalent candidate remains an unresolved survivor. Never turn an unexpected compiler/test failure into an expected case result.

The order sample's `mutation-cases.json` and `eng/run-shape-investigations.py` compare unchanged original tests with the expanded suite. Bind real line/branch coverage to its original source scope, preserve raw test failures, and keep before/after advice separate from authoritative Shape receipts. Its coverage reports must contain all four production assemblies. Model metadata must identify the actual sample requirement. No live calls occur in CI or the default try command.

Live evaluation requires an explicit user request, `--live`, an appropriate request budget, and configured credentials. Never read credential files, print environment values, embed keys in arguments, or commit secrets. Use provider-specific environment variables described in the sample README. Stop on rate limits or restriction warnings; never retry through a different provider to obtain a preferred answer. CI remains offline for models.

## Reporting and publication

Report changed behavior, checks performed, failures, and evidence gaps. Keep generated evidence under ignored `evidence/generated/`. Distinguish tested protocol mappings from live provider qualification; distinguish a Qwen gateway from an actual Clef checkpoint. Do not invent confidence values or compare unequal smoke tests as a benchmark.

Prepare all work locally. Do not push, publish drafts, create remote issues or PRs, or send messages without explicit user authorization for that action. The CsvHelper consumer in `samples/60-csvhelper-functional` keeps a pinned source assessment separate from its .NET 11 sample verification. Read its own AGENTS.md for mutation, license, scope and advisory requirements.

The owner authorized local Forgejo review followed by consolidated remote Git branches and PRs on 21 October 2026. Keep future publication within explicit session authorization. Marketing drafts remain local review material.
