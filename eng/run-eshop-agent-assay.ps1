param(
    [Parameter(Mandatory = $true)]
    [string] $EShopRoot,
    [string] $AssayCommand = "cs-assay"
)

$ErrorActionPreference = "Stop"
$expectedCommit = "9b4f9434f46fdc5c1a6e9e936af2868340cdbc48"
$playgroundRoot = Split-Path -Parent $PSScriptRoot
$sampleRoot = Join-Path $playgroundRoot "samples/20-eshop-agent-assay"
$evidenceRoot = Join-Path $playgroundRoot "evidence/generated/eshop-agent"
$solutionPath = Join-Path $EShopRoot "eShop.Assay.slnx"
$projectPath = Join-Path $EShopRoot "src/Ordering.Domain/Ordering.Domain.csproj"

$actualCommit = (git -C $EShopRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw "Expected eShop commit $expectedCommit but found $actualCommit"
}

$trackedChanges = git -C $EShopRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $trackedChanges) {
    throw "The eShop tracked worktree must be clean before evidence capture."
}

if (-not (Test-Path $solutionPath)) {
    dotnet new sln --name eShop.Assay --format slnx --output $EShopRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not create focused solution." }
    dotnet sln $solutionPath add $projectPath
    if ($LASTEXITCODE -ne 0) { throw "Could not add Ordering.Domain." }
}

New-Item -ItemType Directory -Force $evidenceRoot | Out-Null
Set-Content -Path (Join-Path $evidenceRoot "upstream-commit.txt") -Value $actualCommit

dotnet restore $projectPath
if ($LASTEXITCODE -ne 0) { throw "Upstream restore failed." }
dotnet build $projectPath --no-restore
if ($LASTEXITCODE -ne 0) { throw "Upstream baseline build failed." }

& $AssayCommand doctor
if ($LASTEXITCODE -ne 0) { throw "CSharpAssay doctor failed." }

& $AssayCommand check $solutionPath `
    --policy (Join-Path $sampleRoot ".csassay.json") `
    --json (Join-Path $evidenceRoot "check.json") `
    --sarif (Join-Path $evidenceRoot "check.sarif")
$assayExit = $LASTEXITCODE
if ($assayExit -notin 0, 1, 2) {
    throw "CSharpAssay failed operationally with exit code $assayExit."
}

Write-Host "Provisional eShop evidence captured with exit code $assayExit."
