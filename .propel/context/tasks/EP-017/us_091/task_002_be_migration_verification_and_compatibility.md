# Task - task_002_be_migration_verification_and_compatibility

## Requirement Reference

- User Story: us_091
- Story Location: .propel/context/tasks/EP-017/us_091/us_091.md
- Acceptance Criteria:
  - AC-4: Given schema changes are backward-compatible, When migrated, Then existing application instances continue functioning during the migration (zero-downtime support).
  - AC-5: Given a migration completes, When post-migration verification runs, Then checksums verify table structure integrity and row counts match pre-migration expectations.
- Edge Case:
  - How does the system handle a migration that cannot be made backward-compatible? The migration is split into multiple sequential migrations (expand-contract pattern) to maintain compatibility.

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

Implement the post-migration integrity verification service and backward compatibility guard that ensures schema changes are safe for zero-downtime deployments (DR-031, DR-032, NFR-021). This task builds on the migration pipeline engine (task_001) by adding two capabilities: (1) a `MigrationVerificationService` that runs automated post-migration checks — table structure checksums, row count comparison against pre-migration snapshots, foreign key constraint validation, and migration history checksum verification (AC-5), and (2) a `CompatibilityGuard` that analyzes pending EF Core migrations for backward-incompatible operations and flags them before execution — detecting breaking changes such as column drops, column renames, type narrowing, and NOT NULL additions without defaults (AC-4). When the guard detects a breaking change, it recommends the expand-contract migration pattern (edge case 2): split the breaking change into a safe "expand" migration (add new column, copy data) deployed first, followed by a "contract" migration (remove old column) deployed after all application instances have been updated. The verification service runs automatically after `MigrationExecutionService.ApplyPendingMigrationsAsync()` completes and produces a structured report persisted to a `MigrationVerificationLog` entity.

## Dependent Tasks

- US_091 task_001_be_migration_pipeline_engine — Requires MigrationExecutionService, CustomHistoryRepository, MigrationExecutionResult, pre-migration snapshot.
- US_008 — Requires EF Core entity models and ApplicationDbContext.
- US_003 — Requires PostgreSQL database infrastructure.

## Impacted Components

- **NEW** `src/UPACIP.Service/Migration/MigrationVerificationService.cs` — IMigrationVerificationService: post-migration integrity checks (checksums, row counts, FK validation)
- **NEW** `src/UPACIP.Service/Migration/CompatibilityGuard.cs` — ICompatibilityGuard: analyze pending migrations for backward-incompatible operations
- **NEW** `src/UPACIP.Service/Migration/Models/VerificationResult.cs` — DTO: verification outcome with per-table checksums, row counts, FK results
- **NEW** `src/UPACIP.Service/Migration/Models/CompatibilityReport.cs` — DTO: compatibility analysis with breaking changes and expand-contract recommendations
- **NEW** `src/UPACIP.Service/Migration/Models/MigrationVerificationLog.cs` — Entity: verification audit trail
- **MODIFY** `src/UPACIP.Service/Migration/MigrationExecutionService.cs` — Call verification after successful migration, call compatibility guard before execution
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<MigrationVerificationLog>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IMigrationVerificationService, ICompatibilityGuard

## Implementation Plan

1. **Implement `IMigrationVerificationService` / `MigrationVerificationService` (AC-5)**: Create in `src/UPACIP.Service/Migration/MigrationVerificationService.cs` with constructor injection of `ApplicationDbContext`, `IOptionsMonitor<MigrationExecutionOptions>`, and `ILogger<MigrationVerificationService>`.

   **Method `Task<VerificationResult> VerifyMigrationIntegrityAsync(PreMigrationSnapshot snapshot, CancellationToken ct)`**:

   - (a) **Table structure checksum verification (DR-032)**: Query the PostgreSQL information schema to compute a deterministic checksum of each table's structure:
     ```sql
     SELECT table_name,
            md5(string_agg(
              column_name || ':' || data_type || ':' || COALESCE(character_maximum_length::text, '') || ':' ||
              is_nullable || ':' || COALESCE(column_default, ''),
              ',' ORDER BY ordinal_position
            )) AS structure_checksum
     FROM information_schema.columns
     WHERE table_schema = 'public'
     GROUP BY table_name
     ORDER BY table_name;
     ```
     Compare post-migration checksums against the pre-migration snapshot. Tables that were intentionally modified by the migration will have changed checksums — these are expected. Tables NOT targeted by the migration should have identical checksums. If an unrelated table's checksum changed, flag it as an anomaly:
     ```
     Log.Warning("MIGRATION_VERIFICATION_ANOMALY: Table={Table} structure changed " +
       "but was not targeted by migration {Migration}");
     ```

   - (b) **Row count validation (AC-5)**: Query `pg_stat_user_tables` for post-migration row counts and compare against the pre-migration snapshot:
     ```sql
     SELECT schemaname, relname, n_live_tup
     FROM pg_stat_user_tables
     WHERE schemaname = 'public'
     ORDER BY relname;
     ```
     For each table, compare counts. Schema-only migrations (DDL) should not change row counts. If a migration includes data transformations (DML), expected row count changes should be documented in the migration class via a custom attribute or comment. Unexpected row count changes are flagged:
     ```
     Log.Warning("MIGRATION_VERIFICATION_ROWCOUNT: Table={Table}, " +
       "Pre={PreCount}, Post={PostCount}, Delta={Delta}");
     ```

   - (c) **Foreign key constraint validation**: Verify all FK constraints remain valid post-migration by querying for unvalidated constraints:
     ```sql
     SELECT conname, conrelid::regclass AS table_name
     FROM pg_constraint
     WHERE contype = 'f' AND NOT convalidated;
     ```
     If any FK constraints are unvalidated, attempt to validate them: `ALTER TABLE {table} VALIDATE CONSTRAINT {constraint}`. Log results.

   - (d) **Migration history checksum cross-check**: Read the `MigrationChecksum` from the `__EFMigrationsHistory` table (populated by `CustomHistoryRepository` from task_001). Recompute the checksum from the migration class SQL and verify it matches. A mismatch indicates the migration class was modified after application or the SQL was tampered with:
     ```
     Log.Error("MIGRATION_CHECKSUM_MISMATCH: Migration={Name}, " +
       "Recorded={Recorded}, Computed={Computed}. Possible tampering.");
     ```

   - (e) Return `VerificationResult` with all check outcomes.

2. **Create `VerificationResult` DTO**: Create in `src/UPACIP.Service/Migration/Models/VerificationResult.cs`:
   - `bool OverallPassed` — true if all checks passed without errors (warnings are acceptable).
   - `List<TableStructureCheck> StructureChecks` — per-table structure checksum comparison.
   - `List<TableRowCountCheck> RowCountChecks` — per-table row count comparison.
   - `bool ForeignKeyConstraintsPassed` — true if all FK constraints are valid.
   - `List<string> ForeignKeyIssues` — details of any FK validation failures.
   - `bool HistoryChecksumPassed` — true if migration checksums match.
   - `List<string> Warnings` — non-fatal anomalies detected.
   - `List<string> Errors` — fatal integrity issues detected.
   - `DateTime VerifiedAtUtc` — when verification was performed.

   Supporting records:
   - `TableStructureCheck { TableName, PreChecksum, PostChecksum, ExpectedChange, Matched }`.
   - `TableRowCountCheck { TableName, PreCount, PostCount, Delta, ExpectedChange }`.

3. **Implement `ICompatibilityGuard` / `CompatibilityGuard` (AC-4, edge case 2)**: Create in `src/UPACIP.Service/Migration/CompatibilityGuard.cs` with constructor injection of `ApplicationDbContext` and `ILogger<CompatibilityGuard>`.

   **Method `Task<CompatibilityReport> AnalyzePendingMigrationsAsync(CancellationToken ct)`**:

   - (a) Retrieve pending migrations via `context.Database.GetPendingMigrationsAsync()`.
   - (b) For each pending migration, resolve the migration class and instantiate a `MigrationBuilder`. Call `Up(builder)` to capture the operations list.
   - (c) Analyze each `MigrationOperation` for backward-incompatible patterns:

     | Operation Type | Breaking? | Recommendation |
     |----------------|-----------|----------------|
     | `DropColumnOperation` | YES | Expand-contract: add new column first, migrate data, then drop in a later migration |
     | `RenameColumnOperation` | YES | Expand-contract: add new column, copy data, update app to use both, then drop old |
     | `AlterColumnOperation` (type narrowing) | YES | Add new column with new type, migrate data, then drop old |
     | `AlterColumnOperation` (add NOT NULL without default) | YES | Add default value first, backfill nulls, then add NOT NULL constraint |
     | `DropTableOperation` | YES | Remove app references first, then drop in a later migration |
     | `RenameTableOperation` | YES | Create view with old name pointing to new table during transition |
     | `AddColumnOperation` (nullable) | NO | Safe — existing queries ignore new columns |
     | `AddColumnOperation` (NOT NULL with default) | NO | Safe — default value satisfies existing rows |
     | `CreateTableOperation` | NO | Safe — new table doesn't affect existing queries |
     | `CreateIndexOperation` | MAYBE | Safe if `CONCURRENTLY` (non-blocking); blocking otherwise |

   - (d) For `CreateIndexOperation`, check if the migration uses `IsUnique` or is on a large table — recommend `CONCURRENTLY` index creation for zero-downtime: `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ...")` instead of the standard `CreateIndex` which takes a table lock.
   - (e) Return `CompatibilityReport` with findings and expand-contract recommendations.

4. **Create `CompatibilityReport` DTO**: Create in `src/UPACIP.Service/Migration/Models/CompatibilityReport.cs`:
   - `bool IsBackwardCompatible` — true if no breaking operations found.
   - `List<BreakingChange> BreakingChanges` — list of detected incompatible operations.
   - `List<string> Recommendations` — expand-contract pattern suggestions.
   - `List<string> SafeOperations` — operations confirmed as backward-compatible.
   - `string MigrationName` — which migration was analyzed.

   Supporting record:
   - `BreakingChange { MigrationName, OperationType, TableName, ColumnName, Severity, ExpandContractSuggestion }`.
   - `Severity` enum: `Warning`, `Breaking`.

5. **Create `MigrationVerificationLog` entity**: Create in `src/UPACIP.Service/Migration/Models/MigrationVerificationLog.cs`:
   - `Guid Id` (PK).
   - `string MigrationName` — which migration was verified.
   - `bool StructureCheckPassed` — table structure verification result.
   - `bool RowCountCheckPassed` — row count verification result.
   - `bool ForeignKeyCheckPassed` — FK constraint verification result.
   - `bool HistoryChecksumPassed` — migration checksum cross-check result.
   - `bool OverallPassed` — aggregate result.
   - `string? WarningDetails` — JSON-serialized warnings.
   - `string? ErrorDetails` — JSON-serialized errors.
   - `DateTime VerifiedAtUtc` — when verification ran.
   Add `DbSet<MigrationVerificationLog>` to `ApplicationDbContext` with an index on `VerifiedAtUtc`.

6. **Integrate into `MigrationExecutionService`**: Modify the `ApplyPendingMigrationsAsync` method (from task_001) to:

   **Before execution** — run the compatibility guard:
   ```csharp
   var compatibilityReport = await _compatibilityGuard
       .AnalyzePendingMigrationsAsync(ct);
   if (!compatibilityReport.IsBackwardCompatible)
   {
       Log.Warning("MIGRATION_COMPATIBILITY_WARNING: " +
           "BreakingChanges={Count}. Review expand-contract recommendations.",
           compatibilityReport.BreakingChanges.Count);
       // Log each breaking change with its recommendation
       foreach (var change in compatibilityReport.BreakingChanges)
       {
           Log.Warning("BREAKING_CHANGE: Migration={Migration}, " +
               "Operation={Op}, Table={Table}, Column={Column}, " +
               "Suggestion={Suggestion}",
               change.MigrationName, change.OperationType,
               change.TableName, change.ColumnName,
               change.ExpandContractSuggestion);
       }
       // Do NOT block execution — log warnings for developer awareness
       // Production deployments should review compatibility reports
   }
   ```

   **After successful execution** — run verification:
   ```csharp
   var verificationResult = await _verificationService
       .VerifyMigrationIntegrityAsync(preMigrationSnapshot, ct);
   if (!verificationResult.OverallPassed)
   {
       Log.Error("MIGRATION_VERIFICATION_FAILED: Errors={Errors}",
           string.Join("; ", verificationResult.Errors));
   }
   // Persist verification log
   await PersistVerificationLogAsync(verificationResult, ct);
   ```

7. **Document expand-contract pattern (edge case 2)**: The compatibility guard produces actionable recommendations when breaking changes are detected. The standard expand-contract workflow for a column rename:

   **Phase 1 — Expand (migration_001)**:
   - Add new column with the target name (nullable or with default).
   - Add a database trigger or application logic to sync old → new column.
   - Deploy application version that writes to both columns, reads from new.

   **Phase 2 — Contract (migration_002, deployed later)**:
   - Remove the old column after all application instances read from the new column.
   - Remove the sync trigger.

   This pattern is documented in the `CompatibilityReport.Recommendations` field for each breaking change, providing developers with the specific migration split needed.

8. **Register services**: In `Program.cs`: register `services.AddScoped<IMigrationVerificationService, MigrationVerificationService>()`, register `services.AddScoped<ICompatibilityGuard, CompatibilityGuard>()`. Add `DbSet<MigrationVerificationLog>` to `ApplicationDbContext`.

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
│   │   ├── Migration/
│   │   │   ├── MigrationExecutionService.cs         ← from task_001
│   │   │   └── Models/
│   │   │       ├── MigrationExecutionOptions.cs     ← from task_001
│   │   │       └── MigrationExecutionResult.cs      ← from task_001
│   │   ├── Monitoring/
│   │   └── Retention/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       ├── Configurations/
│       └── Migrations/
│           └── CustomHistoryRepository.cs           ← from task_001
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
│   └── Apply-Migrations.ps1                         ← from task_001
```

> Assumes US_091 task_001 (migration pipeline engine), US_008 (EF Core entity models), and US_003 (PostgreSQL infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Migration/MigrationVerificationService.cs | IMigrationVerificationService: post-migration structure checksums, row counts, FK validation |
| CREATE | src/UPACIP.Service/Migration/CompatibilityGuard.cs | ICompatibilityGuard: analyze migrations for breaking operations, recommend expand-contract |
| CREATE | src/UPACIP.Service/Migration/Models/VerificationResult.cs | DTO: per-table structure/row count checks, FK results, checksum cross-check |
| CREATE | src/UPACIP.Service/Migration/Models/CompatibilityReport.cs | DTO: breaking changes list with expand-contract recommendations |
| CREATE | src/UPACIP.Service/Migration/Models/MigrationVerificationLog.cs | Entity: verification audit trail with all check outcomes |
| MODIFY | src/UPACIP.Service/Migration/MigrationExecutionService.cs | Add pre-execution compatibility guard, post-execution verification |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<MigrationVerificationLog> |
| MODIFY | src/UPACIP.Api/Program.cs | Register IMigrationVerificationService, ICompatibilityGuard |

## External References

- [EF Core Migrations Operations — Microsoft Docs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations)
- [information_schema.columns — PostgreSQL 16](https://www.postgresql.org/docs/16/infoschema-columns.html)
- [pg_constraint — PostgreSQL 16](https://www.postgresql.org/docs/16/catalog-pg-constraint.html)
- [Zero-Downtime Migrations — Expand-Contract Pattern](https://www.prisma.io/dataguide/types/relational/expand-and-contract-pattern)
- [CREATE INDEX CONCURRENTLY — PostgreSQL 16](https://www.postgresql.org/docs/16/sql-createindex.html#SQL-CREATEINDEX-CONCURRENTLY)
- [pg_stat_user_tables — PostgreSQL 16](https://www.postgresql.org/docs/16/monitoring-stats.html#MONITORING-PG-STAT-ALL-TABLES-VIEW)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Post-migration table structure checksums are computed and compared against pre-migration snapshot (AC-5)
- [ ] Post-migration row counts match pre-migration expectations for DDL-only migrations (AC-5)
- [ ] Unexpected row count changes are flagged with a warning
- [ ] FK constraint validation detects and logs unvalidated constraints
- [ ] Migration history checksum cross-check detects mismatches
- [ ] CompatibilityGuard detects DropColumn, RenameColumn, type narrowing as breaking (AC-4)
- [ ] CompatibilityGuard recommends expand-contract pattern for breaking changes (edge case 2)
- [ ] AddColumn (nullable) and CreateTable are classified as safe
- [ ] MigrationVerificationLog persists verification outcomes
- [ ] Verification runs automatically after successful migration execution

## Implementation Checklist

- [ ] Implement `IMigrationVerificationService` with structure checksum, row count, FK, and history verification
- [ ] Create `VerificationResult` DTO with per-table check results and supporting records
- [ ] Implement `ICompatibilityGuard` analyzing MigrationOperation types for breaking changes
- [ ] Create `CompatibilityReport` DTO with breaking changes and expand-contract recommendations
- [ ] Create `MigrationVerificationLog` entity and add DbSet to ApplicationDbContext
- [ ] Integrate compatibility guard (pre-execution) into MigrationExecutionService
- [ ] Integrate verification service (post-execution) into MigrationExecutionService
- [ ] Register IMigrationVerificationService and ICompatibilityGuard in Program.cs
