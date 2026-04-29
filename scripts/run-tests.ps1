<#
.SYNOPSIS
    Runs all .NET unit tests with code coverage collection and generates merged HTML/Cobertura reports.

.DESCRIPTION
    Orchestration flow (AC-3, AC-4):
      1. Clean previous coverage results.
      2. Restore local .NET tools (ReportGenerator via dotnet-tools.json).
      3. Execute each test project with coverlet Cobertura collection.
      4. Fail immediately if any test project reports failures (AC-4).
      5. Merge per-project Cobertura XML files via ReportGenerator.
      6. Generate HTML, Cobertura, and TextSummary reports.
      7. Delegate threshold enforcement to enforce-quality-gates.ps1.

.PARAMETER Threshold
    Minimum line coverage percentage required for critical assemblies (UPACIP.Service,
    UPACIP.Api). Default: 80 (per NFR-036 / AC-3).

.PARAMETER OutputDir
    Root directory for coverage artefacts (XML sources, merged reports).
    Default: ./coverage-results relative to the repository root.

.PARAMETER Configuration
    .NET build configuration passed to dotnet test. Default: Release.

.EXAMPLE
    pwsh scripts/run-tests.ps1
    pwsh scripts/run-tests.ps1 -Threshold 80 -OutputDir ./coverage-results
    pwsh scripts/run-tests.ps1 -Threshold 85 -Configuration Debug
#>
param(
    [int]    $Threshold     = 80,
    [string] $OutputDir     = "./coverage-results",
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve paths relative to the repository root ────────────────────────────────────
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {

# ── 1. Clean previous results ─────────────────────────────────────────────────────────
Write-Host "Cleaning previous coverage results..." -ForegroundColor Cyan
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ── 2. Restore local .NET tools (ReportGenerator) ────────────────────────────────────
Write-Host "Restoring .NET local tools..." -ForegroundColor Cyan
dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet tool restore failed."
    exit 1
}

# ── 3. Run each test project with coverage collection ────────────────────────────────
$testProjects = @(
    "tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj",
    "tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj",
    "tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj"
)

$coverageFiles = [System.Collections.Generic.List[string]]::new()

foreach ($project in $testProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    $resultDir   = Join-Path $OutputDir $projectName

    Write-Host "`nRunning tests: $projectName" -ForegroundColor Cyan

    dotnet test $project `
        --configuration $Configuration `
        --settings "tests/.runsettings" `
        --collect:"XPlat Code Coverage" `
        --results-directory $resultDir `
        --logger "trx;LogFileName=$projectName.trx" `
        --no-restore

    # AC-4: Any test failure immediately stops the pipeline.
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests FAILED in $projectName. Halting coverage pipeline."
        exit 1
    }

    # Locate the Cobertura XML produced by coverlet for this project.
    $coverageFile = Get-ChildItem -Path $resultDir -Recurse -Filter "coverage.cobertura.xml" |
        Select-Object -First 1
    if ($coverageFile) {
        $coverageFiles.Add($coverageFile.FullName)
        Write-Host "  Coverage file: $($coverageFile.FullName)" -ForegroundColor DarkGray
    } else {
        Write-Warning "  No coverage.cobertura.xml found under $resultDir"
    }
}

# ── 4. Merge and report ───────────────────────────────────────────────────────────────
if ($coverageFiles.Count -eq 0) {
    Write-Error "No coverage files collected. Ensure coverlet.collector is referenced in test projects."
    exit 1
}

$reportDir     = Join-Path $OutputDir "report"
$mergedReports = $coverageFiles -join ";"

Write-Host "`nGenerating merged coverage report..." -ForegroundColor Cyan

dotnet reportgenerator `
    "-reports:$mergedReports" `
    "-targetdir:$reportDir" `
    "-reporttypes:Html;Cobertura;TextSummary" `
    "-assemblyfilters:+UPACIP.Service;+UPACIP.Api;+UPACIP.DataAccess;-UPACIP.*.Tests;-UPACIP.Tests.Common;-UPACIP.ArchTests"

if ($LASTEXITCODE -ne 0) {
    Write-Error "ReportGenerator failed."
    exit 1
}

# ── 5. Display text summary ───────────────────────────────────────────────────────────
$summaryFile = Join-Path $reportDir "Summary.txt"
if (Test-Path $summaryFile) {
    Write-Host "`n--- Coverage Summary ---" -ForegroundColor Cyan
    Get-Content $summaryFile
}

Write-Host "`nFull HTML report: $reportDir/index.html" -ForegroundColor Cyan

# ── 6. Enforce quality gate ───────────────────────────────────────────────────────────
& "$PSScriptRoot/enforce-quality-gates.ps1" `
    -CoverageDir $reportDir `
    -Threshold   $Threshold

} finally {
    Pop-Location
}
