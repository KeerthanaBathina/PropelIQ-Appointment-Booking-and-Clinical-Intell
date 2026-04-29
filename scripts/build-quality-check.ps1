<#
.SYNOPSIS
    Full quality gate pipeline: build (zero warnings) + test execution + coverage threshold.
    Exits non-zero on any gate failure (US_098, AC-4, TR-033, TR-034).

.DESCRIPTION
    Gate 1 — Build with TreatWarningsAsErrors: all CS*/SA*/CA* warnings are compile errors.
    Gate 2 — StyleCop/Analyzer validation: enforced via TreatWarningsAsErrors in Gate 1.
    Gate 3 — Test execution: dotnet test must pass all discovered tests.
    Gate 4 — Coverage threshold: delegates to scripts/run-tests.ps1 from US_097.

.PARAMETER CoverageThreshold
    Minimum line-coverage percentage for UPACIP.Service and UPACIP.Api. Default: 80.

.PARAMETER SkipTests
    Skip Gates 3 and 4. Useful for a fast build-only check.

.PARAMETER SkipCoverage
    Skip Gate 4 only. Tests still run; coverage report is not enforced.

.PARAMETER Configuration
    MSBuild configuration. Default: Release.

.EXAMPLE
    pwsh scripts/build-quality-check.ps1 -CoverageThreshold 80
    pwsh scripts/build-quality-check.ps1 -SkipTests -SkipCoverage
#>
param(
    [int]    $CoverageThreshold = 80,
    [switch] $SkipTests,
    [switch] $SkipCoverage,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "       UPACIP QUALITY GATE PIPELINE       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Configuration : $Configuration"
Write-Host "  CoverageThreshold: $CoverageThreshold%"
Write-Host "  SkipTests    : $SkipTests"
Write-Host "  SkipCoverage : $SkipCoverage"
Write-Host "-----------------------------------------`n"

# ── Gate 1: Zero-warning build (AC-4) ────────────────────────────────────────────────────
# TreatWarningsAsErrors=true in Directory.Build.props ensures every CS*, SA*, CA*, IDE*
# warning at 'warning' severity or above is promoted to a compile error.
Write-Host "[Gate 1/4] Building solution with zero-warning policy..." -ForegroundColor Yellow
dotnet build UPACIP.sln --configuration $Configuration --no-incremental

if ($LASTEXITCODE -ne 0)
{
    Write-Host ""
    Write-Host "FAIL [Gate 1/4]: Build failed — compiler warnings or analyzer violations detected." -ForegroundColor Red
    Write-Host "Fix all warnings/violations before committing (AC-4)." -ForegroundColor Red
    exit 1
}
Write-Host "PASS [Gate 1/4]: Solution built with zero warnings.`n" -ForegroundColor Green

# ── Gate 2: StyleCop / Roslyn analyzer validation ────────────────────────────────────────
# StyleCop.Analyzers and EnforceCodeStyleInBuild=true are configured in Directory.Build.props.
# Any naming, ordering, or spacing violation becomes a build error in Gate 1.
# This gate is therefore implicitly passed when Gate 1 passes.
Write-Host "[Gate 2/4] StyleCop / Roslyn analyzer validation..." -ForegroundColor Yellow
Write-Host "PASS [Gate 2/4]: Enforced via zero-warning build (SA*/IDE*/CA* are build errors).`n" -ForegroundColor Green

# ── Gate 3: Test execution ────────────────────────────────────────────────────────────────
if (-not $SkipTests)
{
    Write-Host "[Gate 3/4] Running all tests..." -ForegroundColor Yellow

    dotnet test UPACIP.sln `
        --configuration $Configuration `
        --no-build `
        --logger "trx;LogFileName=test-results.trx" `
        --logger "console;verbosity=normal"

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host ""
        Write-Host "FAIL [Gate 3/4]: One or more tests failed (AC-4)." -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS [Gate 3/4]: All tests passed.`n" -ForegroundColor Green
}
else
{
    Write-Host "[Gate 3/4] Tests skipped (-SkipTests).`n" -ForegroundColor Yellow
}

# ── Gate 4: Code coverage threshold ──────────────────────────────────────────────────────
if (-not $SkipCoverage -and -not $SkipTests)
{
    Write-Host "[Gate 4/4] Checking code coverage (threshold: $CoverageThreshold%)..." -ForegroundColor Yellow

    & "$PSScriptRoot/run-tests.ps1" -Threshold $CoverageThreshold

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host ""
        Write-Host "FAIL [Gate 4/4]: Code coverage below $CoverageThreshold% threshold (AC-4)." -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS [Gate 4/4]: Code coverage meets or exceeds $CoverageThreshold%.`n" -ForegroundColor Green
}
else
{
    Write-Host "[Gate 4/4] Coverage check skipped.`n" -ForegroundColor Yellow
}

# ── Summary ───────────────────────────────────────────────────────────────────────────────
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "       ALL QUALITY GATES PASSED           " -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "CI integration example:" -ForegroundColor Gray
Write-Host "  GitHub Actions : pwsh scripts/build-quality-check.ps1 -CoverageThreshold 80" -ForegroundColor Gray
Write-Host "  Azure DevOps   : PowerShell@2 filePath: scripts/build-quality-check.ps1" -ForegroundColor Gray
exit 0
