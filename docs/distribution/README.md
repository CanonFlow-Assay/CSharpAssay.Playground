# CSharpAssay distribution pack — DRAFT

Internal review material. No external publication, deployment, package release, outreach or schedule is authorized. All text is AI-assisted preparation requiring the owner's technical and editorial review. The code work follows the 25 October Playground/distribution brief, read in full from Downloads on 27 October. Instruction-file SHA-256: `9f4045b5d7da0d95927c82668184527d6a72d088ec9c9509e0b181b1eff25fe0`.

## One explanation and one starting example

**Functional C# for AI-assisted development. Guide the agent. Check the code. Keep the evidence.**

ExpenseGuard demonstrates the division of responsibility. AI advice is simulated by default. The domain decides, compiler/analyzers/tests detect different problems, and verification records what ran. The skill remains a separate proposal; this increment does not claim to ship one.

| Draft | Audience / supported claim | Evidence and action |
| --- | --- | --- |
| [Substack case study](substack.md) | Working C# developers; C02 advice isolation | [Recorded examples](../examples/catalogue.json); reproduce the receipt case |
| [YouTube scripts and shot list](youtube.md) | Developers who want to see the workflow; C01/C02 | Same examples; run the documented control |
| [LinkedIn drafts/carousel](linkedin.md) | Developers and engineering leads; C01/C06 | Same examples; inspect a finding and its limit |
| [Medium tutorial/comparison outline](medium.md) | Developers trying the supported lane; C01/C05 | Canonical setup plus recorded fixtures |
| [Community material](community.md) | Relevant technical communities; C02/C05 | Focused question and reproducible source; venue checks pending |
| [Functional conversion showcase](functional-conversion-showcase.md) | Functional-curious C# developers; bounded conversion path and its limits | A distinct four-channel draft; classic/refined GoF crosswalk plus explicit eShop and preview-toolchain boundaries |
| [Editorial/podcast packet](outreach.md) | Editors and hosts; C02/C06 | One engineering result and a documented tradeoff |
| [Claims register](claims.json) | Reviewers | Exact wording, detector, source paths, controls, identities and expiry conditions |
| [Readiness and rollback](qualification-and-rollout.md) | Maintainers | Qualification matrix, staged schedule, limits and rollback |

Each draft has one claim/evidence/action/limit block. `pack-manifest.json`, generated only after the pack is finalized, records content/asset digests and DRAFT approval state. It is not permission to post. Exact channel/account and bytes need later owner approval; changed bytes require a new review.

## Read the evidence honestly

The static explorer serves recorded examples. Public derivatives omit private source/tool paths and bind the retained original machine receipt by digest. Source edits invalidate the example association; there is no JavaScript analyzer or arbitrary browser compilation. Historical badges refer to their recorded producer SHA and do not track current main.

Regenerate source-bound examples from a clean producer, then stage their validated public derivatives. Staging modifies web assets and invalidates the prior current-application binding. Commit and requalify the final application before review. The original run remains a historical fixture producer; never relabel it as the later commit.

```bash
./eng/tooling with-inventory-database -- dotnet eng/Assay.Tooling/bin/Release/net11.0/Assay.Tooling.dll \
  qualify-distribution --engine-root /absolute/path/to/qualified-engine \
  --expense-evidence "$PWD/evidence/generated/expenseguard/producer" \
  --output "$PWD/evidence/generated/distribution/producer"
./eng/tooling stage-distribution --output "$PWD/evidence/generated/distribution/producer"
```

These commands require the existing exact SDK, clean committed producer source, qualified matching engine packages and a completed current ExpenseGuard qualification. They do not install an SDK, call a provider, send money or publish content.

Preparation installs the tool into a package-hash-specific directory and verifies every runtime payload entry against the copied package. Use `./eng/tooling packed-assay -- explain CSAI0001` (or `doctor`, `check`, `verify`, `assess`) for that bound executable. A same-version `dotnet tool run` resolver entry is not evidence of matching bytes. No global tool cache is deleted. A tooling-only commit may consume a completed ancestor application qualification only after its entire declared file inventory and hashes match; the original application producer SHA remains explicit and the actual bound-build probe must still pass. Any application/asset change requires requalification.

HN is deliberately an author-preparation sheet rather than generated submission copy, because its current rules prohibit that copy. DEV promotional reuse is blocked under its current AI-assisted-content guidance. The owner's later channel list takes precedence over an older generic instruction to draft for every venue alike.
