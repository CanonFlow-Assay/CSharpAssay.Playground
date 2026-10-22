#!/usr/bin/env bash
set -euo pipefail
workflow_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$workflow_root"
workflow_sdk_revision="$(./eng/tooling sdk-source)"
dotnet restore Assay.Workflow.slnx --force-evaluate
[[ "$(./eng/tooling sdk-source)" == "$workflow_sdk_revision" ]]
dotnet build Assay.Workflow.slnx --no-restore -c Release -m:2 -nodeReuse:false
dotnet pack src/Assay.Workflow.Domain/Assay.Workflow.Domain.csproj --no-build --no-restore -c Release --output .packages -p:SourceRevisionId="$workflow_sdk_revision" -p:RepositoryCommit="$workflow_sdk_revision"
dotnet pack samples/50-decision-assistance/src/DecisionAssist.Domain/DecisionAssist.Domain.csproj --no-build --no-restore -c Release --output .packages -p:SourceRevisionId="$workflow_sdk_revision" -p:RepositoryCommit="$workflow_sdk_revision"
./eng/tooling normalize-preview-package .packages/CanonFlow.Assay.Workflow.0.1.0-preview.1.nupkg .packages/CanonFlow.DecisionAssist.0.1.0-preview.1.nupkg
# Invalidate only generated SDK cache entries owned by this checkout. Consumer
# locks must then be refreshed deliberately against this committed producer.
rm -rf .packages/cache/canonflow.assay.workflow/0.1.0-preview.1 .packages/cache/canonflow.decisionassist/0.1.0-preview.1
echo 'Application workflow built; local contract package prepared. Nothing published.'
