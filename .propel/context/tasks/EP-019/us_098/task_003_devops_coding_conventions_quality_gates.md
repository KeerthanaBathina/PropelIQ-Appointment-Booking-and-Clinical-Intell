# Task - task_003_devops_coding_conventions_quality_gates

## Requirement Reference

- User Story: us_098
- Story Location: .propel/context/tasks/EP-019/us_098/us_098.md
- Acceptance Criteria:
  - AC-3: Given C# coding conventions are configured, When a developer builds the solution, Then StyleCop or Roslyn analyzers enforce PascalCase for public members, camelCase for parameters, and async suffix for async methods.
  - AC-4: Given quality gates are configured, When the build pipeline runs, Then it fails on any compiler warning, StyleCop violation, or test failure.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Backend | StyleCop.Analyzers | 1.2.x |
| Backend | Microsoft.CodeAnalysis.NetAnalyzers | 8.x |
| DevOps | PowerShell Scripts | 5.1+ |

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

Configure C# coding convention enforcement via StyleCop.Analyzers and Roslyn analyzers, and establish zero-warning quality gates that fail the build on any violation, satisfying AC-3, AC-4, TR-033, and TR-034. This task installs `StyleCop.Analyzers` across all source projects via a shared `Directory.Build.props`, configures a `.editorconfig` with naming rules (PascalCase public members, camelCase parameters, async suffix), creates a `stylecop.json` configuration file, and sets `TreatWarningsAsErrors` to enforce zero-warning builds. A `scripts/build-quality-check.ps1` script orchestrates the full quality pipeline — build with warnings-as-errors, run all tests, and validate coverage — ensuring CI builds fail on any compiler warning, StyleCop violation, or test failure (AC-4).

## Dependent Tasks

- US_001 — Requires backend project scaffold.
- US_097 task_001 — Requires xUnit test projects for test failure detection.
- US_097 task_003 — Coordinates with coverage quality gates.

## Impacted Components

- **NEW** `Directory.Build.props` — Shared MSBuild properties: TreatWarningsAsErrors, StyleCop analyzer reference
- **NEW** `.editorconfig` — Roslyn naming conventions and code style rules
- **NEW** `stylecop.json` — StyleCop configuration: documentation rules, ordering, naming
- **NEW** `scripts/build-quality-check.ps1` — Full quality pipeline: build + test + coverage + warnings
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Verify TreatWarningsAsErrors propagation, remove conflicting NoWarn
- **MODIFY** `src/UPACIP.Service/UPACIP.Service.csproj` — Verify TreatWarningsAsErrors propagation
- **MODIFY** `src/UPACIP.DataAccess/UPACIP.DataAccess.csproj` — Verify TreatWarningsAsErrors propagation
- **MODIFY** `src/UPACIP.Contracts/UPACIP.Contracts.csproj` — Verify TreatWarningsAsErrors propagation

## Implementation Plan

1. **Create `Directory.Build.props` for solution-wide analyzer configuration**: Create at the solution root `Directory.Build.props`:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <AnalysisLevel>latest-recommended</AnalysisLevel>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
       <EnableNETAnalyzers>true</EnableNETAnalyzers>
       <CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
     </PropertyGroup>

     <!-- StyleCop Analyzers for all source projects -->
     <ItemGroup Condition="!$(MSBuildProjectName.Contains('Tests'))">
       <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.*">
         <PrivateAssets>all</PrivateAssets>
         <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       </PackageReference>
       <AdditionalFiles Include="$(MSBuildThisFileDirectory)stylecop.json" Link="stylecop.json" />
     </ItemGroup>

     <!-- Relaxed settings for test projects -->
     <PropertyGroup Condition="$(MSBuildProjectName.Contains('Tests'))">
       <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
       <NoWarn>$(NoWarn);SA1600;SA1601;SA1602;CS1591</NoWarn>
     </PropertyGroup>

     <!-- Suppress specific warnings that conflict with project conventions -->
     <PropertyGroup>
       <NoWarn>$(NoWarn);SA1101;SA1200;SA1633</NoWarn>
     </PropertyGroup>
   </Project>
   ```
   Key settings:
   - **`TreatWarningsAsErrors = true` (AC-4)**: All compiler warnings become errors — build fails on any warning.
   - **`EnforceCodeStyleInBuild = true` (AC-3)**: Roslyn code style rules from `.editorconfig` are enforced at build time, not just in IDE.
   - **`AnalysisLevel = latest-recommended`**: Uses the latest .NET analyzer rules with recommended severity.
   - **StyleCop exclusion for test projects**: Test code has relaxed documentation requirements (`SA1600` series) and allows warnings.
   - **Global suppressions**: `SA1101` (this. prefix — team preference), `SA1200` (using directives placement — conflicts with ImplicitUsings), `SA1633` (file header — not required).

2. **Create `stylecop.json` configuration**: Create at the solution root `stylecop.json`:
   ```json
   {
     "$schema": "https://raw.githubusercontent.com/DotNetAnalyzers/StyleCopAnalyzers/master/StyleCop.Analyzers/StyleCop.Analyzers/Settings/stylecop.schema.json",
     "settings": {
       "documentationRules": {
         "companyName": "UPACIP",
         "copyrightText": "",
         "xmlHeader": false,
         "documentInterfaces": true,
         "documentExposedElements": true,
         "documentInternalElements": false,
         "documentPrivateElements": false,
         "documentPrivateFields": false
       },
       "orderingRules": {
         "usingDirectivesPlacement": "outsideNamespace",
         "systemUsingDirectivesFirst": true
       },
       "namingRules": {
         "allowCommonHungarianPrefixes": false,
         "allowedHungarianPrefixes": []
       },
       "layoutRules": {
         "newlineAtEndOfFile": "require"
       },
       "readabilityRules": {},
       "spacingRules": {},
       "maintainabilityRules": {}
     }
   }
   ```
   Configuration choices:
   - **Documentation**: Required for public/exposed members only. Internal and private members exempt to reduce boilerplate.
   - **Ordering**: `using` directives outside namespace, `System` usings first.
   - **Naming**: No Hungarian notation allowed.
   - **Layout**: Require newline at end of file.

3. **Create `.editorconfig` with C# naming conventions (AC-3)**: Create at the solution root `.editorconfig`:
   ```ini
   root = true

   [*]
   indent_style = space
   indent_size = 4
   end_of_line = crlf
   charset = utf-8
   trim_trailing_whitespace = true
   insert_final_newline = true

   [*.cs]
   # Naming conventions (AC-3: PascalCase public, camelCase parameters, async suffix)

   # PascalCase for public members (methods, properties, events)
   dotnet_naming_rule.public_members_pascal_case.severity = error
   dotnet_naming_rule.public_members_pascal_case.symbols = public_members
   dotnet_naming_rule.public_members_pascal_case.style = pascal_case_style

   dotnet_naming_symbols.public_members.applicable_accessibilities = public, protected, internal, protected_internal
   dotnet_naming_symbols.public_members.applicable_kinds = method, property, event, delegate

   dotnet_naming_style.pascal_case_style.capitalization = pascal_case

   # PascalCase for types (classes, structs, interfaces, enums)
   dotnet_naming_rule.types_pascal_case.severity = error
   dotnet_naming_rule.types_pascal_case.symbols = all_types
   dotnet_naming_rule.types_pascal_case.style = pascal_case_style

   dotnet_naming_symbols.all_types.applicable_kinds = class, struct, interface, enum

   # Interfaces must start with I
   dotnet_naming_rule.interfaces_prefix.severity = error
   dotnet_naming_rule.interfaces_prefix.symbols = interfaces
   dotnet_naming_rule.interfaces_prefix.style = interface_style

   dotnet_naming_symbols.interfaces.applicable_kinds = interface

   dotnet_naming_style.interface_style.required_prefix = I
   dotnet_naming_style.interface_style.capitalization = pascal_case

   # camelCase for parameters (AC-3)
   dotnet_naming_rule.parameters_camel_case.severity = error
   dotnet_naming_rule.parameters_camel_case.symbols = parameters
   dotnet_naming_rule.parameters_camel_case.style = camel_case_style

   dotnet_naming_symbols.parameters.applicable_kinds = parameter

   dotnet_naming_style.camel_case_style.capitalization = camel_case

   # camelCase with _ prefix for private fields
   dotnet_naming_rule.private_fields_underscore.severity = error
   dotnet_naming_rule.private_fields_underscore.symbols = private_fields
   dotnet_naming_rule.private_fields_underscore.style = underscore_camel_style

   dotnet_naming_symbols.private_fields.applicable_accessibilities = private
   dotnet_naming_symbols.private_fields.applicable_kinds = field

   dotnet_naming_style.underscore_camel_style.required_prefix = _
   dotnet_naming_style.underscore_camel_style.capitalization = camel_case

   # Async suffix for async methods (AC-3)
   dotnet_naming_rule.async_methods_suffix.severity = warning
   dotnet_naming_rule.async_methods_suffix.symbols = async_methods
   dotnet_naming_rule.async_methods_suffix.style = async_suffix_style

   dotnet_naming_symbols.async_methods.applicable_kinds = method
   dotnet_naming_symbols.async_methods.required_modifiers = async

   dotnet_naming_style.async_suffix_style.required_suffix = Async
   dotnet_naming_style.async_suffix_style.capitalization = pascal_case

   # PascalCase for constants
   dotnet_naming_rule.constants_pascal_case.severity = error
   dotnet_naming_rule.constants_pascal_case.symbols = constants
   dotnet_naming_rule.constants_pascal_case.style = pascal_case_style

   dotnet_naming_symbols.constants.applicable_kinds = field
   dotnet_naming_symbols.constants.required_modifiers = const

   # Code style preferences
   csharp_style_var_for_built_in_types = false:suggestion
   csharp_style_var_when_type_is_apparent = true:suggestion
   csharp_style_var_elsewhere = false:suggestion

   csharp_prefer_braces = true:warning
   csharp_using_directive_placement = outside_namespace:error
   csharp_style_namespace_declarations = file_scoped:warning
   csharp_style_expression_bodied_methods = when_on_single_line:suggestion
   csharp_style_expression_bodied_properties = true:suggestion
   csharp_style_prefer_switch_expression = true:suggestion
   csharp_style_prefer_pattern_matching = true:suggestion
   csharp_style_prefer_not_pattern = true:suggestion

   # Formatting
   csharp_new_line_before_open_brace = all
   csharp_new_line_before_else = true
   csharp_new_line_before_catch = true
   csharp_new_line_before_finally = true

   # Analyzer severities
   dotnet_diagnostic.CA1707.severity = error          # Identifiers should not contain underscores (except private fields)
   dotnet_diagnostic.CA1716.severity = warning         # Identifiers should not match keywords
   dotnet_diagnostic.CA1822.severity = suggestion      # Mark members as static
   dotnet_diagnostic.CA2007.severity = none            # Do not directly await Task (irrelevant for ASP.NET Core)
   dotnet_diagnostic.CA1848.severity = suggestion      # Use LoggerMessage delegates
   dotnet_diagnostic.CA1062.severity = none            # Validate arguments (handled by nullable ref types)
   dotnet_diagnostic.IDE0005.severity = warning        # Remove unnecessary using directives

   [*.{json,yaml,yml}]
   indent_size = 2

   [*.md]
   trim_trailing_whitespace = false
   ```
   Key rules enforced (AC-3):
   - **PascalCase public members**: `severity = error` — public methods, properties, events must use PascalCase.
   - **camelCase parameters**: `severity = error` — all method parameters must use camelCase.
   - **Async suffix**: `severity = warning` — async methods must end with `Async` (warning level since some framework overrides like `Main` are exempt).
   - **Private field underscore prefix**: `_fieldName` convention enforced.
   - **Interface I prefix**: `IServiceName` convention enforced.
   - **File-scoped namespaces**: Preferred with warning severity.

4. **Create `scripts/build-quality-check.ps1` — full quality pipeline (AC-4)**: Create in `scripts/build-quality-check.ps1`:
   ```powershell
   <#
   .SYNOPSIS
       Runs the complete quality gate pipeline: build (zero warnings) + tests + coverage.
       Exits non-zero if any gate fails.
   #>
   param(
       [int]$CoverageThreshold = 80,
       [switch]$SkipTests,
       [switch]$SkipCoverage
   )

   $ErrorActionPreference = "Stop"
   $exitCode = 0

   Write-Host "=========================================" -ForegroundColor Cyan
   Write-Host "       QUALITY GATE PIPELINE              " -ForegroundColor Cyan
   Write-Host "=========================================" -ForegroundColor Cyan

   # Gate 1: Build with TreatWarningsAsErrors
   Write-Host "`n[Gate 1/4] Building solution (zero warnings)..." -ForegroundColor Yellow
   dotnet build UPACIP.sln --configuration Release --no-incremental 2>&1 | Tee-Object -Variable buildOutput

   if ($LASTEXITCODE -ne 0) {
       Write-Host "FAIL: Build failed — compiler warnings or errors detected." -ForegroundColor Red
       Write-Host "Fix all warnings before committing." -ForegroundColor Red
       exit 1
   }
   Write-Host "PASS: Build succeeded with zero warnings." -ForegroundColor Green

   # Gate 2: StyleCop/Analyzer violations (included in build via TreatWarningsAsErrors)
   Write-Host "`n[Gate 2/4] StyleCop/Analyzer validation..." -ForegroundColor Yellow
   # StyleCop violations are already caught in Gate 1 since TreatWarningsAsErrors is enabled.
   # Any SA* or CA* rule violation causes build failure.
   Write-Host "PASS: StyleCop/Analyzer rules enforced via build." -ForegroundColor Green

   # Gate 3: Test execution
   if (-not $SkipTests) {
       Write-Host "`n[Gate 3/4] Running all tests..." -ForegroundColor Yellow
       dotnet test UPACIP.sln --configuration Release --no-build --logger "trx" 2>&1

       if ($LASTEXITCODE -ne 0) {
           Write-Host "FAIL: One or more tests failed." -ForegroundColor Red
           exit 1
       }
       Write-Host "PASS: All tests passed." -ForegroundColor Green
   } else {
       Write-Host "`n[Gate 3/4] Tests skipped (--SkipTests)." -ForegroundColor Yellow
   }

   # Gate 4: Code coverage threshold
   if (-not $SkipCoverage -and -not $SkipTests) {
       Write-Host "`n[Gate 4/4] Checking code coverage..." -ForegroundColor Yellow
       & "$PSScriptRoot/run-tests.ps1" -Threshold $CoverageThreshold

       if ($LASTEXITCODE -ne 0) {
           Write-Host "FAIL: Code coverage below ${CoverageThreshold}%." -ForegroundColor Red
           exit 1
       }
       Write-Host "PASS: Code coverage meets threshold." -ForegroundColor Green
   } else {
       Write-Host "`n[Gate 4/4] Coverage check skipped." -ForegroundColor Yellow
   }

   Write-Host "`n=========================================" -ForegroundColor Cyan
   Write-Host "       ALL QUALITY GATES PASSED            " -ForegroundColor Green
   Write-Host "=========================================" -ForegroundColor Cyan
   exit 0
   ```
   Quality gates (AC-4):
   1. **Build with zero warnings**: `TreatWarningsAsErrors` in `Directory.Build.props` converts all CS*, SA*, CA* warnings to errors. Build fails on any violation.
   2. **StyleCop/Analyzer**: Enforced via build — no separate step needed since violations are build errors.
   3. **Test failures**: `dotnet test` exits non-zero if any test fails.
   4. **Coverage threshold**: Delegates to `run-tests.ps1` from US_097 task_003 for 80% enforcement.

5. **Configure per-project NoWarn overrides**: Some warnings need targeted suppression in specific projects:

   **`UPACIP.Api.csproj`** — suppress `CS1591` (missing XML comment) for non-public types since `GenerateDocumentationFile` is enabled:
   ```xml
   <PropertyGroup>
     <NoWarn>$(NoWarn);1591</NoWarn>
   </PropertyGroup>
   ```

   **`UPACIP.Contracts.csproj`** — same `CS1591` suppression:
   ```xml
   <PropertyGroup>
     <NoWarn>$(NoWarn);1591</NoWarn>
   </PropertyGroup>
   ```

   These suppressions are narrowly scoped — only the XML documentation warning for non-documented members. All StyleCop naming, ordering, and formatting rules remain enforced.

6. **Verify async suffix compliance (AC-3)**: Audit existing async methods to ensure they follow the `Async` suffix convention. Methods that violate:
   ```csharp
   // VIOLATION — missing Async suffix
   public async Task<Guid> Append(AuditLogEntry entry, CancellationToken ct) { ... }

   // CORRECT
   public async Task<Guid> AppendAsync(AuditLogEntry entry, CancellationToken ct) { ... }
   ```
   The `.editorconfig` rule `dotnet_naming_rule.async_methods_suffix` at `severity = warning` (promoted to error by `TreatWarningsAsErrors`) catches violations at build time. Controller action methods that are async but follow REST conventions (e.g., `GetById`) should use `[SuppressMessage]` if the async suffix would conflict with route naming conventions — but the preferred approach is to apply the Async suffix consistently.

7. **Add Roslyn analyzer package to `Directory.Build.props`**: The `Microsoft.CodeAnalysis.NetAnalyzers` package is included by default in .NET 8 SDK via `EnableNETAnalyzers`. The `.editorconfig` configures severity levels for specific CA* rules. No additional package installation needed unless upgrading analyzer rules:
   ```xml
   <!-- Already enabled via Directory.Build.props -->
   <EnableNETAnalyzers>true</EnableNETAnalyzers>
   <AnalysisLevel>latest-recommended</AnalysisLevel>
   ```

8. **Document quality gate CI integration**: The `build-quality-check.ps1` script integrates with any CI system:
   ```yaml
   # Example GitHub Actions step
   - name: Quality Gate Check
     run: pwsh scripts/build-quality-check.ps1 -CoverageThreshold 80

   # Example Azure DevOps step
   - task: PowerShell@2
     inputs:
       filePath: scripts/build-quality-check.ps1
       arguments: -CoverageThreshold 80
   ```
   The script exits non-zero on any gate failure, which CI systems interpret as a failed step. The pipeline stops immediately — developers must fix all warnings, style violations, and test failures before merging.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Logging/
│   │   │   └── SerilogConfiguration.cs              ← from task_001
│   │   ├── Swagger/
│   │   │   ├── SwaggerConfiguration.cs              ← from task_002
│   │   │   ├── ConfigureSwaggerOptions.cs
│   │   │   └── SwaggerExampleSchemaFilter.cs
│   │   └── Middleware/
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── tests/
│   ├── UPACIP.ArchTests/
│   ├── UPACIP.Service.Tests/                        ← from US_097 task_001
│   ├── UPACIP.Api.Tests/                            ← from US_097 task_001
│   └── UPACIP.Tests.Common/                         ← from US_097 task_001
├── e2e/                                             ← from US_097 task_002
├── scripts/
│   ├── run-tests.ps1                                ← from US_097 task_003
│   ├── enforce-quality-gates.ps1                    ← from US_097 task_003
│   └── run-e2e-tests.ps1                            ← from US_097 task_003
├── app/
└── config/
```

> Assumes US_001, US_097 (testing infrastructure), and task_001/task_002 of this story are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Directory.Build.props | Solution-wide: TreatWarningsAsErrors, StyleCop reference, analyzer config |
| CREATE | .editorconfig | Naming conventions: PascalCase public, camelCase params, async suffix |
| CREATE | stylecop.json | StyleCop config: documentation, ordering, naming rules |
| CREATE | scripts/build-quality-check.ps1 | Full quality pipeline: build + test + coverage gates |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Verify TreatWarningsAsErrors, CS1591 suppression |
| MODIFY | src/UPACIP.Service/UPACIP.Service.csproj | Verify TreatWarningsAsErrors propagation |
| MODIFY | src/UPACIP.DataAccess/UPACIP.DataAccess.csproj | Verify TreatWarningsAsErrors propagation |
| MODIFY | src/UPACIP.Contracts/UPACIP.Contracts.csproj | Verify TreatWarningsAsErrors, CS1591 suppression |

## External References

- [StyleCop.Analyzers — Configuration](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/Configuration.md)
- [EditorConfig — .NET Code Style Rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
- [.NET Naming Rules — EditorConfig](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules)
- [Directory.Build.props — MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)
- [Code Analysis — TreatWarningsAsErrors](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings)

## Build Commands

```powershell
# Build with zero-warning enforcement
dotnet build UPACIP.sln --configuration Release

# Run full quality gate pipeline
pwsh scripts/build-quality-check.ps1 -CoverageThreshold 80

# Build only (skip tests and coverage)
pwsh scripts/build-quality-check.ps1 -SkipTests -SkipCoverage
```

## Implementation Validation Strategy

- [ ] `dotnet build` with TreatWarningsAsErrors fails on any compiler warning (AC-4)
- [ ] StyleCop SA* violations cause build failure (AC-3, AC-4)
- [ ] PascalCase is enforced for public methods and properties (AC-3)
- [ ] camelCase is enforced for method parameters (AC-3)
- [ ] Async suffix is enforced for async methods (AC-3)
- [ ] Private fields require underscore prefix (_fieldName)
- [ ] Interfaces require I prefix (IServiceName)
- [ ] Test projects have relaxed TreatWarningsAsErrors (documentation not required)
- [ ] `scripts/build-quality-check.ps1` exits non-zero on any gate failure (AC-4)
- [ ] Pipeline fails on compiler warning, StyleCop violation, or test failure (AC-4)

## Implementation Checklist

- [ ] Create Directory.Build.props with TreatWarningsAsErrors and StyleCop reference
- [ ] Create stylecop.json with documentation, ordering, naming configuration
- [ ] Create .editorconfig with PascalCase, camelCase, async suffix naming rules
- [ ] Configure relaxed settings for test projects in Directory.Build.props
- [ ] Create scripts/build-quality-check.ps1 orchestrating all quality gates
- [ ] Verify CS1591 suppression in Api and Contracts projects
- [ ] Audit existing async methods for Async suffix compliance
- [ ] Document CI integration pattern for quality gate script
