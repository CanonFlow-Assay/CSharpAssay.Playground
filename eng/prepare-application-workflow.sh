#!/usr/bin/env bash
set -euo pipefail
workflow_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$workflow_root"
dotnet restore Assay.Workflow.slnx --force-evaluate
dotnet build Assay.Workflow.slnx --no-restore -c Release -m:2 -nodeReuse:false
dotnet pack src/Assay.Workflow.Domain/Assay.Workflow.Domain.csproj --no-build --no-restore -c Release --output .packages
dotnet pack samples/50-decision-assistance/src/DecisionAssist.Domain/DecisionAssist.Domain.csproj --no-build --no-restore -c Release --output .packages
echo 'Application workflow built; local contract package prepared. Nothing published.'
