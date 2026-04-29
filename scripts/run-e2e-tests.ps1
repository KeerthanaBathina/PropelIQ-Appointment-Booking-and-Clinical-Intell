<#
.SYNOPSIS
    Runs Playwright E2E tests with flaky test detection and separate reporting.

.DESCRIPTION
    Execution flow (AC-4, edge case 2):
      1. Install npm dependencies and browser binaries if not already present.
      2. Run Playwright tests against the specified browser project (or all browsers).
      3. Parse the JSON results file to count flaky tests (passed after retry).
      4. Report flaky tests as warnings — they pass the build but alert the team.
      5. Fail with exit code 1 if any test failed after all retries (genuine failure).

    Flaky test handling (edge case 2):
      - Playwright's retries (configured in playwright.config.ts: retries=2 in CI)
        automatically re-run failed tests up to 2 times.
      - Tests that pass on a retry are classified as "flaky" in results.json.
      - Flaky count is printed separately from genuine failures so developers can
        investigate intermittent failures without treating them as blocking.

.PARAMETER Browser
    Browser project to run. One of: chromium, firefox, webkit, all.
    Default: all (runs all three browser projects).

.PARAMETER E2EDir
    Path to the e2e/ directory. Default: ./e2e relative to the repository root.

.EXAMPLE
    pwsh scripts/run-e2e-tests.ps1
    pwsh scripts/run-e2e-tests.ps1 -Browser chromium
    pwsh scripts/run-e2e-tests.ps1 -Browser firefox
#>
param(
    [ValidateSet("all", "chromium", "firefox", "webkit")]
    [string] $Browser = "all",
    [string] $E2EDir  = "./e2e"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$e2ePath  = Join-Path $repoRoot $E2EDir

if (-not (Test-Path $e2ePath)) {
    Write-Error "E2E directory not found at '$e2ePath'."
    exit 1
}

Push-Location $e2ePath
try {

    # ── Install dependencies if needed ───────────────────────────────────────────────
    if (-not (Test-Path "node_modules")) {
        Write-Host "Installing Playwright dependencies..." -ForegroundColor Cyan
        npm ci
        if ($LASTEXITCODE -ne 0) { Write-Error "npm ci failed."; exit 1 }

        Write-Host "Installing browser binaries..." -ForegroundColor Cyan
        npx playwright install --with-deps
        if ($LASTEXITCODE -ne 0) { Write-Error "Playwright browser install failed."; exit 1 }
    }

    # ── Run Playwright tests ─────────────────────────────────────────────────────────
    # Set CI=true so playwright.config.ts applies retries=2 and headless mode.
    $env:CI = "true"

    $projectArgs = if ($Browser -ne "all") { @("--project=$Browser") } else { @() }

    Write-Host "`nRunning Playwright E2E tests (browser: $Browser)..." -ForegroundColor Cyan
    npx playwright test @projectArgs
    $playwrightExitCode = $LASTEXITCODE

    # ── Parse results for flaky test reporting (edge case 2) ──────────────────────────
    $resultsFile = "test-results/results.json"
    $totalTests  = 0
    $flakyTests  = 0
    $failedTests = 0
    $passedTests = 0

    if (Test-Path $resultsFile) {
        $results = Get-Content $resultsFile -Raw | ConvertFrom-Json

        # Flatten all spec objects from all suites (recursive helper via Where-Object)
        $allSpecs = $results.suites | ForEach-Object { $_.specs }

        $totalTests  = ($allSpecs | Measure-Object).Count
        $flakyTests  = ($allSpecs | Where-Object {
                           $_.tests | Where-Object { $_.status -eq "flaky" }
                       } | Measure-Object).Count
        $failedTests = ($allSpecs | Where-Object {
                           $_.tests | Where-Object { $_.status -eq "failed" -or $_.status -eq "unexpected" }
                       } | Measure-Object).Count
        $passedTests = $totalTests - $failedTests
    }

    Write-Host ""
    Write-Host "=================================================" -ForegroundColor Cyan
    Write-Host "              E2E TEST RESULTS                   " -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Cyan
    Write-Host ("  Total  : {0}" -f $totalTests)
    Write-Host ("  Passed : {0}" -f $passedTests)  -ForegroundColor Green

    if ($flakyTests -gt 0) {
        # Edge case 2: flaky tests are reported as warnings, NOT failures.
        Write-Host ("  Flaky  : {0}  ← passed after retry — investigate intermittent failures" -f $flakyTests) `
            -ForegroundColor Yellow
    } else {
        Write-Host ("  Flaky  : 0") -ForegroundColor Green
    }

    if ($failedTests -gt 0) {
        Write-Host ("  Failed : {0}" -f $failedTests) -ForegroundColor Red
    } else {
        Write-Host ("  Failed : 0") -ForegroundColor Green
    }

    Write-Host "=================================================" -ForegroundColor Cyan
    Write-Host "  Report : playwright-report/index.html"
    if ($flakyTests -gt 0) {
        Write-Host "  Note   : Flaky tests are visible under 'Flaky' in the HTML report." -ForegroundColor Yellow
    }
    Write-Host "=================================================" -ForegroundColor Cyan

    # ── AC-4: Fail the build on genuine test failures ────────────────────────────────
    if ($playwrightExitCode -ne 0) {
        Write-Host ""
        Write-Host "E2E QUALITY GATE FAILED: Tests failed after all retries. See playwright-report/ for details." `
            -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "E2E QUALITY GATE PASSED: All tests passed." -ForegroundColor Green
    exit 0

} finally {
    Pop-Location
}
