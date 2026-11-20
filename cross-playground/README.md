# Shared C# and F# playground

Both playground repositories carry this same catalogue and the complete
tracked source snapshots of both collections. A snapshot includes its samples,
test harnesses, build and Assay policy files, documentation, licenses, and
historical material. The native samples in each repository use their existing
paths and build commands.

| Collection | Included material |
| --- | --- |
| C# | 12 sample groups, including the archived eShop case study. The authored solution contains 48 projects. |
| F# | Enterprise examples, functional patterns, PostgreSQL wallet, Decision Assist, and the archived legacy playground. The active solution contains 35 projects. |

The [manifest](manifest.json) lists each group and the original source commit,
Git tree, archive SHA-256 and Assay producer pin for each collection. Both
Decision Assist examples retain their own behavior and test cases.

## Browse and run either collection

Run these commands from either playground repository on Linux. The checkout
command creates a fresh Git working copy of the selected snapshot and checks
its tree against the original. Keep working copies outside other .NET
repositories to avoid inherited MSBuild settings. The materialized commit
has its own identity.

    ./cross-playground/playground list
    ./cross-playground/playground checkout csharp /tmp/playground-csharp
    ./cross-playground/playground checkout fsharp /tmp/playground-fsharp

To build, run registered tests, and collect the respective Assay receipts,
supply a local Git checkout containing the producer commit named in the
manifest. Use the pinned .NET SDK, Bash, jq, and Docker. F# qualification also
uses Python 3.12+ and Node 26/npm.

    ./cross-playground/playground qualify csharp ../CSharpAssay /tmp/playground-csharp-results
    ./cross-playground/playground qualify fsharp ../FSharpAssay /tmp/playground-fsharp-results

Qualification runs in a temporary workspace outside the host repository and
saves both checkouts in the new results directory after execution. It retains
source identities, commands, exit codes, logs, and the native reports. C# runs all authored project tests and the
refined, GoF, Functional Shape, and payment saga gates. F# runs the active
solution qualification, the retained Jest suites, and the wallet mutation
exercise. Follow each extracted README for the remaining sample-specific
commands. Historical and unsupported inputs retain their documented scope.

## Check parity

    ./cross-playground/playground check

Both native CI workflows check the two archive digests and the committed
native inputs. When a native sample or build input changes, use the updated
reviewed commits to regenerate the two source snapshots and the manifest.
Exclude the cross-playground directory itself from snapshot refreshes. Copy
the same updated cross-playground directory to both repositories.
