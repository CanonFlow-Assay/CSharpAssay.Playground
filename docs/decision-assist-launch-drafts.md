# Decision Assist launch drafts

Local drafts for review. The adoption workflow and provider changes are not yet pushed. Publish only after approval and verification that the linked public revision contains the demonstrated commands. No outreach or publication is authorized by this file.

## LinkedIn draft

I'm building CSharpAssay for developers and coding agents who deliberately choose functional C#.

The idea is concrete: make selected architectural promises executable. Keep domain data immutable, represent outcomes explicitly, and retain evidence of what was checked.

In the Playground, a tiny mutation changes `amount > 0` to `amount >= 0`. Existing positive and negative tests still pass. A zero-boundary assertion exposes the difference.

Decision Assist can suggest the next investigation through Jev, OpenAI or a configured local model. Its suggestion stays separate from verification. Missing execution evidence requires review, and model advice cannot grant a passing verdict.

The models also make mistakes. That's part of the demonstration, and why we're keeping a deterministic baseline and comparing providers on identical inputs.

This is an early .NET 11 preview using the separately qualified RC2 SDK. Native unions still require preview C#. I'm interested in feedback from functional C# developers and teams reviewing agent-generated changes: does this workflow make the next investigation clearer?

## Substack or Medium draft

### Functional C# that developers and agents can check

An instruction such as “keep the business core functional” is useful only when a team can tell whether a change follows it. An AI coding agent can produce plausible code that compiles while adding mutable state or hiding an outcome the design intended to make explicit.

CSharpAssay explores a chosen functional C# policy through Roslyn checks and recorded verification evidence. The choice matters: this is a discipline a team adopts for an appropriate boundary. It is not a claim that every object-oriented design is defective or that immutable data proves the business logic correct.

The Playground makes the idea tangible with rule examples, behavior-preserving functional alternatives, and an optional Decision Assist experiment. Its current native-union profile requires the exact pinned .NET 11 RC2 SDK and preview C#.

### A small test gap with an observable consequence

Consider a rule that accepts amounts greater than zero. Positive and negative examples can pass while a mutation from `amount > 0` to `amount >= 0` remains undetected. A test for zero distinguishes the original from the mutant.

The Playground copies the source into an isolated temporary project, compiles both versions, executes the declared probes, and records their results and source identities. The mutation is real; the example is intentionally small. A separate synthetic scenario set exercises missing evidence and routing decisions without pretending those scenarios are additional execution results.

That separation is central. A recommendation to add a test is not proof that the test repairs a gap. The observed original and mutant behavior provides that evidence.

### What decision models contribute

A typed decision model can choose among a few investigations: inspect an assertion, check reachability, review the contract, or request human context. Jev and OpenAI have live integration trials; other provider mappings and local options are available with different qualification levels.

Early trials already show disagreement and mistakes. We therefore keep the deterministic suggestion visible, stop before inference when context cannot support an investigation, and leave the verification verdict unchanged. A shared evaluation command can give both providers identical inputs, with an explicit request budget. Its labels are drafts to review, not a finished benchmark.

This is where we want to learn whether model advice earns its cost. Does it help a reviewer select a useful investigation? Does it handle uncertainty appropriately? Is a deterministic rule already sufficient?

### Making the workflow usable by agents

Agents need exact prerequisites, commands, stable diagnostics and machine-readable results. Repository instructions point to an offline demonstration and relevant verification commands. CI can execute the checks independently of an agent's explanation.

The first useful result is modest: a developer can reproduce a finding, understand its scope, and verify a change. Broader mutation and resilience tooling belongs to the future roadmap. The immediate experiment is useful optional advice around trustworthy evidence.

## Review before publication

Confirm the published revision has the runnable workflow and expected results. Preserve the exact toolchain and preview status. Link to the getting-started guide and source rather than implying a stable public package install is available. Include model mistakes and the limits of the synthetic scenarios. Use one detailed article destination initially and a short LinkedIn introduction; adapt subsequent material to actual feedback.
