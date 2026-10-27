# DRAFT — An AI suggestion said approve. The expense rules required a receipt.

Series: Assay Engineering. Audience: working C# developers and reviewers. Supported claim: C02, with C01/C05/C06 as supporting distinctions. Evidence: [recorded examples](../examples/catalogue.json); exact producer identities are inside the entries. Action: reproduce the ExpenseGuard case. Approval: DRAFT; content digest belongs in `pack-manifest.json`. Limits: synthetic sample, simulated default advisor/payment, selected code checks and configured tests. Refresh source/toolchain/package status before publication. AI-assisted draft for human technical/editorial review; keep the eventual article free to read.

<!-- article-start -->

An expense arrives without a required receipt. The assistant suggests approval. ExpenseGuard asks for more information instead.

The assistant in this demonstration is an offline stub that deliberately returns approval-shaped advice. No live model is being evaluated, and no real payment is sent. That detail belongs in the opening because the interesting result is the boundary between suggestion and authority. A misleading suggestion should remain a suggestion, regardless of whether it comes from a model, a deterministic fixture or a person typing a note.

The application does not give that suggestion a route to the approval state. Its domain engine evaluates the claim and policy facts. The test describes the expected outcome. The verification report records the evidence that ran. Those are separate responsibilities, and preserving the separation makes the demonstration useful.

## Start with a rule small enough to challenge

ExpenseGuard's policy requires a receipt above its configured threshold. The surrounding facts matter: a valid amount and currency, a recent date, no duplicate, the appropriate category and the available exchange-rate evidence. If another fact is invalid, the application could reject the claim for a different reason. A good example controls those facts so the displayed reason means what we say it means.

The guided case uses amounts below, exactly at and just above the threshold. These are decimal amounts, not approximate binary floating-point values. That seems mundane, but it is part of making the demonstration reproducible. An invalid-precision amount rejected by money validation would not demonstrate a missing-receipt decision.

For the above-threshold case, the simulated advisor still says Approved. The domain returns NeedsMoreInfo. Changing the recommendation does not supply a receipt, change the policy or authenticate an approver. The report identifies the domain and the executed application test as the relevant authorities. It does not describe this as a Roslyn analyzer discovering an expense rule.

## Give each layer a job

The domain contains values, states, policy evaluation and explicit transition results. HTTP, PostgreSQL, receipt storage, configuration and optional provider calls belong to the shell. This is a deliberately chosen functional core, not a claim that all object-oriented or mutable C# is defective.

The architecture can be described without specialist terminology:

```text
Claim and receipt facts -> Domain decision -> Stored state and audit record
             Advice ----> Displayed suggestion only

During development and CI:
Compiler + selected code-policy checks + configured tests -> Evidence report
```

The bottom line of that diagram is not a runtime approval service. CSAssay does not sit in front of every expense and infer whether it is legitimate. It checks selected code policy and evaluates the configured verification evidence for the source being reviewed. The application remains responsible for its business decision and operational controls.

The compiler supplies another important layer. Native-union exhaustiveness belongs to the supported C# compiler. A prototype CSAssay observation about catch-all handling is separate advice, not a substitute compiler or an admitted release blocker.

## A finding a developer can understand

Consider a public record whose Total property has a public setter. A caller can change that property after creation. If the chosen domain policy expects immutable carriers, that shape is a useful review finding.

The practical response may be a small set-to-init edit. The offline explanation shows a compiling before/after example, what the rule inspects and where its inspection stops. The code action is an explicit proposed edit. A reviewer still checks callers, serializers, interfaces and persistence contracts before accepting it.

There is also a limitation worth showing beside the example: init is shallow. An init-only property can reference a mutable list, and a record does not establish that its entire object graph is immutable. Selected collection-exposure checks help with known shapes; they do not prove that every reachable object or effect is safe.

That is a useful conversation with an agent. “Make it functional” is vague. “This public record exposes a setter; inspect the finding and review this small change” supplies a concrete reason and a bounded next action.

## Break the requirement, not the demonstration

A stronger business control changes the configured receipt threshold while leaving the requirements tests unchanged. The existing first-change exercise locates the policy constant, moves it from 25 to 30 in a disposable copy, runs the required tests and reads the failure.

The failed assertion matters more than a red icon. It identifies an actual disagreement between the candidate implementation and the retained requirement. A compiler error, unavailable database or interrupted process would be a different result. None should be counted as a successfully detected business mutation.

After the experiment, restore only the deliberate edit and rerun into a fresh evidence directory. Keep the failed output as well as the successful output. A model's explanation that it fixed the issue is not the evidence; the independent execution is.

This approach also exposes a limit of testing. If the requirement itself is wrong, or the tests omit an important case, a passing run can still leave a business defect. CSAssay does not invent the missing product decision. Human review, boundary examples and carefully chosen negative controls remain necessary.

## Read the receipt before trusting the colour

The readable report needs to answer a few ordinary questions. Was this analysis only or authoritative verification? Which input and policy were used? Did the required tests execute? Is anything missing? Were there execution failures? Which source and tool identities does the evidence describe?

A provisional check can exit zero while showing findings. That is useful observation, but it is not release authority. Zero findings combined with missing required evidence should remain incomplete. “No tests configured” and “tests did not run” also mean different things. Neither establishes that all expense behaviour is correct.

The static Playground makes those distinctions visible with recorded fixtures. It does not compile arbitrary visitor code. If a visitor edits an example, the page marks that edit Unverified and stops associating the stored result with it. Resetting returns to the recorded input, not to a newly executed scan.

Recorded badges follow the same principle. A green historical result can describe its producer commit. It must not pretend to monitor today's main branch. The adjacent evidence index supplies scope, tool identity, required-test status and the linked receipt. A hash is a binding under trusted local storage, not a signature or a whole-application certification.

## The shell still needs engineering

Functional data alone does not solve persistence, concurrency or payment integration. ExpenseGuard uses expected versions, database transactions, audit-chain checks and a simulated payment worker. Their bounded behaviour needs actual HTTP, database and worker tests.

One particularly instructive review finding concerned upload cleanup. A database commit can succeed while its acknowledgement is lost. An exception at the client does not prove rollback, so deleting the uploaded receipt as compensation can destroy bytes a committed record still references. Retaining the file and handling reconciliation separately is a different responsibility from checking record immutability.

Another concerns progress. Correctly refusing one invalid queued item is not enough if that item prevents every later valid item from running. Retry scheduling and an explicit manual-review outcome address a system property the receipt-presence check alone cannot establish. Those are application tests and operational decisions, not newly invented analyzer guarantees.

## Make the trial honest and small

The current native development line uses an exact .NET 11 RC2 SDK pin, preview language settings and locally prepared packages. Historical public NuGet releases are a different lane. Current vendor support and any final-SDK upgrade need separate review and qualification; a new ecosystem release does not automatically qualify this tool or sample.

The first useful outcome is modest: a developer understands one finding, reproduces one failed requirement control and can explain what the resulting evidence supports. No account, model key or live payment is needed to explore the recorded examples. Running the complete application checks requires the documented local prerequisites, including real isolated PostgreSQL.

That is the workflow I want from AI-assisted C#: concrete instructions, independent checks and a report that does not claim more than the evidence. To try it, start with the runnable ExpenseGuard case and its documented limits:

https://github.com/CanonFlow-Assay/CSharpAssay.Playground/tree/ec1304e9c9d379a7ae72b0ab65f35d64cc9874df/samples/100-expenseguard

<!-- article-end -->
