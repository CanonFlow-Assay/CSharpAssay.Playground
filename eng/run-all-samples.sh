#!/usr/bin/env bash
set -euo pipefail
sample_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$sample_root"
mkdir -p evidence/generated/all-samples
./eng/tooling sdk-source
./eng/tooling audit-sample-toolchain
dotnet restore AllSamples.slnx --locked-mode
dotnet build AllSamples.slnx --no-restore -c Release -m:2 -nodeReuse:false
./eng/tooling with-inventory-database -- dotnet test AllSamples.slnx --no-build --no-restore -c Release --max-parallel-test-modules 1
dotnet run --project samples/10-gilded-rose/characterization/GildedRose.Characterization.csproj --no-build --no-restore -c Release
echo 'All authored samples built and tested under the strict .NET 11 toolchain. Negative controls retain their expected policy violations; run the sample evidence gates for verification authority.'
