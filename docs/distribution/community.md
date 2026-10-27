# Community material — DRAFT / permission pending

Audience: relevant C# communities. Claim: C02/C05. Evidence: [recorded examples](../examples/catalogue.json) and exact sample source. Action: reproduce and critique the authority boundary. Approval: DRAFT; content digest in manifest. Limits: synthetic bounded sample and native-only development lane. No messages or submissions were sent.

## r/dotnet / r/csharp candidate

Working title: “Keeping simulated AI advice separate from expense approval in a C# sample”

I maintain CSharpAssay and its Playground. ExpenseGuard is a bounded example where an offline advisor deliberately suggests approval while the domain still requires a missing receipt. No live model or real payment is involved.

The interesting split is between three detectors: business tests encode the expense requirements, the compiler checks native-union exhaustiveness, and CSAssay applies selected admitted code policy and records configured verification evidence. The recorded explorer marks edited code Unverified rather than pretending stored findings are a fresh scan.

The sample also has operational tradeoffs: PostgreSQL transactions, expected versions, receipt integrity, retry/manual-review behavior and local build binding. Hash chains and a green report do not certify security or complete business correctness. The inspected native development build uses an exact RC2 SDK and local-feed packages; current support/release qualification is a separate task.

Runnable starting source: https://github.com/CanonFlow-Assay/CSharpAssay.Playground/tree/ec1304e9c9d379a7ae72b0ab65f35d64cc9874df/samples/100-expenseguard

For teams using coding agents, is this separation between advice, domain authority and verification evidence useful? Which boundary would you want the example to make more explicit?

Before posting, refresh the source to the actual reviewed public revision containing the new explorer and verify the appropriate community's rules. The older runnable sample link does not claim the unreleased explorer is already public.

| Community | Rules URL / check date | Current permission status |
| --- | --- | --- |
| r/dotnet | https://www.reddit.com/r/dotnet/about/rules/ · 27 October 2026 | Fetched page did not expose rule details; promotion/allowed format must be confirmed |
| r/csharp | https://www.reddit.com/r/csharp/about/rules/ · 27 October 2026 | Same gap; no submission permission established |
| r/ExperiencedDevs | https://www.reddit.com/r/ExperiencedDevs/about/rules/ | Not checked; only a substantive permitted architecture discussion |
| r/LocalLLaMA | https://www.reddit.com/r/LocalLLaMA/about/rules/ | Not checked; no local-model result claimed from a stub |

No duplicate posts, coordinated votes, manufactured comments, purchased accounts, moderator bypass or unagreed AMA. The AI-assisted draft may be ineligible under a venue's current policy; do not submit it just because affiliation is disclosed.

## HN author-preparation sheet — intentionally not submission copy

The owner's later supplied instructions and current [HN rules](https://news.ycombinator.com/newsguidelines.html) prohibit generated submission text and generated/AI-edited comments. This overrides the older brief's request for a paste-ready Show HN draft. The owner must independently write and manually submit their own text. No generated title/body/comment is supplied here.

Facts to personally verify before writing: usable trial path; exact source/package/SDK; actual detector and result; selected policy limits; simulated default advisor/payment; missing evidence behavior. Show HN requires something people can try, not only a landing page or newsletter. Choose a time the maintainer can answer genuine questions. Do not solicit votes or resubmit for attention. [Show HN](https://news.ycombinator.com/showhn.html).

## Beginner adaptation — Hashnode candidate; DEV reuse restricted

Start with one ordinary C# record from the canonical guide. A record gives useful value-oriented behavior, but a public setter can still change a property. Init restricts later assignment; it is not deep immutability. A collection inside the record deserves its own consideration.

Next, explain outcomes. An expense may need more information, require approval or be rejected. Closed alternatives can make those cases explicit. The supported C# compiler checks an exhaustive switch; CSAssay does not replace that compiler responsibility. Use the current native-union fixture rather than older C# 11 terminology or a library-based union representation.

Then select the recorded before/after variant, read the named diagnostic, its scope and its limit. The browser shows stored execution, not your code being compiled. Make an edit and observe Unverified. Reset to the recorded input, then follow the local tutorial to reproduce it with the real tool.

Finally, explain the shell: HTTP input, persistence and framework contracts may remain effectful or nullable at a declared boundary, with explicit conversion into the core. Do not hide arbitrary domain behavior there just to pass checks. Keep the example small and respectful; there is no fictional “Dave” customer or caricature of senior developers.

This outline can become a human-reviewed Hashnode tutorial after checking its current policies and canonical-source facilities. It is not approved for DEV promotional reuse: [DEV's AI guidance](https://dev.to/guidelines-for-ai-assisted-articles-on-dev) restricts AI-assisted promotion/brand building and generated comments. A future eligible educational piece requires a separate decision, factual ownership and appropriate disclosure; disclosure alone is not permission.
