#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
assay_root="${1:-$repo_root/../CSharpAssay}"
assay_root="$(cd "$assay_root" && pwd)"
preparation_mode="${2:-}"
if [[ $# -gt 2 || ( -n "$preparation_mode" && "$preparation_mode" != '--qualified' && "$preparation_mode" != '--bootstrap-locks' ) ]]; then
  echo 'Usage: prepare-assay.sh [engine-checkout] [--qualified|--bootstrap-locks]' >&2
  exit 64
fi
if [[ "$preparation_mode" == '--bootstrap-locks' && -n "$(git -C "$repo_root" status --porcelain --untracked-files=all)" ]]; then
  echo 'Bootstrap requires a clean disposable checkout; existing work is not committed.' >&2
  exit 65
fi
commit_bootstrap_locks() {
  local lock_message="$1"
  shift
  if [[ "$preparation_mode" == '--bootstrap-locks' ]] && ! git diff --quiet -- "$@"; then
    git -c user.name='Assay bootstrap' -c user.email='assay-bootstrap@localhost.invalid' \
      -c commit.gpgsign=false commit --only -m "$lock_message" -- "$@"
  fi
}
if [[ "$preparation_mode" == '--qualified' ]]; then
  (cd "$assay_root" && dotnet eng/CsAssay.Qualification/bin/Release/net11.0/CsAssay.Qualification.dll copy-qualified --destination "$repo_root/.packages")
else
  cd "$assay_root"
  dotnet restore CSharpAssay.slnx --locked-mode
  dotnet build CSharpAssay.slnx --no-restore --configuration Release
  dotnet pack src/CsAssay.Analyzers --no-build --no-restore --configuration Release --output "$repo_root/.packages"
  dotnet pack src/CsAssay.Runner --no-build --no-restore --configuration Release --output "$repo_root/.packages"
  dotnet eng/CsAssay.PackageAudit/bin/Release/net11.0/CsAssay.PackageAudit.dll \
    "$repo_root/.packages/engine-provenance" "$(git rev-parse HEAD)" "$(dotnet --version)" unsigned normalize \
    "$repo_root/.packages/CsAssay.Analyzers.0.2.0-rc.1.nupkg" "$repo_root/.packages/CsAssay.Tool.0.2.0-rc.1.nupkg"
fi
cd "$repo_root"
export NUGET_PACKAGES="$repo_root/.packages/cache"
# Local prerelease packages change as the paired source revision changes.
# Invalidate only this checkout's generated Assay cache, never a user's cache.
rm -rf "$NUGET_PACKAGES/csassay.analyzers/0.2.0-rc.1" \
       "$NUGET_PACKAGES/csassay.tool/0.2.0-rc.1"
dotnet tool restore --configfile NuGet.config
# Bootstrap the producer graph before restoring package-only inventory consumers.
dotnet restore Assay.Workflow.slnx --force-evaluate --configfile NuGet.config
# Explicit bootstrap records changed engine-package identities before the SDK
# freshness check. Ordinary preparation still refuses dirty producer inputs.
commit_bootstrap_locks 'Record rebuilt engine packages in disposable SDK producer locks' \
  src/Assay.Workflow.Domain/packages.lock.json \
  src/Assay.Workflow.Cli/packages.lock.json \
  samples/50-decision-assistance/src/DecisionAssist.Domain/packages.lock.json \
  eng/Assay.Tooling/packages.lock.json
./eng/prepare-application-workflow.sh
# Refresh the rebuilt local package identity across every authored project.
# The subsequent all-sample and evidence gates still require locked restore.
dotnet restore AllSamples.slnx --force-evaluate --configfile NuGet.config
commit_bootstrap_locks 'Record rebuilt preview SDK packages in disposable consumer locks' \
  ':(glob)**/packages.lock.json'
if [[ "$preparation_mode" == '--bootstrap-locks' && -n "$(git status --porcelain --untracked-files=all)" ]]; then
  echo 'Bootstrap left unexpected changes; inspect this disposable checkout.' >&2
  exit 65
fi
echo "Prepared CsAssay 0.2.0-rc.1 packages for .NET 11."
