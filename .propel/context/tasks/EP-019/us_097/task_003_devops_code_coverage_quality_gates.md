# Task - task_003_devops_code_coverage_quality_gates

## Requirement Reference

- User Story: us_097
- Story Location: .propel/context/tasks/EP-019/us_097/us_097.md
- Acceptance Criteria:
  - AC-3: Given tests are executed, When the test suite completes, Then the system enforces 80% code coverage for critical business logic paths.
  - AC-4: Given the CI pipeline runs, When quality gates are checked, Then the build fails if code coverage drops below 80% or any test fails.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Testing | coverlet.collector | 6.x |
| Testing | ReportGenerator | 5.x |
| Testing | @playwright/test | 1.x |
| DevOps | PowerShell Scripts | 5.1+ |
| Backend | .NET 8 | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement code coverage enforcement and CI quality gates that fail the build when coverage drops below 80% for critical business logic or any test fails (AC-3, AC-4, NFR-036). This task creates a `scripts/run-tests.ps1` PowerShell script that orchestrates test execution, coverage collection (via coverlet), coverage merging across multiple test projects, HTML/Cobertura report generation (via ReportGenerator), and threshold enforcement. A `scripts/enforce-quality-gates.ps1` script parses coverage reports and exits with non-zero code if line coverage for `UPACIP.Service` (critical business logic) falls below 80%. Coverage exclusions are configured for non-critical paths (migrations, DTOs, program bootstrap). The scripts are designed for both local developer execution and CI pipeline integration.

## Dependent Tasks

- task_001_be_xunit_moq_unit_testing_infrastructure — Requires xUnit test projects with coverlet.collector configured.
- task_002_e2e_playwright_testing_infrastructure — Requires Playwright E2E test infrastructure.

## Impacted Components

- **NEW** `scripts/run-tests.ps1` — Orchestrates test execution with coverage collection and report generation
- **NEW** `scripts/enforce-quality-gates.ps1` — Parses coverage reports and enforces 80% threshold
- **NEW** `scripts/run-e2e-tests.ps1` — Orchestrates Playwright E2E test execution with retry reporting
- **MODIFY** `tests/.runsettings` — Add coverage thresholds and additional exclusion patterns
- **NEW** `.config/dotnet-tools.json` — Local tool manifest for ReportGenerator

## Implementation Plan

1. **Configure `.config/dotnet-tools.json` for ReportGenerator**: Create in `.config/dotnet-tools.json`:
   ```json
   {
     "version": 1,
     "isRoot": true,
     "tools": {
       "dotnet-reportgenerator-globaltool": {
         "version": "5.3.0",
         "commands": ["reportgenerator"]
       }
     }
   }
   ```
   This enables `dotnet tool restore` to install ReportGenerator locally without requiring global installation — ensuring consistent tooling across developer machines and CI.

2. **Enhance `tests/.runsettings` with coverage thresholds (AC-3)**: Update the `.runsettings` file from task_001 with additional configuration:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <RunSettings>
     <DataCollectionRunSettings>
       <DataCollectors>
         <DataCollector friendlyName="XPlat Code Coverage">
           <Configuration>
             <Format>cobertura</Format>
             <Exclude>
               [UPACIP.*.Tests]*,
               [UPACIP.Tests.Common]*,
               [UPACIP.ArchTests]*
             </Exclude>
             <ExcludeByFile>
               **/Migrations/**,
               **/Program.cs,
               **/obj/**
             </ExcludeByFile>
             <ExcludeByAttribute>
               System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute,
               System.CodeDom.Compiler.GeneratedCodeAttribute
             </ExcludeByAttribute>
             <SkipAutoProps>true</SkipAutoProps>
             <DeterministicReport>true</DeterministicReport>
           </Configuration>
         </DataCollector>
       </DataCollectors>
     </DataCollectionRunSettings>
   </RunSettings>
   ```
   Coverage exclusions:
   - Test assemblies (`UPACIP.*.Tests`, `UPACIP.Tests.Common`, `UPACIP.ArchTests`).
   - EF Core migrations, `Program.cs` bootstrap, build output.
   - Types decorated with `[ExcludeFromCodeCoverage]` or `[GeneratedCode]`.
   - Auto-properties (DTO properties that are trivial getters/setters).

3. **Create `scripts/run-tests.ps1` — test orchestration (AC-3, AC-4)**: Create in `scripts/run-tests.ps1`:
   ```powershell
   <#
   .SYNOPSIS
       Runs all unit tests with code coverage collection and generates reports.
   .PARAMETER Threshold
       Minimum line coverage percentage for critical business logic. Default: 80.
   .PARAMETER OutputDir
       Directory for coverage reports. Default: ./coverage-results.
   #>
   param(
       [int]$Threshold = 80,
       [string]$OutputDir = "./coverage-results"
   )

   $ErrorActionPreference = "Stop"

   # Clean previous results
   if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
   New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

   # Restore tools
   dotnet tool restore

   # Run unit tests with coverage
   $testProjects = @(
       "tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj",
       "tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj",
       "tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj"
   )

   $coverageFiles = @()
   foreach ($project in $testProjects) {
       $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
       $resultDir = Join-Path $OutputDir $projectName

       dotnet test $project `
           --configuration Release `
           --settings tests/.runsettings `
           --collect:"XPlat Code Coverage" `
           --results-directory $resultDir `
           --logger "trx;LogFileName=$projectName.trx" `
           --no-restore

       if ($LASTEXITCODE -ne 0) {
           Write-Error "Tests failed in $projectName"
           exit 1
       }

       # Find coverage file
       $coverageFile = Get-ChildItem -Path $resultDir -Recurse -Filter "coverage.cobertura.xml" |
           Select-Object -First 1
       if ($coverageFile) {
           $coverageFiles += $coverageFile.FullName
       }
   }

   # Merge coverage reports
   $mergedReports = $coverageFiles -join ";"
   dotnet reportgenerator `
       "-reports:$mergedReports" `
       "-targetdir:$OutputDir/report" `
       "-reporttypes:Html;Cobertura;TextSummary" `
       "-assemblyfilters:+UPACIP.Service;+UPACIP.Api;+UPACIP.DataAccess;-UPACIP.*.Tests;-UPACIP.Tests.Common;-UPACIP.ArchTests"

   # Display summary
   $summaryFile = Join-Path $OutputDir "report/Summary.txt"
   if (Test-Path $summaryFile) {
       Get-Content $summaryFile
   }

   Write-Host "`nCoverage report generated at: $OutputDir/report/index.html"

   # Enforce quality gate
   & "$PSScriptRoot/enforce-quality-gates.ps1" -CoverageDir "$OutputDir/report" -Threshold $Threshold
   ```
   Orchestration flow:
   1. Clean previous coverage results.
   2. Restore .NET local tools (ReportGenerator).
   3. Execute each test project with coverlet coverage collection.
   4. Fail immediately if any test project has failures (AC-4).
   5. Merge Cobertura XML files from all projects.
   6. Generate HTML + Cobertura + TextSummary reports via ReportGenerator.
   7. Call `enforce-quality-gates.ps1` for threshold validation.

4. **Create `scripts/enforce-quality-gates.ps1` — coverage threshold enforcement (AC-3, AC-4)**: Create in `scripts/enforce-quality-gates.ps1`:
   ```powershell
   <#
   .SYNOPSIS
       Enforces code coverage quality gates. Fails with non-zero exit code if thresholds are not met.
   .PARAMETER CoverageDir
       Directory containing the merged Cobertura report.
   .PARAMETER Threshold
       Minimum line coverage percentage. Default: 80.
   #>
   param(
       [string]$CoverageDir = "./coverage-results/report",
       [int]$Threshold = 80
   )

   $ErrorActionPreference = "Stop"

   $coberturaFile = Join-Path $CoverageDir "Cobertura.xml"
   if (-not (Test-Path $coberturaFile)) {
       Write-Error "Coverage report not found at $coberturaFile"
       exit 1
   }

   [xml]$coverage = Get-Content $coberturaFile
   $overallLineRate = [math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
   $overallBranchRate = [math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)

   Write-Host "========================================="
   Write-Host "       CODE COVERAGE QUALITY GATE        "
   Write-Host "========================================="
   Write-Host "Overall Line Coverage:   $overallLineRate%"
   Write-Host "Overall Branch Coverage: $overallBranchRate%"
   Write-Host "Threshold:               $Threshold%"
   Write-Host "========================================="

   # Check per-assembly coverage for critical business logic
   $failed = $false
   $packages = $coverage.coverage.packages.package
   foreach ($pkg in $packages) {
       $name = $pkg.name
       $lineRate = [math]::Round([double]$pkg.'line-rate' * 100, 2)

       # Enforce threshold on critical assemblies only
       $criticalAssemblies = @("UPACIP.Service", "UPACIP.Api")
       if ($criticalAssemblies -contains $name) {
           $status = if ($lineRate -ge $Threshold) { "PASS" } else { "FAIL" }
           $color = if ($status -eq "PASS") { "Green" } else { "Red" }
           Write-Host "$status  $name  ${lineRate}%" -ForegroundColor $color

           if ($lineRate -lt $Threshold) {
               $failed = $true
           }
       } else {
           Write-Host "SKIP  $name  ${lineRate}% (non-critical)" -ForegroundColor Yellow
       }
   }

   Write-Host "========================================="

   if ($failed) {
       Write-Error "QUALITY GATE FAILED: One or more critical assemblies below ${Threshold}% line coverage."
       exit 1
   }

   Write-Host "QUALITY GATE PASSED: All critical assemblies meet ${Threshold}% coverage threshold." -ForegroundColor Green
   exit 0
   ```
   Quality gate logic:
   - Parses the merged Cobertura XML report.
   - Evaluates line coverage per assembly.
   - Enforces 80% threshold on critical assemblies (`UPACIP.Service`, `UPACIP.Api`) per NFR-036.
   - Skips non-critical assemblies (`UPACIP.DataAccess` — entity/context code with limited logic).
   - Exits with non-zero code on failure → CI pipeline treats as build failure (AC-4).

5. **Create `scripts/run-e2e-tests.ps1` — Playwright E2E execution with flaky test reporting**: Create in `scripts/run-e2e-tests.ps1`:
   ```powershell
   <#
   .SYNOPSIS
       Runs Playwright E2E tests with flaky test detection and separate reporting.
   .PARAMETER Browser
       Browser to test against. Default: all (chromium, firefox, webkit).
   #>
   param(
       [string]$Browser = "all"
   )

   $ErrorActionPreference = "Stop"

   Push-Location "e2e"

   try {
       # Install dependencies if needed
       if (-not (Test-Path "node_modules")) {
           npm ci
           npx playwright install --with-deps
       }

       # Run tests
       $projectArg = if ($Browser -ne "all") { "--project=$Browser" } else { "" }
       $env:CI = "true"

       npx playwright test $projectArg

       $exitCode = $LASTEXITCODE

       # Parse results for flaky test detection
       $resultsFile = "test-results/results.json"
       if (Test-Path $resultsFile) {
           $results = Get-Content $resultsFile | ConvertFrom-Json

           $total = $results.suites | ForEach-Object { $_.specs } |
               Measure-Object | Select-Object -ExpandProperty Count
           $flaky = $results.suites | ForEach-Object { $_.specs } |
               Where-Object { $_.tests | Where-Object { $_.status -eq "flaky" } } |
               Measure-Object | Select-Object -ExpandProperty Count

           Write-Host "========================================="
           Write-Host "       E2E TEST RESULTS                  "
           Write-Host "========================================="
           Write-Host "Total Tests: $total"
           Write-Host "Flaky Tests: $flaky"

           if ($flaky -gt 0) {
               Write-Host "WARNING: $flaky flaky test(s) detected. Review playwright-report for details." -ForegroundColor Yellow
           }
           Write-Host "========================================="
       }

       if ($exitCode -ne 0) {
           Write-Error "E2E tests failed. See playwright-report/ for details."
           exit 1
       }

       Write-Host "E2E tests passed." -ForegroundColor Green
   }
   finally {
       Pop-Location
   }
   ```
   Flaky test handling (edge case 2):
   - Playwright's built-in retry (max 2) automatically retries failed tests.
   - Tests that pass on retry are marked as `flaky` in the JSON report.
   - Script counts and reports flaky tests separately from genuine failures.
   - Flaky tests are logged as warnings — they pass the build but alert developers.

6. **Add `[ExcludeFromCodeCoverage]` attribute convention documentation**: Define which classes should be excluded from coverage metrics (not counted toward 80% threshold):
   - EF Core entity configurations (`IEntityTypeConfiguration<T>` implementations).
   - Program.cs bootstrap code.
   - DTOs with only auto-properties (already handled by `SkipAutoProps`).
   - Generated code (migrations, scaffold output).

   Developers apply `[ExcludeFromCodeCoverage]` to intentionally excluded classes:
   ```csharp
   [ExcludeFromCodeCoverage]
   public class PatientConfiguration : IEntityTypeConfiguration<Patient> { ... }
   ```
   All other Service and Api layer code must meet 80% line coverage (AC-3).

7. **Create CI integration example**: Document the CI pipeline integration pattern for the quality gates. The scripts are designed to run in any CI system (Azure DevOps, GitHub Actions):
   ```yaml
   # Example GitHub Actions step
   - name: Run Unit Tests with Coverage
     run: pwsh scripts/run-tests.ps1 -Threshold 80

   - name: Run E2E Tests
     run: pwsh scripts/run-e2e-tests.ps1

   - name: Upload Coverage Report
     uses: actions/upload-artifact@v4
     with:
       name: coverage-report
       path: coverage-results/report/
   ```
   CI integration points:
   - `run-tests.ps1` exits non-zero if any test fails OR coverage < 80% → CI step fails (AC-4).
   - `run-e2e-tests.ps1` exits non-zero if any E2E test fails (after retries) → CI step fails (AC-4).
   - Coverage HTML report uploaded as artifact for manual inspection.

8. **Define critical business logic paths for coverage tracking (AC-3)**: Define which namespaces constitute "critical business logic" for the 80% threshold:
   - `UPACIP.Service.AuditLog.*` — CQRS audit log operations.
   - `UPACIP.Service.Import.*` — CSV import engine.
   - `UPACIP.Service.Compliance.*` — HIPAA compliance verification.
   - `UPACIP.Service.PatientRights.*` — Patient data export/deletion.
   - `UPACIP.Service.Recovery.*` — Recovery target monitoring.
   - `UPACIP.Api.Controllers.*` — API controller request handling.
   - `UPACIP.Api.Middleware.*` — Middleware pipeline (correlation, security).

   Non-critical (excluded or relaxed threshold):
   - `UPACIP.DataAccess.Migrations.*` — Auto-generated.
   - `UPACIP.DataAccess.Configurations.*` — Entity configuration boilerplate.
   - `UPACIP.Contracts.*` — DTOs and interfaces (no logic to test).

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── .config/
├── src/
│   ├── UPACIP.Api/
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── tests/
│   ├── UPACIP.ArchTests/
│   ├── UPACIP.Service.Tests/                        ← from task_001
│   │   ├── UPACIP.Service.Tests.csproj
│   │   └── AuditLog/AuditLogCommandServiceTests.cs
│   ├── UPACIP.Api.Tests/                            ← from task_001
│   │   ├── UPACIP.Api.Tests.csproj
│   │   └── Controllers/AuditLogControllerTests.cs
│   ├── UPACIP.Tests.Common/                         ← from task_001
│   │   ├── Fixtures/DbContextFixture.cs
│   │   └── Mocks/
│   └── .runsettings                                 ← from task_001
├── e2e/                                             ← from task_002
│   ├── package.json
│   ├── playwright.config.ts
│   ├── fixtures/
│   └── tests/
├── scripts/
├── app/
├── config/
└── Server/
```

> Assumes task_001 (xUnit/Moq infrastructure) and task_002 (Playwright infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | scripts/run-tests.ps1 | Test orchestration: run all projects, collect coverage, generate reports |
| CREATE | scripts/enforce-quality-gates.ps1 | Coverage threshold enforcement: parse Cobertura, fail below 80% |
| CREATE | scripts/run-e2e-tests.ps1 | Playwright E2E execution with flaky test detection and reporting |
| CREATE | .config/dotnet-tools.json | Local tool manifest for ReportGenerator |
| MODIFY | tests/.runsettings | Add ExcludeByAttribute, DeterministicReport, refined exclusion patterns |

## External References

- [coverlet — Code Coverage for .NET](https://github.com/coverlet-coverage/coverlet)
- [ReportGenerator — Coverage Report Tool](https://github.com/danielpalme/ReportGenerator)
- [Code Coverage — .NET Testing](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage)
- [ExcludeFromCodeCoverage — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.excludefromcodecoverageattribute)
- [Playwright — CI/CD Integration](https://playwright.dev/docs/ci)

## Build Commands

```powershell
# Restore .NET tools (ReportGenerator)
dotnet tool restore

# Run all unit tests with coverage and quality gate enforcement
pwsh scripts/run-tests.ps1 -Threshold 80

# Run E2E tests
pwsh scripts/run-e2e-tests.ps1

# Run quality gate check only (after tests already ran)
pwsh scripts/enforce-quality-gates.ps1 -Threshold 80

# View coverage report
Start-Process coverage-results/report/index.html
```

## Implementation Validation Strategy

- [ ] `dotnet tool restore` installs ReportGenerator from local manifest
- [ ] `scripts/run-tests.ps1` runs all test projects and generates merged coverage report (AC-3)
- [ ] Cobertura XML contains per-assembly line-rate values
- [ ] `scripts/enforce-quality-gates.ps1` exits 0 when coverage >= 80% (AC-3)
- [ ] `scripts/enforce-quality-gates.ps1` exits 1 when coverage < 80% (AC-4)
- [ ] Critical assemblies (UPACIP.Service, UPACIP.Api) are individually checked
- [ ] Non-critical assemblies (DataAccess, Contracts) are skipped in threshold check
- [ ] Test failures cause non-zero exit code before reaching coverage check (AC-4)
- [ ] `scripts/run-e2e-tests.ps1` reports flaky test count separately from failures
- [ ] Coverage HTML report is browsable at coverage-results/report/index.html

## Implementation Checklist

- [ ] Create .config/dotnet-tools.json with ReportGenerator tool manifest
- [ ] Enhance tests/.runsettings with ExcludeByAttribute and refined exclusions
- [ ] Create scripts/run-tests.ps1 orchestrating test execution and coverage merge
- [ ] Create scripts/enforce-quality-gates.ps1 parsing Cobertura and enforcing 80%
- [ ] Create scripts/run-e2e-tests.ps1 with Playwright execution and flaky reporting
- [ ] Verify non-zero exit codes propagate for test failures and coverage gaps
- [ ] Define critical vs non-critical assembly classification for threshold
- [ ] Document CI integration pattern for quality gate scripts
