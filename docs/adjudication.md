# Adjudication method

CSharpAssay produces evidence; a reviewer decides what that evidence means in
the project’s context. This repository records both parts.

## Decision order

1. Confirm the workspace loaded, every required rule completed, and no required
   evidence is missing.
2. Separate provisional observation (`check`) from release authority
   (`verify`).
3. Confirm that policy boundaries match the intended core and shell.
4. Classify each finding as a true defect, contextual design choice, detector
   limitation, or suspected tool defect.
5. Protect behavior before structural change, then rerun the identical evidence
   contract after change.

An authoritative pass is necessary for the refined lane. This playground also
requires zero findings, because a policy can legitimately pass while reporting
non-blocking findings outside a blocking boundary.

## Current adjudication

### Controlled rule matrix

All 12 findings are intentional true positives. The matrix is detector
calibration, not application quality. If a finding disappears or a new one
appears, the evidence assertion fails until the change is understood and
reviewed.

### Gilded Rose upstream

`CSAN0001` is a true observation: the harness disables nullable analysis to
compile the pinned legacy source as received. No null-related claim beyond that
observation is made.

The public mutable setters and list in ordinary classes are not reported by the
current immutable-carrier rules. This is a scope limitation, not proof of
immutability. The refined derivative removes this design anyway and behavior
tests show the selected legacy cases are preserved.

### Conjured inventory

The requirements request faster degradation for Conjured items, while the
pinned implementation behaves like normal inventory. The refined code preserves
the implementation’s observed behavior. Adding the feature needs a distinct
decision, tests, and commit; mixing it into this refactor would make the
equivalence claim false.

### Gang of Four functional crosswalk

The five classic findings are prototype/contextual advisory observations, not
blocking defects: `CSAF0001 ×3`, `CSAD0002 ×1`, and `CSAI0003 ×1`. The refined
lane has zero observed findings and ten shared behavior cases, while the
[sample adjudication](../samples/30-gof-functional-crosswalk/ADJUDICATION.md)
records when each classic pattern remains preferable and what the evidence
does not prove.

## Evidence ownership

- `.csassay.*.json` owns the admitted rules and boundary contract.
- `evidence/expectations/reports.json` owns exact machine expectations.
- `provenance.json` owns upstream revision, license, blob, transformation, and
  local hash facts.
- characterization and xUnit tests own the demonstrated behavior cases.
- this document owns human interpretation and exceptions.

Any change to one owner should trigger review of the others.
