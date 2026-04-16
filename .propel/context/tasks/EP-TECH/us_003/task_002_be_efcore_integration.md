# Task - task_002_be_efcore_integration

## Requirement Reference

- User Story: us_003
- Story Location: .propel/context/tasks/EP-TECH/us_003/us_003.md
- Acceptance Criteria:
  - AC-1: Given PostgreSQL 16 is installed, When the application starts, Then it connects to the database using a connection string from appsettings.json with connection pooling (max 100 connections).
  - AC-2: Given EF Core 8 is configured, When a developer runs `dotnet ef migrations add Initial`, Then the migration is generated without errors using code-first approach.
  - AC-3: Given the database is provisioned, When a developer runs `dotnet ef database update`, Then the migration applies successfully creating the database schema.
- Edge Case:
  - What happens when PostgreSQL is not running? Application startup logs clear error with connection retry guidance.
  - How does the system handle invalid connection strings? EF Core throws descriptive exception caught by error handling middleware.

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
| ORM | Entity Framework Core | 8.x |
| Database Provider | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
| Database | PostgreSQL | 16.x |

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

Install and configure Entity Framework Core 8 with the Npgsql PostgreSQL provider in the Data Access layer. Set up the application `DbContext`, configure the connection string from `appsettings.json` with a max pool size of 100, register the DbContext in the dependency injection container with retry-on-failure for transient faults, and verify that code-first migrations can be generated and applied successfully. The connection pooling is managed at the Npgsql ADO.NET level via the connection string `Maximum Pool Size` parameter.

## Dependent Tasks

- task_001_db_postgresql_provisioning — PostgreSQL database must be provisioned before EF Core can connect and apply migrations.
- US_001 task_001_be_solution_scaffold — Backend solution with layered architecture (Api, Service, DataAccess projects) must exist.
- US_001 task_002_be_middleware_pipeline — Error handling middleware must exist to catch database connection exceptions.

## Impacted Components

- **MODIFY** `src/UPACIP.DataAccess/UPACIP.DataAccess.csproj` — Add EF Core 8 and Npgsql NuGet packages
- **NEW** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Application DbContext with DbSet declarations
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add EF Core Design package for migrations tooling
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register DbContext in DI container with Npgsql connection
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add ConnectionStrings section with PostgreSQL connection string
- **MODIFY** `src/UPACIP.Api/appsettings.Development.json` — Add development-specific connection string

## Implementation Plan

1. **Install NuGet packages**: Add `Microsoft.EntityFrameworkCore` (8.x), `Npgsql.EntityFrameworkCore.PostgreSQL` (8.x), and `Microsoft.EntityFrameworkCore.Design` (8.x) to the appropriate projects. The Design package goes on the Api project (startup project) for CLI tooling; the core + Npgsql packages go on the DataAccess project.
2. **Create ApplicationDbContext**: In the DataAccess project, create `ApplicationDbContext` extending `DbContext`. Override `OnModelCreating` for Fluent API configuration. Keep it empty for now — domain entities will be added by subsequent user stories. Include the `DbContext` constructor accepting `DbContextOptions<ApplicationDbContext>`.
3. **Configure connection string**: Add a `ConnectionStrings` section to `appsettings.json` with the PostgreSQL connection string including `Maximum Pool Size=100` and `Timeout=30` parameters. The connection string format: `Host=localhost;Port=5432;Database=upacip;Username=upacip_app;Password={password};Maximum Pool Size=100;Timeout=30`. Use user secrets or environment variables for the password — never hardcode credentials.
4. **Register DbContext in DI**: In `Program.cs`, register `ApplicationDbContext` using `AddDbContext<ApplicationDbContext>` with `UseNpgsql`. Configure `EnableRetryOnFailure` with max 3 retries and 10-second delay for transient fault handling (per NFR-032). Set the PostgreSQL version to 16.0 explicitly via `SetPostgresVersion`.
5. **Install EF Core CLI tools**: Ensure `dotnet-ef` global tool is installed (`dotnet tool install --global dotnet-ef`). Verify it works by running `dotnet ef --version`.
6. **Generate initial migration**: Run `dotnet ef migrations add Initial --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api` to generate the initial (empty) migration. This validates that the DbContext configuration, connection string, and project references are all correct.
7. **Apply migration**: Run `dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api` to apply the migration to the database. Verify the `__EFMigrationsHistory` table is created in the `upacip` database.
8. **Add connection health logging**: In `Program.cs`, add a startup check that attempts to connect to the database and logs a clear error message with retry guidance if PostgreSQL is not reachable, rather than crashing with an unhandled exception.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   ├── Models/
│   │   │   └── ErrorResponse.cs
│   │   └── Controllers/
│   │       └── WeatherForecastController.cs
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       └── UPACIP.DataAccess.csproj
├── app/
│   └── (frontend project from US_002)
└── scripts/
    ├── check-sdk.ps1
    ├── deploy-frontend.ps1
    ├── provision-database.ps1
    └── provision-database.sql
```

> Assumes US_001 tasks and task_001_db_postgresql_provisioning are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/UPACIP.DataAccess.csproj | Add `Microsoft.EntityFrameworkCore` 8.x and `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x NuGet packages |
| CREATE | src/UPACIP.DataAccess/ApplicationDbContext.cs | DbContext with constructor accepting options, empty OnModelCreating override for Fluent API |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `Microsoft.EntityFrameworkCore.Design` 8.x NuGet package for migrations CLI |
| MODIFY | src/UPACIP.Api/Program.cs | Register `ApplicationDbContext` with `UseNpgsql`, retry-on-failure, SetPostgresVersion(16,0), and startup connection health check |
| MODIFY | src/UPACIP.Api/appsettings.json | Add `ConnectionStrings:DefaultConnection` with PostgreSQL connection string (Maximum Pool Size=100) |
| MODIFY | src/UPACIP.Api/appsettings.Development.json | Add development-specific connection string pointing to localhost |
| CREATE | src/UPACIP.DataAccess/Migrations/ | Auto-generated initial migration files from `dotnet ef migrations add Initial` |

## External References

- [EF Core 8 getting started with ASP.NET Core](https://learn.microsoft.com/en-us/ef/core/get-started/overview/first-app?tabs=netcore-cli)
- [Npgsql EF Core PostgreSQL provider](https://www.npgsql.org/efcore/)
- [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
- [EF Core migrations overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [Npgsql connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)
- [ASP.NET Core user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-8.0)

## Build Commands

```powershell
# Install EF Core CLI tools
dotnet tool install --global dotnet-ef

# Restore packages
dotnet restore UPACIP.sln

# Build solution
dotnet build UPACIP.sln --configuration Debug

# Generate initial migration
dotnet ef migrations add Initial --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migration to database
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Verify migration was applied
dotnet ef migrations list --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors and zero warnings after adding EF Core packages
- [ ] `dotnet ef migrations add Initial` generates migration files without errors
- [ ] `dotnet ef database update` applies the migration to the PostgreSQL database successfully
- [ ] `__EFMigrationsHistory` table exists in the `upacip` database with the Initial migration entry
- [ ] Application starts and connects to PostgreSQL using the connection string from `appsettings.json`
- [ ] Connection string includes `Maximum Pool Size=100` parameter
- [ ] Application logs a clear error message when PostgreSQL is not running (not an unhandled crash)
- [ ] Application password is not hardcoded in `appsettings.json` (uses user secrets or environment variable)

## Implementation Checklist

- [ ] Add `Microsoft.EntityFrameworkCore` 8.x and `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x NuGet packages to `UPACIP.DataAccess.csproj`
- [ ] Add `Microsoft.EntityFrameworkCore.Design` 8.x NuGet package to `UPACIP.Api.csproj`
- [ ] Create `src/UPACIP.DataAccess/ApplicationDbContext.cs` with constructor accepting `DbContextOptions<ApplicationDbContext>` and empty `OnModelCreating` override
- [ ] Add `ConnectionStrings:DefaultConnection` to `appsettings.json` with `Host=localhost;Port=5432;Database=upacip;Username=upacip_app;Password={from-secrets};Maximum Pool Size=100;Timeout=30`
- [ ] Register `ApplicationDbContext` in `Program.cs` with `UseNpgsql`, `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)`, and `SetPostgresVersion(16, 0)`
- [ ] Add startup database connection health check in `Program.cs` that logs a descriptive error with retry guidance if PostgreSQL is unreachable
- [ ] Run `dotnet ef migrations add Initial` and `dotnet ef database update` to validate the full EF Core pipeline works end-to-end
