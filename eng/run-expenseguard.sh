#!/usr/bin/env bash
set -euo pipefail
expense_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$expense_root"
: "${ASSAY_RUNNER:?Set ASSAY_RUNNER to the qualified .NET 11 cs-assay.dll}"
dotnet build eng/Assay.Tooling/Assay.Tooling.csproj -c Release -m:2 -nodeReuse:false
exec ./eng/tooling with-inventory-database -- dotnet eng/Assay.Tooling/bin/Release/net11.0/Assay.Tooling.dll qualify-expenseguard "$@"
