# Decision assistance slice 1 validation

Validated locally on 18 October 2026 using the repository-pinned .NET SDK `11.0.100-rc.1.26425.128` and locally packed CsAssay `0.2.0-rc.1` packages.

## Change

The Playground adds `samples/50-decision-assistance`: a native-union domain for experiment states and advice, an offline investigation baseline, a bounded optional Jev adapter, and an isolated source-mutation experiment. Coaching and firm feedback change wording while leaving action and authority separate. Deterministic advice stays available during optional provider outages.

The refined gate now includes the new domain and CLI. The existing seven required rules and their authority remain intact. Its registered test minimum increases from 9 to 40 and its exact project/test expectations are updated to match the expanded scope.

## Observed checks

| Check | Local result |
| --- | --- |
| Full Playground Release build | Passed |
| Playground regression suite | 40 passed, zero failed or skipped; includes 31 new decision-assistance cases |
| Authoritative refined verification | Pass; 5 projects, 40 tests, zero findings, missing evidence or tool failures |
| Existing impure/upstream expectations | Passed unchanged inventories |
| Gilded Rose characterization | 8 passed |
| GoF crosswalk | 10 passed; repeated JSON/SARIF byte-identical |
| Functional Shape | 23 passed; same three expected advisories; repeated JSON/SARIF byte-identical |
| Isolated mutation | Original passes 2 weak and 3 strong checks; mutant survives 2 weak checks and fails exactly the zero-boundary check in the strong suite |
| Offline adapter behavior | Registered choices, refusal/unknown output, malformed/oversized responses, outages, missing credentials, cancellation and invalid credential handling checked |
| Optional provider unavailable | Deterministic action remains visible; experiment advice retains Inconclusive verification status |
| Feedback mode | Firm/coaching mode preserves the action and verification status |

The probe is a declared synthetic fixture, not a general mutation engine. It reports no coverage measurement. Its records bind the original, mutant, probe and build-input hashes; they remain separate from native Assay verification receipts.

## Reproduce

```sh
export DOTNET_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"
./eng/prepare-assay.sh ../CSharpAssay
./eng/run-assay.sh ../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll
./eng/run-gof-crosswalk.sh
./eng/run-functional-shape.sh
python3 eng/run-decision-assistance.py
```

When reproducing on this worktree, supply the actual sibling CSharpAssay path. Generated receipts remain in ignored `evidence/generated/` and CI uploads them as artifacts. The preparation entrypoint is now executable, matching its documented and CI invocation. Local prerelease analyzer content hashes were regenerated through the existing preparation script; package versions were not changed.

## Qualification boundary and next slice

No live Jev inference, provider accuracy benchmark, performance benchmark, or complete CSharpAssay engine test-suite run is claimed. Live requests require an explicit `--jev` invocation and the user's environment key. The preview records the requested alias; its resolved model revision is NotObserved. CI uses only offline contract fixtures and the deterministic baseline.

The next slice should qualify actual provider request/response identity on synthetic packets, pin the model identity where supported, and compare advice with deterministic/human investigation on held-out examples. Add other providers only after that contract is demonstrated. Existing user changes in the original checkout were kept separate from this worktree.
