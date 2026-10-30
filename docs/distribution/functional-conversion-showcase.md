# Functional C# conversion showcase — DRAFT

Status: local review draft, 30 October 2026. This is a second, distinct
explanation after the 28 October ExpenseGuard launch. It must not be reposted
as that launch, sent to a subreddit, scheduled, or published until the owner
reviews the exact destination copy and confirms publication. The draft is
AI-assisted and must retain that disclosure when it is published.

## The promise

**Functional C# is most useful when it makes one important decision easier to
see, test, and change—not when it turns an entire application into a rewrite
project.**

The Playground's smallest practical route is the GoF crosswalk. It keeps the
observable business behaviour, then changes only the representation of a
bounded decision. For example, the approval state becomes a closed value with
pure transition functions:

```csharp
public enum ApprovalState { Draft, Submitted, Approved }

public static ApprovalState Submit(ApprovalState state) =>
    state == ApprovalState.Draft ? ApprovalState.Submitted : state;

public static ApprovalState Approve(ApprovalState state) =>
    state == ApprovalState.Submitted ? ApprovalState.Approved : state;
```

This is deliberately not an automatic conversion. A stateful object can still
be the better fit for identity, callbacks, framework lifecycle, incremental UI
editing, or effects that genuinely belong together. The crosswalk's tests run
both representations against the same ten declared business cases; they do not
prove performance, serialization, concurrency, or broad architectural quality.

## A conversion path for a functional-curious C# developer

1. **Choose one decision, not a subsystem.** Pick a calculation, eligibility
   rule, state transition, or conversion that has a small, closed outcome set.
   Do not begin with EF Core entities, HTTP DTOs, DI composition, or a large
   object graph.
2. **Write the outcomes down.** Replace an implicit Boolean/exception/nullable
   convention with a named outcome only where callers must make a choice. Keep
   the input and output small enough to test without a database.
3. **Move effects to an edge.** Parsing HTTP, accessing a database, reading a
   file, and calling a provider remain useful work. Convert those inputs once
   at a declared shell boundary; keep the core decision deterministic.
4. **Keep the old behaviour observable.** Characterize the legacy behaviour
   first, then add the boundary cases the old tests skipped. A passing green
   suite is not a guarantee that a meaningful difference is covered.
5. **Ask each tool the question it can answer.** The compiler can check an
   unhandled native-union case. CSharpAssay can apply the selected team policy.
   Application tests can check that the business rule holds. None substitutes
   for the other two.
6. **Expand only after a useful result.** Use the small crosswalk first;
   ExpenseGuard adds HTTP, PostgreSQL, worker, authorization, audit and
   evidence concerns once the boundaries are understandable.

## What the Playground actually taught us

### Detour 1: `record` and `init` do not establish immutability

The convenient C# surface is not the whole guarantee. A record may still hold
mutable nested data, and an `init` property is not a proof of deep immutability.
That is why the project describes a **selected policy**, not universal purity,
and why its findings remain scoped to configured rules.

### Detour 2: advice has to lose to evidence

ExpenseGuard deliberately lets an offline simulated advisor suggest approval
for an expense that lacks a required receipt. The domain returns
`NeedsMoreInfo`; the advice cannot invent a receipt or approve payment. This is
the practical rule for coding agents too: a plausible recommendation may select
the next investigation, but it cannot turn incomplete execution into a pass.

### Detour 3: tests can miss the one value that changes the policy

The mutation from `amount > 0` to `amount >= 0` leaves ordinary positive and
negative tests green. A zero-boundary test distinguishes the two behaviours.
The lesson is not “use mutation testing everywhere”; it is “make the boundary
that carries the policy explicit, then test it.”

### Detour 4: do not convert the framework boundary for its own sake

The Playground includes a historical eShop Ordering.Domain case study, but it
does not vendor, port, or label eShop as functionally incorrect. The eShop
source remains outside the .NET 11 Playground. Its useful lesson is narrower:
keep HTTP, persistence, and framework contracts at declared boundaries, and
make a conversion only when the core decision becomes clearer and testable.

### Detour 5: current language features need an honest toolchain story

The native-union route is pinned to the repository's .NET 11 RC2 SDK and C#
preview settings. It is a qualified preview lane, not a claim of final-SDK
support or a recommendation to change every production project today. Start
with the examples before committing a team to the same lane.

## A seven-minute first look

Read the [GoF functional crosswalk](../../samples/30-gof-functional-crosswalk/README.md)
and compare one classic/refined pair. Then read the
[adjudication notes](../../samples/30-gof-functional-crosswalk/ADJUDICATION.md)
before treating a finding as a refactoring instruction. The runnable sequence
is intentionally longer because it restores locked packages, runs the shared
tests and rejects changed evidence:

```text
./eng/prepare-assay.sh ../CSharpAssay
dotnet test samples/30-gof-functional-crosswalk/tests/Gof.Crosswalk.Tests/Gof.Crosswalk.Tests.csproj \
  --configuration Release
./eng/run-gof-crosswalk.sh
```

That last command is a reproducibility gate, not a beginner's prerequisite.
The actual first success is being able to explain one changed decision and the
test that protects it.

## Publishing package — tailored, not duplicated

Every version below points to the existing published Playground and source.
Do not create a company account, a paid campaign, an email blast, a subreddit
submission, a cross-post, or automated follow-up. The Reddit version is for
the existing profile only; r/dotnet explicitly disallows AI-written library
promotion and no subreddit permission is inferred.

### LinkedIn — short engineering lesson

**Proposed text**

I used to understand functional programming in C# mostly as a set of good
ideas: immutability, pure functions, explicit outcomes.

The hard part was deciding where those ideas help a real application without
turning “adopt FP” into a rewrite.

My current rule is deliberately small: choose one decision with a closed set of
outcomes, keep effects at a declared boundary, and prove the behaviour before
expanding the scope.

In CSharpAssay Playground, an approval transition is a tiny pure function over
`Draft`, `Submitted`, and `Approved`. The value is not that the enum is clever.
The value is that the transition and its boundary cases are visible to a
reviewer.

The project also keeps the uncomfortable lessons visible: `init` is not deep
immutability; an AI recommendation cannot override missing evidence; and a
test suite can miss the one boundary value that changes a rule.

The examples are a selected functional C# policy, not a claim that OO design,
EF Core, or framework boundaries are wrong. It is a .NET 11 RC2/C# preview
lane, so treat it as an experiment with a reproducible toolchain rather than a
blanket migration prescription.

Source and recorded examples: https://github.com/CanonFlow-Assay/CSharpAssay.Playground

If you were converting one existing C# decision—not a whole service—would you
start with a state transition, a calculation, or input validation?

_AI-assisted draft; maintainer-authored technical claims are limited to the
linked examples and their recorded evidence._

### Substack — canonical case study

**Title:** I knew the functional ideas. Turning one C# decision into evidence
was the harder part.

**Subtitle:** A small .NET 11 Playground conversion, the detours it exposed,
and why I did not try to convert an entire service.

**Proposed text**

Functional programming can sound obvious from a distance. Avoid mutation.
Prefer explicit outcomes. Keep side effects at the edge. Those are useful
principles, but they do not tell a working C# developer which file to touch on
Monday morning.

My first useful constraint was to stop thinking about “a functional rewrite.”
Instead, I chose one decision with a closed outcome set. In the CSharpAssay
Playground, that is an approval transition: Draft, Submitted, Approved. The
functions that move between those states take a value and return a value. That
makes the valid changes visible and cheap to test.

The interesting part is what this does *not* do. It does not remove ASP.NET,
EF Core, PostgreSQL, dependency injection, or the need for ordinary C# objects.
Those are often appropriate at a boundary. The point is to convert HTTP,
persistence, and provider inputs once, then let the core rule be a deterministic
decision over explicit facts.

The Playground contains behavior-matched classic and functional-first examples.
It does not declare the classic versions bad. A stateful object can be a better
choice when identity, callbacks, UI edits, or framework lifecycle are central.
The value of a conversion is that a particular business decision becomes easier
to inspect and protect—not that the code resembles a functional-language demo.

Three detours stopped me from overselling that conclusion.

First, a C# `record` or `init` property does not establish deep immutability.
Nested state can still mutate. CSharpAssay therefore checks a selected policy;
it does not issue a purity certificate.

Second, advice is not authority. ExpenseGuard contains an offline simulation
where an advisor recommends approving an expense with no required receipt. The
domain still returns `NeedsMoreInfo`. The analogy to code generation is
deliberate: a fluent explanation can suggest the next check, but it cannot make
missing tests, missing receipts, or incomplete execution become true.

Third, green tests can still omit the decision boundary. The small mutation
`amount > 0` → `amount >= 0` preserves ordinary positive and negative cases.
Only a zero test reveals the difference. I take from this neither a demand for
mutation testing everywhere nor a claim that counts prove quality. I take a
more modest rule: name the boundary that carries policy, and test it.

There is also a .NET 11 reality check. The Playground's native-union examples
use its pinned RC2 SDK and C# preview configuration. That is an intentional,
reproducible experiment—not a claim of final SDK support or a reason to alter
every production solution. The historical eShop case study remains historical;
it is not a port and it is not evidence that a reference application should be
rewritten in a functional style.

If you want to try the idea, start with the GoF crosswalk rather than the full
application. Compare one state transition or calculation, read the limits, and
run the shared tests. Only then decide whether an equivalent decision in your
own code is worth making explicit.

The source, commands, and evidence boundaries are in the CSharpAssay Playground:
https://github.com/CanonFlow-Assay/CSharpAssay.Playground

_This is an AI-assisted technical article. It describes selected examples and
recorded evidence, not a correctness, security, or production-readiness claim._

### Medium — code-first tutorial

**Title:** Don’t “convert to functional C#.” Convert one decision and keep the
proof.

**Proposed text**

Functional C# becomes practical when it narrows a review question.

Suppose an approval flow is represented by objects that carry mutable flags,
callbacks, and state changes. Before redesigning anything, isolate the part
that answers one question: “given the current state, what is the next state?”

```csharp
public enum ApprovalState { Draft, Submitted, Approved }

public static ApprovalState Submit(ApprovalState state) =>
    state == ApprovalState.Draft ? ApprovalState.Submitted : state;

public static ApprovalState Approve(ApprovalState state) =>
    state == ApprovalState.Submitted ? ApprovalState.Approved : state;
```

That is not a framework. It is an explicit boundary. Tests can now state the
cases directly: submitting a draft moves it to submitted; submitting an already
submitted item leaves it submitted; approving a draft does not skip the
workflow.

The next step is often missed: keep the things this code does *not* prove in
view. It does not verify an HTTP request, save a row, deliver an event, or make
the data structure deeply immutable. Those concerns should be handled at a
declared shell boundary and tested with the appropriate tool.

In CSharpAssay Playground, the compiler checks a missing native-union case,
CSharpAssay applies a selected code policy, and application tests protect
ExpenseGuard's receipt rule. Those are different questions, so they retain
different evidence. No model suggestion can replace them.

A real detour made this concrete: changing `amount > 0` to `amount >= 0` can
leave happy-path and negative tests passing. The test that matters is zero. A
small explicit decision makes that boundary easy to see.

The project is a .NET 11 RC2 and C# preview experiment with exact pinned
tooling. It does not claim that every service, EF entity, or object-oriented
pattern should change. Start with the readable crosswalk, keep an equivalent
test suite, and expand only when a specific decision is genuinely simpler.

Try one example and its limits:
https://github.com/CanonFlow-Assay/CSharpAssay.Playground/tree/main/samples/30-gof-functional-crosswalk

_AI-assisted draft. The examples demonstrate a selected functional C# policy,
not universal immutability or whole-application correctness._

### Reddit profile — request a concrete critique

**Title:** A small functional-C# conversion question: which boundary would you
test first?

**Proposed text**

I maintain CSharpAssay Playground and am trying to keep a functional-C# example
honest rather than presenting it as a rewrite recipe.

The smallest sample changes an approval decision into explicit states and pure
transitions. It leaves HTTP, EF/persistence, and other effects at declared
boundaries. Shared tests compare the classic and refined behavior for the
declared cases.

The detours are the point: a record is not deep immutability; a green suite can
miss a zero boundary; an advisor/model suggestion cannot make incomplete
evidence pass; and the historical eShop case study is not a port or a claim that
eShop is architecturally wrong.

The repository uses an exact .NET 11 RC2/C# preview lane, so I would especially
value criticism of the *method*, not enthusiasm for a broad migration.

If you were reviewing this conversion, which test or boundary would you demand
before believing it improved the code: invalid transitions, persistence mapping,
concurrency, serialization, dependency injection, or something else?

Source and limits: https://github.com/CanonFlow-Assay/CSharpAssay.Playground/tree/main/samples/30-gof-functional-crosswalk

_AI-assisted draft; profile post only, with no subreddit submission or
cross-post proposed._

## Publication preflight

- Verify that the current public `main` still contains the linked GoF and
  ExpenseGuard paths. Do not point at a feature branch or promise the draft is
  itself deployed.
- Keep the SDK wording as “pinned .NET 11 RC2/C# preview lane”; do not call it
  stable, final, production-qualified, a current Microsoft support statement,
  or a benchmark.
- Keep the eShop wording historical and bounded. Do not claim a port, upstream
  endorsement, parity, or broad eShop assessment.
- Publish at most these four distinct account-native adaptations, one at a
  time, only after explicit final confirmation. Read back the public result.
  Stop immediately on an account, rate-limit, restriction, or policy warning.
- Do not send Substack email notifications, enable paid access, create a
  company identity, tag people, react to posts, or create automatic replies.
