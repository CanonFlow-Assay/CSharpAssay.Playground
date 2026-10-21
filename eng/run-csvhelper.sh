#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
assay="${1:-../CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll}"
if [[ $# -gt 1 || ! -f "$assay" ]]; then
  echo "Usage: $0 [path/to/cs-assay.dll]; prepare local Assay packages first." >&2
  exit 64
fi
output="evidence/generated/csvhelper"
mkdir -p "$output"
# A failed new invocation must not leave a previous completed gate visible.
rm -f "$output/validation.json" "$output/verify.json" "$output/verify.sarif" \
  "$output/verify.html" "$output/verify-repeat.json" "$output/verify-repeat.sarif" \
  "$output/verify-repeat.html"
python3 eng/test-csvhelper-evidence.py
python3 eng/test-csvhelper-streaming.py
python3 eng/test-csvhelper-hefty.py
python3 eng/test-csvhelper-review.py
python3 eng/test-csvhelper-roundtrip.py
python3 eng/run-csvhelper-functional.py
python3 eng/run-csvhelper-streaming.py
python3 eng/evaluate-csvhelper-advice.py
sample="samples/60-csvhelper-functional"
for name in verify verify-repeat; do
  dotnet "$assay" verify "$sample/CsvFunctional.slnx" \
    --policy "$sample/.csassay.json" --profile native \
    --json "$output/$name.json" --sarif "$output/$name.sarif" --html "$output/$name.html"
done
cmp "$output/verify.json" "$output/verify-repeat.json"
cmp "$output/verify.sarif" "$output/verify-repeat.sarif"
python3 eng/assert-csvhelper.py
python3 eng/prepare-csvhelper-review.py
