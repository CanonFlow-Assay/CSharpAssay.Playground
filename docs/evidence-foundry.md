# Evidence foundry

This repository is a public transformation ledger, not a hall of shame. A case
study exists to show exactly what CSharpAssay observed, what humans decided,
what behavior was protected, and what remains unknown.

Every imported case study must include:

- repository URL, immutable commit, license, and hashes for imported files;
- a bounded reason for selection and an explicit non-goal;
- runnable characterization evidence before structural changes;
- provisional JSON/SARIF before refinement;
- human adjudication for each finding and each known detector blind spot;
- a reviewable derivative rather than edits to the attributed upstream copy;
- an authoritative refined report only when project, policy, target-framework,
  analyzer, suppression, and configured-test evidence is complete;
- a plain statement of remaining behavior, ecosystem, performance, security,
  and architectural unknowns.

The public scorecard is evidence completeness, reproducibility, and retained
behavior—not how many findings an upstream project has. Credit maintainers and
avoid ranking repositories or people.

The first expansion target is the queued Racing Car Katas revision. A larger
reference application should remain observation-only until its build,
dependencies, test reporter, and framework boundaries are qualified.
