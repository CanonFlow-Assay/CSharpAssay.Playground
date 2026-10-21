# Local Forgejo review, then remote Git

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
`238b56a99b1d7be92e9523262bd55cb9f4b26202` rather than assuming the new `assess` command already exists on
engine main. `assay_ref` / `CSHARP_ASSAY_REF` can select another deliberately
reviewed matching revision. Publish the engine feature branch before the
Playground external PR so that checkout can resolve this commit.

The local checkpoint recorded in `application-workflow.md` remains scoped:
301 C# tests passed, six invalid toolchain settings were rejected, and Polly /
new-application mutation runs retain survivors. Engine release qualification is
incomplete: analyzer p95 was 148.641 ms against the unchanged 50 ms budget.
Local and external PRs must retain this gap; neither review record grants Pass
or release approval.
