<#
.SYNOPSIS
    Parses a merged Cobertura XML coverage report and enforces per-assembly line coverage thresholds.

.DESCRIPTION
    Quality gate logic (AC-3, AC-4, NFR-036):
      - Reads the Cobertura.xml produced by ReportGenerator.
      - Evaluates line coverage for every assembly in the report.
      - Enforces the threshold ONLY for critical assemblies (UPACIP.Service, UPACIP.Api).
      - Skips non-critical assemblies (UPACIP.DataAccess, UPACIP.Contracts).
      - Exits with code 1 if any critical assembly is below the threshold.
      - Exits with code 0 when all critical assemblies pass.

    CI integration (AC-4):
      A non-zero exit code causes any standard CI step to mark the build as failed,
      preventing deployment when coverage regresses.

.PARAMETER CoverageDir
    Directory containing the merged Cobertura.xml (output of ReportGenerator).
    Default: ./coverage-results/report

.PARAMETER Threshold
    Minimum required line coverage percentage (0–100). Default: 80.

.EXAMPLE
    pwsh scripts/enforce-quality-gates.ps1
    pwsh scripts/enforce-quality-gates.ps1 -Threshold 85
    pwsh scripts/enforce-quality-gates.ps1 -CoverageDir ./coverage-results/report -Threshold 80
#>
param(
    [string] $CoverageDir = "./coverage-results/report",
    [int]    $Threshold   = 80
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate the merged Cobertura report ───────────────────────────────────────────────
$coberturaFile = Join-Path $CoverageDir "Cobertura.xml"
if (-not (Test-Path $coberturaFile)) {
    Write-Error "Coverage report not found at '$coberturaFile'. Run scripts/run-tests.ps1 first."
    exit 1
}

# ── Parse XML ────────────────────────────────────────────────────────────────────────
[xml]$coverageXml = Get-Content $coberturaFile -Encoding UTF8

$overallLineRate   = [math]::Round([double]$coverageXml.coverage.'line-rate'   * 100, 2)
$overallBranchRate = [math]::Round([double]$coverageXml.coverage.'branch-rate' * 100, 2)

Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "           CODE COVERAGE QUALITY GATE            " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Overall Line Coverage  : $overallLineRate%"
Write-Host "  Overall Branch Coverage: $overallBranchRate%"
Write-Host "  Threshold              : $Threshold%"
Write-Host "=================================================" -ForegroundColor Cyan

# ── Critical assembly classification (AC-3, NFR-036) ─────────────────────────────────
# These assemblies contain core business logic and must meet the coverage threshold.
$criticalAssemblies = @("UPACIP.Service", "UPACIP.Api")

# These assemblies contain entity config, migrations, and DI wiring — excluded from gating.
$skippedAssemblies  = @("UPACIP.DataAccess", "UPACIP.Contracts")

# ── Evaluate per-assembly coverage ───────────────────────────────────────────────────
$gateFailed = $false
$packages   = $coverageXml.coverage.packages.package

foreach ($pkg in $packages) {
    $name     = $pkg.name
    $lineRate = [math]::Round([double]$pkg.'line-rate' * 100, 2)

    if ($criticalAssemblies -contains $name) {
        $pass   = $lineRate -ge $Threshold
        $status = if ($pass) { "PASS" } else { "FAIL" }
        $color  = if ($pass) { "Green" } else { "Red" }
        Write-Host ("  {0,-6}{1,-40}{2,6}%  (threshold: {3}%)" -f $status, $name, $lineRate, $Threshold) `
            -ForegroundColor $color
        if (-not $pass) { $gateFailed = $true }

    } elseif ($skippedAssemblies -contains $name) {
        Write-Host ("  {0,-6}{1,-40}{2,6}%  (non-critical — not gated)" -f "SKIP", $name, $lineRate) `
            -ForegroundColor Yellow

    } else {
        # Unknown assembly: report but do not gate.
        Write-Host ("  {0,-6}{1,-40}{2,6}%  (informational)" -f "INFO", $name, $lineRate) `
            -ForegroundColor Gray
    }
}

Write-Host "=================================================" -ForegroundColor Cyan

# ── Outcome ───────────────────────────────────────────────────────────────────────────
if ($gateFailed) {
    Write-Host ""
    Write-Host "QUALITY GATE FAILED: One or more critical assemblies are below the $Threshold% line coverage threshold." `
        -ForegroundColor Red
    Write-Host "  → Review coverage-results/report/index.html for uncovered code paths." `
        -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "QUALITY GATE PASSED: All critical assemblies meet the $Threshold% coverage threshold." `
    -ForegroundColor Green
Write-Host ""
exit 0
