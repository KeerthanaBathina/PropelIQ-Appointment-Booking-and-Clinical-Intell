# Task - task_002_be_seed_data_integration

## Requirement Reference

- User Story: us_011
- Story Location: .propel/context/tasks/EP-DATA/us_011/us_011.md
- Acceptance Criteria:
  - AC-4: Given the seed script is idempotent, When it runs multiple times, Then it does not create duplicate records and can reset data to a known state.
- Edge Case:
  - What happens when seed script runs against a production database? Script checks for environment variable and aborts if environment is "Production".

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
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
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

Integrate the SQL seed data script into the .NET application lifecycle by creating an `IDataSeeder` service that can optionally execute seed data on application startup in Development/Staging environments. Implement a `--seed` command-line argument that triggers seeding when the application starts, secured by environment checks that prevent execution in Production. Also provide an EF Core-based alternative seeder using `HasData()` for a minimal set of lookup data (roles, enum reference values) that is always applied via migrations, keeping the larger realistic dataset in the SQL script for on-demand seeding.

## Dependent Tasks

- task_001_db_seed_data_script — SQL seed script must exist for the seeder to invoke.
- US_001 task_001_be_solution_scaffold — Backend solution with `Program.cs` must exist.
- US_005 task_001_be_identity_configuration — Identity roles (Patient, Staff, Admin) must be seeded via this task's EF Core `HasData()`.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Seeding/IDataSeeder.cs` — Interface defining `Task SeedAsync(CancellationToken cancellationToken)`
- **NEW** `src/UPACIP.DataAccess/Seeding/SqlFileDataSeeder.cs` — Implementation that reads and executes `scripts/seed-data.sql` via raw Npgsql connection
- **NEW** `src/UPACIP.DataAccess/Seeding/RoleSeedConfiguration.cs` — EF Core `IEntityTypeConfiguration<ApplicationRole>` with `HasData()` for 3 default roles
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register `IDataSeeder` in DI, add `--seed` CLI argument handler that invokes seeder on startup in non-Production environments
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Apply `RoleSeedConfiguration` in `OnModelCreating` for always-applied role seed data

## Implementation Plan

1. **Define `IDataSeeder` interface**: Create `src/UPACIP.DataAccess/Seeding/IDataSeeder.cs` with a single method `Task SeedAsync(CancellationToken cancellationToken = default)`. This abstraction allows swapping seed strategies (SQL file, EF Core `HasData()`, or programmatic seeding) without changing the startup code.

2. **Implement `SqlFileDataSeeder`**: Create `src/UPACIP.DataAccess/Seeding/SqlFileDataSeeder.cs` that:
   - Accepts `ApplicationDbContext` and `IWebHostEnvironment` via constructor injection.
   - In `SeedAsync()`, first checks `environment.IsProduction()` — if true, logs a warning and returns without executing (defense-in-depth; the SQL script has its own guard).
   - Reads the SQL file from `scripts/seed-data.sql` relative to content root via `Path.Combine(environment.ContentRootPath, "scripts", "seed-data.sql")`.
   - Obtains a raw `NpgsqlConnection` from the `ApplicationDbContext.Database.GetDbConnection()`.
   - Executes the SQL file content within a single transaction (`BeginTransaction` → `ExecuteNonQuery` → `Commit`). On failure, rolls back and throws.
   - Logs the number of affected rows and execution time.

3. **Create `RoleSeedConfiguration` for always-applied role data**: Create `src/UPACIP.DataAccess/Seeding/RoleSeedConfiguration.cs` implementing `IEntityTypeConfiguration<ApplicationRole>`. Use `builder.HasData()` to seed the 3 default roles:
   - `{ Id = Guid.Parse("..."), Name = "Admin", NormalizedName = "ADMIN" }`
   - `{ Id = Guid.Parse("..."), Name = "Staff", NormalizedName = "STAFF" }`
   - `{ Id = Guid.Parse("..."), Name = "Patient", NormalizedName = "PATIENT" }`
   Use deterministic GUIDs matching those in the SQL seed script. This data is applied via EF Core migrations (always present, even in Production).

4. **Register seeder in `Program.cs`**: Add `builder.Services.AddScoped<IDataSeeder, SqlFileDataSeeder>()` in DI registration. After `app.Build()`, add a startup block:
   ```csharp
   if (args.Contains("--seed"))
   {
       using var scope = app.Services.CreateScope();
       var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
       await seeder.SeedAsync();
       return; // Exit after seeding (don't start the web server)
   }
   ```
   This allows running `dotnet run --project src/UPACIP.Api -- --seed` to seed and exit. If `--seed` is not passed, the application starts normally without seeding.

5. **Apply `RoleSeedConfiguration` in `ApplicationDbContext`**: In `OnModelCreating`, the `ApplyConfigurationsFromAssembly()` call (from US_008) will automatically discover and apply `RoleSeedConfiguration`. Generate a new migration `dotnet ef migrations add SeedDefaultRoles` to capture the `HasData()` inserts. This migration is safe for all environments.

6. **Add development convenience command**: In `scripts/seed-database.ps1` (from task_001), add an alternative invocation path that calls `dotnet run --project src/UPACIP.Api -- --seed` instead of direct psql. This provides two seeding options: (a) direct SQL via psql for speed, (b) .NET-hosted seeding for environments where psql is not available.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   └── ...
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       ├── Configurations/
│       ├── Identity/
│       │   ├── ApplicationUser.cs
│       │   └── ApplicationRole.cs
│       └── Migrations/
├── app/
└── scripts/
    ├── seed-data.sql
    ├── seed-database.ps1
    ├── provision-database.ps1
    └── provision-database.sql
```

> Assumes task_001_db_seed_data_script, US_001, US_005, and US_008 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Seeding/IDataSeeder.cs | Interface with `SeedAsync` method |
| CREATE | src/UPACIP.DataAccess/Seeding/SqlFileDataSeeder.cs | Implementation reading `scripts/seed-data.sql` via raw Npgsql connection with environment guard |
| CREATE | src/UPACIP.DataAccess/Seeding/RoleSeedConfiguration.cs | EF Core `HasData()` for 3 default roles (Admin, Staff, Patient) with deterministic GUIDs |
| MODIFY | src/UPACIP.Api/Program.cs | Register `IDataSeeder` in DI, add `--seed` CLI argument handler for on-demand seeding |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Apply `RoleSeedConfiguration` via assembly scanning (auto-discovered) |
| MODIFY | scripts/seed-database.ps1 | Add alternative `dotnet run -- --seed` invocation path alongside direct psql |
| CREATE | src/UPACIP.DataAccess/Migrations/*_SeedDefaultRoles.cs | Migration for `HasData()` role seed data |

## External References

- [EF Core Data Seeding (HasData)](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [ASP.NET Core Command-Line Arguments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0#command-line)
- [IWebHostEnvironment.IsProduction()](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostenvironmentenvextensions.isproduction)
- [EF Core Raw SQL Execution](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- [Npgsql ExecuteNonQuery](https://www.npgsql.org/doc/basic-usage.html)

## Build Commands

```powershell
# Build solution
dotnet build UPACIP.sln

# Generate migration for role seed data
dotnet ef migrations add SeedDefaultRoles --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migrations (includes role seed)
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Seed full development dataset via .NET
dotnet run --project src/UPACIP.Api -- --seed

# Seed via direct psql (alternative)
.\scripts\seed-database.ps1

# Verify roles exist (applied via migration in all environments)
psql -U upacip_app -d upacip -c "SELECT \"Name\" FROM \"AspNetRoles\";"

# Verify seed data (applied via --seed in Development only)
psql -U upacip_app -d upacip -c "SELECT COUNT(*) FROM patients;"

# Test idempotency: run twice, verify same counts
dotnet run --project src/UPACIP.Api -- --seed
dotnet run --project src/UPACIP.Api -- --seed
psql -U upacip_app -d upacip -c "SELECT COUNT(*) FROM patients;"
# Expected: 10 (not 20)
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] `dotnet ef migrations add SeedDefaultRoles` generates migration with `HasData()` for 3 roles
- [ ] `dotnet run -- --seed` executes seed script and exits without starting the web server
- [ ] `dotnet run -- --seed` in Production environment logs warning and does NOT execute SQL seed data
- [ ] Roles (Admin, Staff, Patient) exist after migration in all environments (including Production)
- [ ] Running `dotnet run -- --seed` twice produces identical data counts (idempotent)
- [ ] Application starts normally without `--seed` flag (no seeding occurs on regular startup)

## Implementation Checklist

- [ ] Create `IDataSeeder` interface in `src/UPACIP.DataAccess/Seeding/` with `Task SeedAsync(CancellationToken)` method
- [ ] Create `SqlFileDataSeeder` that reads `scripts/seed-data.sql`, checks `IsProduction()`, and executes via raw `NpgsqlConnection` within a transaction
- [ ] Create `RoleSeedConfiguration` with `HasData()` seeding 3 default roles (Admin, Staff, Patient) using deterministic GUIDs
- [ ] Register `IDataSeeder` in DI and add `--seed` CLI argument handler in `Program.cs` that invokes seeder and exits
- [ ] Generate `SeedDefaultRoles` migration and verify it creates role records in migration `Up()` method
- [ ] Update `scripts/seed-database.ps1` to support both psql direct and `dotnet run -- --seed` invocation paths
