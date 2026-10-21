# CSharpAssay Playground

Read the root `AGENTS.md` and any instructions within the sample you change. They describe functional boundaries, the pinned .NET 11 toolchain, evidence rules, and publication constraints.

Start from the repository root with `./eng/try-decision-assistance.sh ../CSharpAssay`. Use `./eng/run-assay.sh ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll` for the refined verification gate after preparation. Existing GoF and Functional Shape scripts cover those samples.

Keep domain decisions immutable and effects in the shell. Never weaken policy, suppress a finding, or alter a verification verdict to accommodate model advice. Incomplete or insufficient evidence requires local review. Model calls require an explicit user request; configured credentials are not permission. Do not read credential files or print keys. Keep model calls out of CI.

Use JSON CLI output and generated evidence to substantiate claims. Read `docs/decision-assist-getting-started.md` for setup and identical-input provider evaluation. Keep changes local until the user approves a push; do not publish launch drafts.
