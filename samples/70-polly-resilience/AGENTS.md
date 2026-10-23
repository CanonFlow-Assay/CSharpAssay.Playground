# Polly consumer and application workflow

Follow the root AGENTS.md. The owner authorized this bounded application-assurance
preview. Keep CsvHelper frozen. Use pinned Polly packages, preserve license and
source/package identity, and exercise consumer settings/delegates rather than
claiming a functional port of the library. No GitHub fetch is needed by default.

Keep immutable native-union domain decisions separate from Polly, clocks,
transport state, timers, process execution and JSON. Preserve all findings and
qualified rules; preview modules cannot admit rules or grant release authority.
Use deterministic controlled time and faults, retain all mutation states and
survivors, and report raw evidence. Requirements must precede implementation.
Human review/provider quality remain separate. No live calls by default.

Run ./eng/run-application-workflow.sh for the complete gate. Existing Playground
gates remain required after shared API/engine changes. Do not retarget an older
application implicitly or silently overwrite scaffolding destinations.
