# Local Forgejo review, then remote Git

Current undertaking, 22 October 2026: **local Forgejo only**. Do not push to
GitHub or update its PRs, checks, issues or comments. The earlier external
publication is historical. External steps below apply only to a separately
authorized future publication. The service's `LOCAL_ONLY` guard prevents its
external publisher from running for this undertaking.

The C# migration was subsequently qualified and merged through local PR #1 in
each repository, at engine `8fa9bbd` and Playground `dc39920`. The later
[agent self-adjudication](self-adjudication-2026-10-22.md) uses new review branches
and fresh receipts. Earlier author reviews and merges do not approve those fixes.

Prepare and test locally, commit on a feature branch, then push that exact commit
to the local Forgejo `forgejo` remote and open a PR against its `main` branch.
Keep `origin` as the external GitHub remote. After local review, publish the same
feature commit to `origin` and open or update one matching external PR. Never
mirror all refs, force-push, merge automatically, or publish packages as part of
this flow. Read `/home/arun/Developer/AGENTS.md` and stop on restriction warnings.

The local instance uses the official Forgejo 17.0.0 rootless image pinned by
container digest, private repositories, disabled public registration and a
loopback-only HTTP port at `http://127.0.0.1:3300`. Its Compose definition is under
`/home/arun/Developer/services/forgejo/compose.yaml`; repository data and server
configuration use separate persistent Docker volumes. Stop/start with
`docker compose -f /home/arun/Developer/services/forgejo/compose.yaml stop` / `up -d`.
Do not remove the volumes. Local sign-in details are saved outside Git in that
service directory with owner-only permissions; never include them in reports.

Forgejo Actions is disabled: the existing local qualification commands produce
the evidence. A local PR is a review record, not another executed CI run. Remote
GitHub Actions remains a separate check and must be observed independently.
Models stay offline in these gates. No automatic cross-forge push mirror exists.

For each subsequent change:

1. Inspect the branch diff, scope, source identities and existing PRs.
2. Run the affected repository gates; retain failures and survivors.
3. Commit locally and record the exact SHA in the local PR.
4. Push only the intended feature branch to `forgejo`, then create/update its PR.
5. When the session authorizes external publication, push the reviewed SHA to
   `origin` and create/update its matching PR. Consolidate remote updates.
6. Report both PR links, commit IDs and actual CI state. Leave merge/release
   decisions pending when qualification remains incomplete.

## Paired engine

The Playground CI defaults to the exact engine assessment commit
`8fa9bbd32a8fffed77a072e56bd5bc4472b3a7c1` rather than assuming the new `assess` command already exists on
engine main. `assay_ref` / `CSHARP_ASSAY_REF` can select another deliberately
reviewed matching revision. This local workflow edit has not run on GitHub.
The paired commit is currently reviewed only in local Forgejo. A separately
authorized future external publication must make that engine commit available
before requesting hosted Playground CI.

The historical 21 October checkpoint in `application-workflow.md` remains scoped:
301 C# tests passed, six invalid toolchain settings were rejected, and Polly /
new-application mutation runs retain survivors. Engine release qualification
was incomplete at that checkpoint: analyzer p95 was 148.641 ms against the
unchanged 50 ms budget. Retain that historical failure. The 22 October optimized
CLI qualification passed all seven gates and 142 tests, including 30.1319 ms p95.
This does not qualify default editor/compiler-host performance or establish
independent human approval. Neither review record grants release approval.

The current maturity qualification is recorded separately under
`evidence/generated/maturity/final/`; its exact engine qualification lives in
the paired engine's `artifacts/maturity-qualified/`. Local PR status updates
refer to those executed results. `publication.json` remains the historical
external journal; `maturity-review.json` records only the new local review.

The 22 October [maturity exercise](maturity-results.md) completed both application
loops, the executable inventory wire regression and all nine local regression
gates. Final checks passed 328 sample tests, eight characterization checks and
102 Python boundary tests across the declared groups. The paired engine passed
142 tests and all seven qualification gates. These are local automated results;
independent human review and hosted CI remain separate.

The subsequent C# tooling migration qualified engine `8fa9bbd` with all seven
release gates and 32.8255 ms p95 under the same optimized CLI runtime profile.
Its record is `artifacts/csharp-tooling-qualified/qualification.json`. To create
that dedicated record on the engine checkout, use
`./eng/qualify-local --output artifacts/csharp-tooling-qualified`. The
Playground's `prepare-assay.sh ../CSharpAssay --qualified` copies those exact
archives through the C# engine qualifier and checks their hashes.
