# Task - task_001_be_migration_pipeline_engine

## Requirement Reference

- User Story: us_091
- Story Location: .propel/context/tasks/EP-017/us_091/us_091.md
- Acceptance Criteria:
  - AC-1: Given a schema change is needed, When a developer creates an EF Core migration, Then the migration script includes both Up (apply) and Down (rollback) methods.
  - AC-2: Given a migration is executed, When it runs against the database, Then it executes within a transaction and automatically rolls back if any step fails.
  - AC-3: Given a migration completes, When the history is checked, Then the __EFMigrationsHistory table records the migration name, timestamp, and checksum.
- Edge Case:
  - What happens when a migration fails mid-execution? Transaction ensures complete rollback; the database remains in its pre-migration state with the failure logged.

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
| Backend | Serilog | 8.x |
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

Implement the transactional migration pipeline engine that extends EF Core's built-in migration infrastructure with enhanced history tracking, transactional execution guarantees, and structured logging (DR-028, DR-029, DR-030). EF Core 8 with Npgsql already provides code-first migrations with `Up`/`Down` methods (AC-1) and uses the `__EFMigrationsHistory` table for tracking applied migrations. This task enhances that foundation with: (1) a custom `IHistoryRepository` implementation that extends the standard `__EFMigrationsHistory` table with an `AppliedAtUtc` timestamp and a SHA-256 checksum of the migration's SQL content (AC-3), (2) a `MigrationExecutionService` that wraps EF Core's `IMigrator.MigrateAsync` with explicit transaction management, pre/post-migration snapshots, and structured Serilog logging (AC-2), and (3) a PowerShell deployment script that invokes the migration pipeline during Windows Service deployments with rollback support (edge case 1). The transactional behavior leverages PostgreSQL's DDL-in-transaction support — unlike some databases, PostgreSQL allows `CREATE TABLE`, `ALTER TABLE`, and other DDL statements inside transactions, making true atomic migrations achievable.

## Dependent Tasks

- US_008 — Requires EF Core entity models and ApplicationDbContext with initial entity configurations.
- US_003 — Requires PostgreSQL database infrastructure with connection string configured.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Migrations/CustomHistoryRepository.cs` — Custom IHistoryRepository: extends __EFMigrationsHistory with timestamp and checksum columns
- **NEW** `src/UPACIP.Service/Migration/MigrationExecutionService.cs` — IMigrationExecutionService: transactional migration execution with pre/post snapshots and logging
- **NEW** `src/UPACIP.Service/Migration/Models/MigrationExecutionOptions.cs` — Configuration: timeout, pre-backup flag, logging level
- **NEW** `src/UPACIP.Service/Migration/Models/MigrationExecutionResult.cs` — DTO: migration outcome with timing, applied migrations list, errors
- **NEW** `scripts/Apply-Migrations.ps1` — PowerShell: deployment script for migration execution with rollback support
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Register CustomHistoryRepository via OnConfiguring or ReplaceService
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IMigrationExecutionService, bind MigrationExecutionOptions

## Implementation Plan

1. **Implement `CustomHistoryRepository` (AC-3)**: Create in `src/UPACIP.DataAccess/Migrations/CustomHistoryRepository.cs` by inheriting from `Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal.NpgsqlHistoryRepository`.

   Override the migration history table schema to add two columns beyond the default `MigrationId` and `ProductVersion`:
   - `AppliedAtUtc` (`timestamp with time zone`, default `now()`) — records when the migration was applied (AC-3 "timestamp").
   - `MigrationChecksum` (`varchar(64)`) — SHA-256 hex string of the migration's generated SQL (AC-3 "checksum").

   Override `GetInsertScript(HistoryRow row)` to include the additional columns when recording a migration:
   ```csharp
   protected override string GetInsertScript(HistoryRow row)
   {
       // Compute checksum from the migration's SQL content
       var checksum = ComputeMigrationChecksum(row.MigrationId);
       return $@"INSERT INTO ""{TableName}"" (""{MigrationIdColumnName}"", ""{ProductVersionColumnName}"", ""AppliedAtUtc"", ""MigrationChecksum"")
                  VALUES ('{row.MigrationId}', '{row.ProductVersion}', now(), '{checksum}');";
   }
   ```

   The checksum is computed by:
   - (a) Resolving the migration class by `MigrationId` from the assembly.
   - (b) Calling `migration.Up(migrationBuilder)` to generate the `MigrationBuilder` operations.
   - (c) Using `IMigrationsSqlGenerator.Generate` to produce the SQL statements.
   - (d) Computing `SHA256.HashData(Encoding.UTF8.GetBytes(concatenatedSql))` and converting to hex string.

   This ensures that the checksum in the history table reflects the exact SQL that was applied, enabling post-migration verification (task_002) to detect tampering or drift.

   Register in `ApplicationDbContext.OnConfiguring`:
   ```csharp
   optionsBuilder.ReplaceService<IHistoryRepository, CustomHistoryRepository>();
   ```

2. **Create `MigrationExecutionOptions` configuration model**: Create in `src/UPACIP.Service/Migration/Models/MigrationExecutionOptions.cs`:
   - `int MigrationTimeoutSeconds` (default: 300 — 5 minutes per migration). Prevents runaway migrations from blocking the database indefinitely.
   - `bool CreatePreMigrationBackup` (default: `true`) — whether to trigger a backup via `IBackupExecutor` before applying migrations (safety net for production).
   - `bool EnableDetailedLogging` (default: `true`) — log each SQL statement during migration execution.
   - `bool DryRun` (default: `false`) — generate and log the SQL without executing it.
   Register via `IOptionsMonitor<MigrationExecutionOptions>`. Add to `appsettings.json`:
   ```json
   "MigrationExecution": {
     "MigrationTimeoutSeconds": 300,
     "CreatePreMigrationBackup": true,
     "EnableDetailedLogging": true,
     "DryRun": false
   }
   ```

3. **Implement `IMigrationExecutionService` / `MigrationExecutionService` (AC-2, edge case 1)**: Create in `src/UPACIP.Service/Migration/MigrationExecutionService.cs` with constructor injection of `ApplicationDbContext`, `IOptionsMonitor<MigrationExecutionOptions>`, and `ILogger<MigrationExecutionService>`.

   **Method `Task<MigrationExecutionResult> ApplyPendingMigrationsAsync(CancellationToken ct)`**:

   - (a) **Detect pending migrations**: Call `context.Database.GetPendingMigrationsAsync()` to determine which migrations need to be applied. If none, return `MigrationExecutionResult { Applied = 0, Status = "UpToDate" }`.
   - (b) **Pre-migration snapshot**: Record pre-migration state — current database version (last applied migration from `GetAppliedMigrationsAsync()`), table row counts via `pg_stat_user_tables`, and table structure checksums. Store as a `PreMigrationSnapshot` for post-migration comparison (used by task_002).
   - (c) **Pre-migration backup (optional)**: If `CreatePreMigrationBackup = true`, call `IBackupExecutor.ExecuteBackupAsync(ct)` to create a safety backup before migration. Log the backup filename for rollback reference.
   - (d) **Dry run mode**: If `DryRun = true`, call `context.Database.GenerateCreateScript()` or use `IMigrator.GenerateScript()` to produce the SQL without executing. Log the SQL and return without applying.
   - (e) **Transactional execution (AC-2)**: Apply each pending migration within an explicit transaction:
     ```csharp
     await using var transaction = await context.Database
         .BeginTransactionAsync(IsolationLevel.Serializable, ct);
     try
     {
         var migrator = context.GetService<IMigrator>();
         foreach (var pendingMigration in pendingMigrations)
         {
             Log.Information("MIGRATION_APPLYING: Name={MigrationName}", pendingMigration);
             await migrator.MigrateAsync(pendingMigration, ct);
             Log.Information("MIGRATION_APPLIED: Name={MigrationName}", pendingMigration);
         }
         await transaction.CommitAsync(ct);
     }
     catch (Exception ex)
     {
         await transaction.RollbackAsync(ct);
         Log.Error(ex, "MIGRATION_FAILED: RolledBack=true, FailedMigration={Migration}, Error={Error}",
             currentMigration, ex.Message);
         return MigrationExecutionResult.Failed(currentMigration, ex.Message);
     }
     ```
     PostgreSQL supports DDL statements inside transactions, so `CREATE TABLE`, `ALTER TABLE`, `CREATE INDEX`, etc., are all rolled back atomically if any step fails (edge case 1).
   - (f) **Post-migration logging**: After successful commit, log a summary:
     ```
     Log.Information("MIGRATION_COMPLETE: Applied={Count}, Duration={Duration}, " +
         "FromVersion={From}, ToVersion={To}",
         applied.Count, elapsed, previousVersion, currentVersion);
     ```
   - (g) Return `MigrationExecutionResult` with the list of applied migrations, total duration, and status.

4. **Create `MigrationExecutionResult` DTO**: Create in `src/UPACIP.Service/Migration/Models/MigrationExecutionResult.cs`:
   - `string Status` — "UpToDate", "Applied", "Failed", "DryRun".
   - `int AppliedCount` — number of migrations applied.
   - `List<string> AppliedMigrations` — names of applied migrations in order.
   - `string? FailedMigration` — name of the migration that failed (null on success).
   - `string? ErrorMessage` — error details (null on success).
   - `TimeSpan Duration` — total execution time.
   - `string PreviousVersion` — last migration before this run.
   - `string CurrentVersion` — last migration after this run.
   - `string? PreMigrationBackupFile` — backup file created before migration (null if disabled).
   - Static factory methods: `MigrationExecutionResult.Success(...)`, `MigrationExecutionResult.Failed(...)`, `MigrationExecutionResult.UpToDate()`.

5. **Enforce Up/Down method completeness (AC-1)**: Add a build-time validation convention. Create a Roslyn analyzer or a simpler runtime check in `MigrationExecutionService` that verifies each pending migration class has a non-empty `Down` method:
   - At migration execution time, resolve each migration class from the assembly.
   - Instantiate a `MigrationBuilder` and call `Down(builder)`.
   - If `builder.Operations.Count == 0`, log a warning: `Log.Warning("MIGRATION_MISSING_DOWN: {MigrationName} has empty Down() method. Rollback will not undo this migration.")`.
   - Do NOT block execution — this is a warning, not a gate. Some migrations legitimately have no rollback (e.g., data seed migrations). But the warning ensures developers are aware.

6. **Create deployment PowerShell script**: Create `scripts/Apply-Migrations.ps1` for production deployment:
   ```powershell
   param(
       [string]$Environment = "Production",
       [switch]$DryRun,
       [switch]$SkipBackup
   )
   # 1. Stop the UPACIP Windows Service
   # 2. Set environment-specific connection string
   # 3. Run: dotnet ef database update --project src/UPACIP.Api
   #    OR invoke the MigrationExecutionService via a CLI command
   # 4. If migration fails, log error and keep service stopped for manual intervention
   # 5. If migration succeeds, start the UPACIP Windows Service
   # 6. Verify service health via /health endpoint
   ```
   The script integrates with the `MigrationExecutionOptions.DryRun` flag and logs all output for audit. It uses `dotnet ef database update` for the actual migration or alternatively invokes a custom CLI command that triggers `MigrationExecutionService`.

7. **Configure EF Core migration conventions**: Ensure `ApplicationDbContext` is configured for:
   - Code-first migrations using the existing entity configurations.
   - PostgreSQL-specific migration generation via `Npgsql.EntityFrameworkCore.PostgreSQL`.
   - The `CustomHistoryRepository` replacement registered via `ReplaceService`.
   - Migration assembly set to `UPACIP.DataAccess` (or wherever migrations are stored).

8. **Register services and bind configuration**: In `Program.cs`: bind `MigrationExecutionOptions` via `builder.Services.Configure<MigrationExecutionOptions>(builder.Configuration.GetSection("MigrationExecution"))`, register `services.AddScoped<IMigrationExecutionService, MigrationExecutionService>()`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Monitoring/
│   │   └── Retention/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs                   ← from US_008 (entity models)
│       ├── Entities/
│       ├── Configurations/
│       └── Migrations/                               ← EF Core auto-generated migrations
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (EF Core entity models) and US_003 (PostgreSQL infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Migrations/CustomHistoryRepository.cs | Custom IHistoryRepository: extends __EFMigrationsHistory with AppliedAtUtc and MigrationChecksum |
| CREATE | src/UPACIP.Service/Migration/MigrationExecutionService.cs | IMigrationExecutionService: transactional migration execution with snapshots and logging |
| CREATE | src/UPACIP.Service/Migration/Models/MigrationExecutionOptions.cs | Config: timeout, pre-backup flag, dry run, logging level |
| CREATE | src/UPACIP.Service/Migration/Models/MigrationExecutionResult.cs | DTO: migration outcome with applied list, timing, errors |
| CREATE | scripts/Apply-Migrations.ps1 | PowerShell: deployment migration script with rollback support |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register CustomHistoryRepository via ReplaceService |
| MODIFY | src/UPACIP.Api/Program.cs | Register IMigrationExecutionService, bind MigrationExecutionOptions |

## External References

- [EF Core Migrations — Microsoft Docs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [Custom Migration History Table — EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table)
- [IMigrator Interface — EF Core](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.migrations.imigrator)
- [PostgreSQL DDL in Transactions](https://wiki.postgresql.org/wiki/Transactional_DDL_in_PostgreSQL:_A_Competitive_Analysis)
- [Npgsql EF Core Provider](https://www.npgsql.org/efcore/)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Add a new migration
dotnet ef migrations add <MigrationName> --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migrations
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] EF Core migrations generate both Up() and Down() methods (AC-1)
- [ ] Empty Down() method triggers a logged warning during execution
- [ ] Migration executes within a PostgreSQL transaction (AC-2)
- [ ] Failed migration rolls back completely — database remains in pre-migration state (edge case 1)
- [ ] __EFMigrationsHistory records MigrationId, ProductVersion, AppliedAtUtc, and MigrationChecksum (AC-3)
- [ ] MigrationChecksum is a valid SHA-256 hex string of the migration SQL
- [ ] Pre-migration backup is created when configured
- [ ] Dry run mode generates SQL without applying changes
- [ ] Structured Serilog events emitted for each migration step

## Implementation Checklist

- [ ] Implement `CustomHistoryRepository` extending NpgsqlHistoryRepository with timestamp and checksum columns
- [ ] Register CustomHistoryRepository in ApplicationDbContext via ReplaceService
- [ ] Create `MigrationExecutionOptions` with timeout, pre-backup, dry run, logging
- [ ] Implement `IMigrationExecutionService` with transactional execution and rollback handling
- [ ] Create `MigrationExecutionResult` DTO with static factory methods
- [ ] Add Down() method completeness validation warning
- [ ] Create `Apply-Migrations.ps1` deployment script
- [ ] Register MigrationExecutionOptions and IMigrationExecutionService in Program.cs
