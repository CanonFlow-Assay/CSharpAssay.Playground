#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
assay_root="${1:-$repo_root/../CSharpAssay}"
assay_root="$(cd "$assay_root" && pwd)"
cd "$assay_root"
dotnet restore CSharpAssay.slnx --locked-mode
dotnet build CSharpAssay.slnx --no-restore --configuration Release
dotnet pack src/CsAssay.Analyzers --no-build --no-restore --configuration Release --output "$repo_root/.packages"
dotnet pack src/CsAssay.Runner --no-build --no-restore --configuration Release --output "$repo_root/.packages"
cd "$repo_root"
export NUGET_PACKAGES="$repo_root/.packages/cache"
# Local prerelease packages change as the paired source revision changes.
# Invalidate only this checkout's generated Assay cache, never a user's cache.
rm -rf "$NUGET_PACKAGES/csassay.analyzers/0.2.0-rc.1" \
       "$NUGET_PACKAGES/csassay.tool/0.2.0-rc.1"
dotnet tool restore --configfile NuGet.config
dotnet restore CSharpAssay.Playground.slnx --force-evaluate --configfile NuGet.config
echo "Prepared CsAssay 0.2.0-rc.1 packages for .NET 11."
