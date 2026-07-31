# Adding a sample

Use a sample to answer one precise question. Do not clone a large repository
merely to produce an impressive finding count.

## Selection checklist

1. State the detector, boundary, or behavior question.
2. Confirm a compatible license and copy its notice where required.
3. Pin a full commit SHA; never build evidence from a moving branch.
4. Import the smallest useful scope and record upstream paths, Git blobs,
   transformations, and local SHA-256 hashes.
5. Capture existing behavior before changing production code.
6. Add both an intentionally failing control and a reviewed passing form where
   detector behavior is the subject.
7. Define exact report expectations, including authority, projects, rules,
   findings, tests, missing evidence, and failures.
8. Add the specimen to `eng/run-assay.sh` and CI.
9. Document false positives, false negatives, deferred business changes, and
   the boundary of the claim.

## Directory convention

```text
samples/NN-name/
  provenance.json
  UPSTREAM_LICENSE.txt
  upstream/          # minimally transformed pinned input
  harness/           # local build adapter
  characterization/  # behavior capture
  refined/           # reviewed derivative, if applicable
```

The harness may adapt build metadata but must not quietly edit the imported
source. If a transformation is necessary, make it reproducible and record it.

## Graduation rule

A catalog entry moves from `reference-only` or `queued-manifest-only` to an
imported status only when it has bounded scope, provenance, runnable behavior,
an adjudication record, and deterministic evidence assertions. Otherwise it is
research material, not release evidence.
