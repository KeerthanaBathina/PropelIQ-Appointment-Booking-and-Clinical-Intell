# Task - task_001_be_solution_scaffold

## Requirement Reference

- User Story: us_001
- Story Location: .propel/context/tasks/EP-TECH/us_001/us_001.md
- Acceptance Criteria:
  - AC-1: Given the project repository is initialized, When a developer clones the repo and runs `dotnet build`, Then the solution compiles with zero errors and zero warnings.
  - AC-2: Given the backend project is running, When a developer navigates to `/swagger`, Then the Swagger UI loads with OpenAPI 3.0 documentation showing all registered endpoints.
  - AC-3: Given the project scaffold is complete, When a developer inspects the solution structure, Then it follows layered architecture with Presentation, Service, and Data Access layers separated into distinct projects.
- Edge Case:
  - What happens when the .NET 8 SDK is not installed? Build script validates SDK version and provides installation guidance.

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
| API Framework | ASP.NET Core MVC | 8.x |
| Documentation | Swagger (Swashbuckle) | 6.x |
| ORM | Entity Framework Core | 8.x |

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

Create the foundational .NET 8 ASP.NET Core Web API solution with a layered architecture (Presentation, Service, Data Access) separated into distinct class library projects. Configure Swagger/Swashbuckle for OpenAPI 3.0 documentation generation. The solution must compile with zero errors and zero warnings, establishing the standardized backend foundation for all subsequent development.

## Dependent Tasks

- None (this is the foundational scaffolding task)

## Impacted Components

- **NEW** `UPACIP.sln` — Solution file binding all projects
- **NEW** `src/UPACIP.Api/` — Presentation layer (ASP.NET Core Web API project)
- **NEW** `src/UPACIP.Service/` — Service/business logic layer (class library)
- **NEW** `src/UPACIP.DataAccess/` — Data access layer (class library)

## Implementation Plan

1. **Create solution and projects**: Initialize the .NET 8 solution with three projects following clean layered architecture. The Presentation project is the ASP.NET Core Web API entry point; Service and Data Access are .NET 8 class libraries.
2. **Establish project references**: Wire inter-project dependencies following the dependency rule (Presentation → Service → Data Access). No reverse references permitted.
3. **Configure Program.cs**: Set up the minimal hosting model with controllers, endpoints explorer, Swagger services, Kestrel, and HTTPS redirection.
4. **Integrate Swashbuckle**: Add `Swashbuckle.AspNetCore` NuGet package to the Presentation project. Configure `AddSwaggerGen` with OpenAPI 3.0 info (title, version, description). Map `/swagger` route for Swagger UI.
5. **Add health check stub controller**: Create a basic `WeatherForecastController` or equivalent placeholder to verify Swagger endpoint registration.
6. **Validate build**: Run `dotnet build` and confirm zero errors and zero warnings across all projects.
7. **Add SDK validation script**: Create a PowerShell script that checks for .NET 8 SDK presence and outputs installation guidance if missing.

## Current Project State

```text
UPACIP/
├── (empty — greenfield project, no existing code)
```

> Placeholder: Updated during task execution based on actual project initialization.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | UPACIP.sln | Solution file with three project references |
| CREATE | src/UPACIP.Api/UPACIP.Api.csproj | ASP.NET Core Web API project targeting net8.0 with Swashbuckle dependency |
| CREATE | src/UPACIP.Api/Program.cs | Minimal hosting model with Swagger, controllers, endpoints explorer |
| CREATE | src/UPACIP.Api/appsettings.json | Base application settings (Kestrel URLs, logging) |
| CREATE | src/UPACIP.Api/appsettings.Development.json | Development environment overrides |
| CREATE | src/UPACIP.Api/Controllers/WeatherForecastController.cs | Placeholder API controller for Swagger endpoint verification |
| CREATE | src/UPACIP.Service/UPACIP.Service.csproj | .NET 8 class library for business logic layer |
| CREATE | src/UPACIP.DataAccess/UPACIP.DataAccess.csproj | .NET 8 class library for data access layer |
| CREATE | scripts/check-sdk.ps1 | PowerShell script validating .NET 8 SDK installation |

## External References

- [ASP.NET Core 8 Web API documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [Swashbuckle.AspNetCore GitHub](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)
- [ASP.NET Core project structure best practices](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [OpenAPI 3.0 specification](https://spec.openapis.org/oas/v3.0.3)

## Build Commands

```powershell
# Restore dependencies
dotnet restore UPACIP.sln

# Build entire solution
dotnet build UPACIP.sln --configuration Debug --no-restore

# Run the API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Verify Swagger at https://localhost:{port}/swagger
```

## Implementation Validation Strategy

- [ ] `dotnet build UPACIP.sln` completes with zero errors and zero warnings
- [ ] `dotnet run` starts Kestrel and serves the API on configured port
- [ ] Navigating to `/swagger` loads Swagger UI with OpenAPI 3.0 documentation
- [ ] Swagger UI displays registered controller endpoints
- [ ] Solution structure shows three separate projects (Api, Service, DataAccess)
- [ ] Project references follow Presentation → Service → Data Access dependency direction
- [ ] `scripts/check-sdk.ps1` correctly detects .NET 8 SDK presence/absence

## Implementation Checklist

- [ ] Create .NET 8 solution file (`UPACIP.sln`) in repository root
- [ ] Create ASP.NET Core Web API project (`src/UPACIP.Api/`) targeting `net8.0`
- [ ] Create Service layer class library (`src/UPACIP.Service/`) targeting `net8.0`
- [ ] Create Data Access layer class library (`src/UPACIP.DataAccess/`) targeting `net8.0`
- [ ] Add project references: Api → Service, Service → DataAccess
- [ ] Configure `Program.cs` with `AddControllers`, `AddEndpointsApiExplorer`, `AddSwaggerGen` (OpenAPI 3.0 info metadata), `UseSwagger`, `UseSwaggerUI`, and `UseHttpsRedirection`
- [ ] Create `scripts/check-sdk.ps1` that validates .NET 8 SDK is installed and prints installation URL if missing
