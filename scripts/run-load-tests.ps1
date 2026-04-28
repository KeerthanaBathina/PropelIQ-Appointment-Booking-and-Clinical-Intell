<#
.SYNOPSIS
    Runs the UPACIP load test suite: seed → start API (optional) → test → report → cleanup.

.DESCRIPTION
    Automates the full load testing workflow for US_082 task_002.
    - Seeds test data (providers, patients, slots) into the target database.
    - Optionally starts the UPACIP API server in the background.
    - Runs all NBomber load scenarios sequentially.
    - Generates HTML and JSON reports in tests/UPACIP.LoadTests/Reports/.
    - Outputs a pass/fail summary.
    - Cleans up seeded test data.

.PARAMETER BaseUrl
    URL of the running UPACIP API. Default: http://localhost:5000

.PARAMETER Scenario
    Which scenario(s) to run: all | booking | search | dashboard | mixed | scalability
    Default: all

.PARAMETER StartApi
    If specified, starts the API process before running tests and stops it afterward.

.PARAMETER SkipSeed
    Skip test data seeding (useful when data was seeded in a previous run).

.PARAMETER SkipCleanup
    Skip test data cleanup after the run (useful for debugging failed tests).

.EXAMPLE
    .\scripts\run-load-tests.ps1 -BaseUrl "http://localhost:5000"

.EXAMPLE
    .\scripts\run-load-tests.ps1 -Scenario booking -SkipSeed

.EXAMPLE
    .\scripts\run-load-tests.ps1 -StartApi -Scenario all
#>

[CmdletBinding()]
param(
    [string] $BaseUrl    = "http://localhost:5000",
    [string] $Scenario   = "all",
    [switch] $StartApi,
    [switch] $SkipSeed,
    [switch] $SkipCleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────
$RepoRoot      = Split-Path -Parent $PSScriptRoot
$LoadTestProj  = Join-Path $RepoRoot "tests\UPACIP.LoadTests\UPACIP.LoadTests.csproj"
$ApiProj       = Join-Path $RepoRoot "src\UPACIP.Api\UPACIP.Api.csproj"
$ReportDir     = Join-Path $RepoRoot "tests\UPACIP.LoadTests\Reports"

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Banner([string]$msg) {
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host ""
}

function Invoke-Step([string]$label, [scriptblock]$block) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $label..." -ForegroundColor Yellow
    & $block
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $label — Done." -ForegroundColor Green
}

# ── Step 0: Build load test project ───────────────────────────────────────────
Write-Banner "UPACIP Load Test Suite — US_082 task_002"

Invoke-Step "Building load test project" {
    dotnet build $LoadTestProj --configuration Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

# ── Step 1: Start API (optional) ───────────────────────────────────────────────
$ApiProcess = $null

if ($StartApi) {
    Invoke-Step "Starting UPACIP API" {
        $ApiProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList "run --project `"$ApiProj`" --no-build" `
            -PassThru -NoNewWindow

        Write-Host "  Waiting 15 s for API to start (PID=$($ApiProcess.Id))..."
        Start-Sleep -Seconds 15

        # Quick health check
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 5
            Write-Host "  API health check: HTTP $($resp.StatusCode)" -ForegroundColor Green
        } catch {
            Write-Warning "  API health check failed — tests may fail if API is not ready."
        }
    }
}

# ── Step 2: Seed test data ─────────────────────────────────────────────────────
if (-not $SkipSeed) {
    Invoke-Step "Seeding test data (50 providers, 5000 patients, 10000 slots)" {
        dotnet run --project $LoadTestProj --configuration Release --no-build `
            -- --seed --base-url $BaseUrl
        if ($LASTEXITCODE -ne 0) { Write-Warning "Seed reported non-zero exit — check output." }
    }
}

# ── Step 3: Run load test scenarios ───────────────────────────────────────────
$ExitCode = 0

try {
    Invoke-Step "Running scenario: $Scenario" {
        dotnet run --project $LoadTestProj --configuration Release --no-build `
            -- --scenario $Scenario --report-dir $ReportDir
        $ExitCode = $LASTEXITCODE
    }
} finally {
    # ── Step 4: Stop API (if we started it) ─────────────────────────────────
    if ($null -ne $ApiProcess -and -not $ApiProcess.HasExited) {
        Invoke-Step "Stopping UPACIP API" {
            $ApiProcess | Stop-Process -Force
            $ApiProcess.WaitForExit(5000) | Out-Null
        }
    }

    # ── Step 5: Cleanup test data ────────────────────────────────────────────
    if (-not $SkipCleanup) {
        Invoke-Step "Cleaning up seeded test data" {
            dotnet run --project $LoadTestProj --configuration Release --no-build `
                -- --cleanup
            if ($LASTEXITCODE -ne 0) { Write-Warning "Cleanup reported non-zero exit." }
        }
    }
}

# ── Step 6: Print report location ─────────────────────────────────────────────
Write-Banner "Results"

if (Test-Path $ReportDir) {
    $Reports = Get-ChildItem -Path $ReportDir -Filter "*.html" -Recurse |
               Sort-Object LastWriteTime -Descending | Select-Object -First 5
    if ($Reports) {
        Write-Host "HTML reports:" -ForegroundColor Cyan
        $Reports | ForEach-Object { Write-Host "  $($_.FullName)" }
    }

    $JsonReports = Get-ChildItem -Path $ReportDir -Filter "scalability_report_*.json" |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($JsonReports) {
        Write-Host "Scalability JSON report:" -ForegroundColor Cyan
        Write-Host "  $($JsonReports[0].FullName)"
    }
}

Write-Host ""
if ($ExitCode -eq 0) {
    Write-Host "  OVERALL RESULT: PASS" -ForegroundColor Green
} else {
    Write-Host "  OVERALL RESULT: FAIL (exit code $ExitCode)" -ForegroundColor Red
}
Write-Host ""

exit $ExitCode
