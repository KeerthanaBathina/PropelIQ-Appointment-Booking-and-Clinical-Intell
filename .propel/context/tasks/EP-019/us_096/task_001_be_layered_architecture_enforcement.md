# Task - task_001_be_layered_architecture_enforcement

## Requirement Reference

- User Story: us_096
- Story Location: .propel/context/tasks/EP-019/us_096/us_096.md
- Acceptance Criteria:
  - AC-1: Given the project structure exists, When a developer reviews the solution, Then it follows layered architecture with Presentation, Service, and Data Access layers in separate projects with unidirectional dependencies.
  - AC-4: Given the architecture is reviewed, When dependency analysis runs, Then no circular dependencies exist and the Presentation layer does not directly reference the Data Access layer.
- Edge Case:
  - What happens when a developer accidentally creates a cross-layer dependency? Build-time analyzers flag architectural violations as compiler warnings.

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
| Backend | Entity Framework Core | 8.x |
| Build | NetArchTest.Rules | 1.x |

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

Formalize the layered architecture (Presentation → Service → Data Access) by enforcing unidirectional project dependencies, defining layer-specific interface contracts, and implementing build-time architectural validation that flags cross-layer violations as compiler warnings (AC-1, AC-4, TR-009, edge case 1). The three layers are implemented as separate .NET projects — `UPACIP.Api` (Presentation), `UPACIP.Service` (Service), `UPACIP.DataAccess` (Data Access) — with strict dependency rules: Api → Service → DataAccess (never reverse). A shared `UPACIP.Contracts` project provides cross-layer DTOs and interfaces without introducing circular references. Build-time architecture tests using NetArchTest.Rules verify that no Presentation-layer class references DataAccess-layer types directly, and that no circular dependencies exist (AC-4). An `.editorconfig` rule and a Roslyn analyzer configuration enforce naming conventions per layer.

## Dependent Tasks

- US_001 — Requires project scaffold with solution structure.

## Impacted Components

- **NEW** `src/UPACIP.Contracts/UPACIP.Contracts.csproj` — Shared interfaces, DTOs, and enums referenced by all layers
- **NEW** `src/UPACIP.Contracts/Services/IServiceBase.cs` — Base service interface defining service layer contract
- **NEW** `src/UPACIP.Contracts/Models/PagedResult.cs` — Generic paginated result DTO used across layers
- **NEW** `src/UPACIP.Contracts/Models/ApiResponse.cs` — Standard API response envelope for consistent responses
- **NEW** `tests/UPACIP.ArchTests/ArchitectureTests.cs` — NetArchTest-based build-time dependency validation
- **NEW** `tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj` — Test project for architecture rule enforcement
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Verify references: UPACIP.Service and UPACIP.Contracts only (NOT UPACIP.DataAccess)
- **MODIFY** `src/UPACIP.Service/UPACIP.Service.csproj` — Verify references: UPACIP.DataAccess and UPACIP.Contracts only (NOT UPACIP.Api)
- **MODIFY** `src/UPACIP.DataAccess/UPACIP.DataAccess.csproj` — Verify references: UPACIP.Contracts only (no upward references)
- **MODIFY** `UPACIP.sln` — Add UPACIP.Contracts and UPACIP.ArchTests projects

## Implementation Plan

1. **Create `UPACIP.Contracts` shared project**: Create `src/UPACIP.Contracts/UPACIP.Contracts.csproj` as a .NET 8 class library with no project references (it is the leaf dependency):
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
     </PropertyGroup>
   </Project>
   ```
   This project contains:
   - **Service interfaces** (`IServiceBase`, `IAuditLogService`, `IPatientService`, etc.) — consumed by the Api layer to call Service layer methods via DI without direct DataAccess references.
   - **DTOs** (`ApiResponse<T>`, `PagedResult<T>`, `ErrorResponse`) — shared request/response models.
   - **Enums** (domain enums already defined per entity models).
   - **No EF Core dependency** — this project must NOT reference Entity Framework Core or any DataAccess-specific packages.

2. **Define `ApiResponse<T>` standard envelope**: Create in `src/UPACIP.Contracts/Models/ApiResponse.cs`:
   ```csharp
   public class ApiResponse<T>
   {
       public T Data { get; init; }
       public bool Success { get; init; }
       public string? Message { get; init; }
       public List<string>? Errors { get; init; }
       public Dictionary<string, object>? Metadata { get; init; }
   }
   ```
   All API controllers return `ApiResponse<T>` for consistency across the Presentation layer.

3. **Define `PagedResult<T>` for collections**: Create in `src/UPACIP.Contracts/Models/PagedResult.cs`:
   ```csharp
   public class PagedResult<T>
   {
       public List<T> Items { get; init; }
       public int Page { get; init; }
       public int PageSize { get; init; }
       public int TotalCount { get; init; }
       public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
       public bool HasNext => Page < TotalPages;
       public bool HasPrevious => Page > 1;
   }
   ```
   Used by all paginated endpoints as the return type from service layer methods.

4. **Enforce project dependency rules (AC-1, AC-4)**: Verify and enforce `.csproj` references:

   **`UPACIP.Api.csproj`** (Presentation layer):
   - References: `UPACIP.Service`, `UPACIP.Contracts`.
   - MUST NOT reference: `UPACIP.DataAccess`.
   - Remove any existing direct reference to `UPACIP.DataAccess` if present.
   - The Api layer accesses data only through `IServiceXxx` interfaces from `UPACIP.Contracts`, resolved via DI.

   **`UPACIP.Service.csproj`** (Service layer):
   - References: `UPACIP.DataAccess`, `UPACIP.Contracts`.
   - MUST NOT reference: `UPACIP.Api`.
   - The Service layer implements `IServiceXxx` interfaces from `UPACIP.Contracts` and depends on `ApplicationDbContext` from `UPACIP.DataAccess`.

   **`UPACIP.DataAccess.csproj`** (Data Access layer):
   - References: `UPACIP.Contracts`.
   - MUST NOT reference: `UPACIP.Api`, `UPACIP.Service`.
   - Contains `ApplicationDbContext`, entity configurations, and migrations only.

   **Dependency direction**: Api → Service → DataAccess → (Contracts is referenced by all three, no upward refs).

5. **Define layer-specific interface contracts**: Create in `src/UPACIP.Contracts/Services/`:

   **`IServiceBase.cs`** — Marker interface identifying service layer classes:
   ```csharp
   public interface IServiceBase { }
   ```

   Move existing service interfaces (e.g., `IPatientDataExportService`, `IHipaaComplianceVerificationService`, `ICsvImportEngine`) to `UPACIP.Contracts` so the Api layer references only interfaces, never implementations. The Service layer provides concrete implementations registered via DI in `Program.cs`.

   This ensures the Presentation layer never has a compile-time dependency on DataAccess types (AC-4).

6. **Create architecture test project (AC-4, edge case 1)**: Create `tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="NetArchTest.Rules" Version="1.*" />
       <PackageReference Include="xunit" Version="2.*" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\..\src\UPACIP.Api\UPACIP.Api.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.Service\UPACIP.Service.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.DataAccess\UPACIP.DataAccess.csproj" />
     </ItemGroup>
   </Project>
   ```

7. **Implement architecture validation tests**: Create in `tests/UPACIP.ArchTests/ArchitectureTests.cs`:

   **Test 1 — Presentation layer must not reference DataAccess** (AC-4):
   ```csharp
   [Fact]
   public void PresentationLayer_ShouldNot_ReferenceDataAccess()
   {
       var result = Types.InAssembly(typeof(Program).Assembly)
           .ShouldNot()
           .HaveDependencyOn("UPACIP.DataAccess")
           .GetResult();
       Assert.True(result.IsSuccessful, "Presentation layer must not directly reference Data Access layer");
   }
   ```

   **Test 2 — DataAccess layer must not reference Presentation or Service** (AC-4):
   ```csharp
   [Fact]
   public void DataAccessLayer_ShouldNot_ReferenceUpperLayers()
   {
       var result = Types.InAssembly(typeof(ApplicationDbContext).Assembly)
           .ShouldNot()
           .HaveDependencyOnAny("UPACIP.Api", "UPACIP.Service")
           .GetResult();
       Assert.True(result.IsSuccessful, "Data Access layer must not reference upper layers");
   }
   ```

   **Test 3 — Service layer must not reference Presentation** (AC-1):
   ```csharp
   [Fact]
   public void ServiceLayer_ShouldNot_ReferencePresentationLayer()
   {
       var result = Types.InAssembly(typeof(IServiceBase).Assembly)
           .That().ResideInNamespace("UPACIP.Service")
           .ShouldNot()
           .HaveDependencyOn("UPACIP.Api")
           .GetResult();
       Assert.True(result.IsSuccessful, "Service layer must not reference Presentation layer");
   }
   ```

   **Test 4 — No circular dependencies** (AC-4):
   ```csharp
   [Fact]
   public void Architecture_ShouldNotHave_CircularDependencies()
   {
       // Verify Api does not reference DataAccess
       // Verify DataAccess does not reference Api or Service
       // If both pass, no circular dependency exists in the 3-layer chain
   }
   ```

   **Test 5 — Controllers reside only in Presentation layer**:
   ```csharp
   [Fact]
   public void Controllers_ShouldResideIn_PresentationLayer()
   {
       var result = Types.InAssembly(typeof(Program).Assembly)
           .That().HaveNameEndingWith("Controller")
           .Should().ResideInNamespaceStartingWith("UPACIP.Api")
           .GetResult();
       Assert.True(result.IsSuccessful);
   }
   ```

   These tests run as part of `dotnet test` and fail the build if violations are introduced (edge case 1 — build-time detection).

8. **Configure `.editorconfig` for layer naming conventions**: Add rules to enforce naming by layer:
   - Controllers: must end with `Controller` and reside in `UPACIP.Api.Controllers`.
   - Services: must end with `Service` and reside in `UPACIP.Service.*`.
   - Entities: must reside in `UPACIP.DataAccess.Entities`.
   - Configure `dotnet_diagnostic.CA1707` (identifier naming) and `dotnet_naming_rule` entries for PascalCase public members, camelCase private fields.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/                                  ← Presentation layer
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/                              ← Service layer
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/
│   │   ├── Import/
│   │   ├── Logging/
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   ├── PatientRights/
│   │   ├── Recovery/
│   │   ├── Resilience/
│   │   └── Security/
│   └── UPACIP.DataAccess/                           ← Data Access layer
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       ├── Configurations/
│       └── Migrations/
├── Server/
├── app/
├── config/
└── scripts/
```

> Assumes US_001 (project scaffold) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Contracts/UPACIP.Contracts.csproj | Shared interfaces, DTOs, enums (no EF Core dependency) |
| CREATE | src/UPACIP.Contracts/Services/IServiceBase.cs | Marker interface for service layer classes |
| CREATE | src/UPACIP.Contracts/Models/ApiResponse.cs | Standard API response envelope DTO |
| CREATE | src/UPACIP.Contracts/Models/PagedResult.cs | Generic paginated result DTO |
| CREATE | tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj | Architecture test project with NetArchTest.Rules |
| CREATE | tests/UPACIP.ArchTests/ArchitectureTests.cs | 5 architecture validation tests (dependency direction, no circular refs) |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add UPACIP.Contracts ref, verify no UPACIP.DataAccess ref |
| MODIFY | src/UPACIP.Service/UPACIP.Service.csproj | Add UPACIP.Contracts ref |
| MODIFY | src/UPACIP.DataAccess/UPACIP.DataAccess.csproj | Add UPACIP.Contracts ref |
| MODIFY | UPACIP.sln | Add UPACIP.Contracts and UPACIP.ArchTests projects |

## External References

- [NetArchTest — Architecture Testing for .NET](https://github.com/BenMorris/NetArchTest)
- [Clean Architecture — Microsoft .NET](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Dependency Inversion Principle — SOLID](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#dependency-inversion)
- [EditorConfig — .NET Naming Conventions](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules)

## Build Commands

```powershell
# Build full solution
dotnet build UPACIP.sln

# Run architecture tests
dotnet test tests/UPACIP.ArchTests/UPACIP.ArchTests.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] UPACIP.Api.csproj does NOT reference UPACIP.DataAccess (AC-1, AC-4)
- [ ] UPACIP.DataAccess.csproj does NOT reference UPACIP.Api or UPACIP.Service (AC-4)
- [ ] UPACIP.Service.csproj does NOT reference UPACIP.Api (AC-1)
- [ ] UPACIP.Contracts.csproj has no project references (leaf dependency)
- [ ] Architecture tests pass: PresentationLayer_ShouldNot_ReferenceDataAccess (AC-4)
- [ ] Architecture tests pass: DataAccessLayer_ShouldNot_ReferenceUpperLayers (AC-4)
- [ ] Architecture tests pass: no circular dependencies detected (AC-4)
- [ ] Introducing a cross-layer reference causes architecture test failure (edge case 1)
- [ ] Controllers reside exclusively in UPACIP.Api namespace

## Implementation Checklist

- [ ] Create UPACIP.Contracts project with no project references
- [ ] Define ApiResponse<T> and PagedResult<T> shared DTOs
- [ ] Define IServiceBase marker interface and move service interfaces to Contracts
- [ ] Enforce .csproj dependency rules (Api→Service→DataAccess, no reverse)
- [ ] Create UPACIP.ArchTests project with NetArchTest.Rules
- [ ] Implement 5 architecture validation tests for dependency direction and naming
- [ ] Add UPACIP.Contracts and UPACIP.ArchTests to UPACIP.sln
- [ ] Configure .editorconfig naming conventions for each layer
