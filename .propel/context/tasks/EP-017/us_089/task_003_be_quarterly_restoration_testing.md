# Task - task_003_be_quarterly_restoration_testing

## Requirement Reference

- User Story: us_089
- Story Location: .propel/context/tasks/EP-017/us_089/us_089.md
- Acceptance Criteria:
  - AC-3: Given the quarterly testing schedule, When a restoration test is due, Then the system provides documented procedures and the admin can restore the latest backup to a test environment and verify data integrity.
  - AC-4: Given a restoration test completes, When the results are verified, Then the system validates row counts, checksums, and referential integrity against the source backup.
- Edge Case:
  - How does the system handle encryption key management for backups? Encryption keys are stored separately from backups using the system's key management configuration; key loss renders backups unrecoverable.

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
| Backend | Serilog | 8.x |
| Database | PostgreSQL (pg_restore) | 16.x |
| Infrastructure | Windows Server 2022 | - |

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

Implement a `BackupRestorationTestService` that enables admin-triggered quarterly restoration testing with automated data integrity validation (DR-026). The service provides an API endpoint for administrators to initiate a restoration test against the latest backup, restoring it to a separate test database. The restoration flow decrypts the encrypted `.dump.enc` file (using `IBackupEncryptionService.DecryptBackupAsync` from US_089 task_001), invokes `pg_restore` to load the backup into a designated test database, and then runs a comprehensive integrity validation suite (AC-4). The validation compares row counts across all application tables, verifies referential integrity via foreign key constraint checks, and computes a table-level checksum comparison between the source production database and the restored test database. Results are persisted to a `RestorationTestLog` entity and returned via the API response. The system also tracks quarterly test scheduling — logging when the last test was performed and alerting if the quarterly window is about to expire without a test.

## Dependent Tasks

- US_088 task_001_be_backup_orchestration_service — Requires BackupLog, BackupOptions for backup directory and pg tools path.
- US_089 task_001_be_backup_encryption_service — Requires IBackupEncryptionService.DecryptBackupAsync for decrypting backup files before restore.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/BackupRestorationTestService.cs` — IBackupRestorationTestService: restore to test DB, run integrity validation
- **NEW** `src/UPACIP.Service/Backup/Models/RestorationTestOptions.cs` — Configuration: test database, pg_restore path, quarterly schedule
- **NEW** `src/UPACIP.Service/Backup/Models/RestorationTestResult.cs` — DTO: validation results with row counts, checksum, referential integrity
- **NEW** `src/UPACIP.Service/Backup/Models/RestorationTestLog.cs` — Entity: restoration test audit trail
- **NEW** `src/UPACIP.Api/Controllers/BackupController.cs` — API: POST restore-test, GET restoration-test-status
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<RestorationTestLog>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind RestorationTestOptions, register IBackupRestorationTestService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add RestorationTest configuration section

## Implementation Plan

1. **Create `RestorationTestOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/RestorationTestOptions.cs`:
   - `string PgRestorePath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_restore.exe"` — standard Windows PostgreSQL installation path).
   - `string TestDatabaseName` (default: `"upacip_restore_test"`) — separate database for restoration testing; NEVER the production database.
   - `string TestDatabaseHost` (default: `"localhost"`) — test database host (can be the same server or a separate test server).
   - `int TestDatabasePort` (default: `5432`) — test database port.
   - `string TestDatabaseUsername` (default: `"upacip_test"`) — dedicated test user with createdb permissions.
   - `int QuarterlyAlertDaysBeforeDue` (default: 14) — days before quarterly deadline to begin alerting.
   - `int MaxRestorationTimeoutMinutes` (default: 120) — maximum time for pg_restore before timeout.
   Register via `IOptionsMonitor<RestorationTestOptions>`. Add to `appsettings.json`:
   ```json
   "RestorationTest": {
     "PgRestorePath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_restore.exe",
     "TestDatabaseName": "upacip_restore_test",
     "TestDatabaseHost": "localhost",
     "TestDatabasePort": 5432,
     "TestDatabaseUsername": "upacip_test",
     "QuarterlyAlertDaysBeforeDue": 14,
     "MaxRestorationTimeoutMinutes": 120
   }
   ```
   The test database password is provided via environment variable `RestorationTest__TestDatabasePassword` (never in `appsettings.json`).

2. **Create `RestorationTestResult` DTO**: Create in `src/UPACIP.Service/Backup/Models/RestorationTestResult.cs`:
   - `bool OverallSuccess` — true only if all three validation checks pass.
   - `string BackupFileName` — which backup file was restored.
   - `DateTime RestoredAtUtc` — when the restoration was performed.
   - `TimeSpan RestorationDuration` — time for pg_restore to complete.
   - `List<TableRowCountComparison> RowCountResults` — per-table row count comparison.
   - `bool ReferentialIntegrityPassed` — true if no FK constraint violations detected.
   - `List<string> ReferentialIntegrityErrors` — details of any FK violations.
   - `bool ChecksumPassed` — true if table checksums match between source and restored.
   - `List<TableChecksumComparison> ChecksumResults` — per-table checksum comparison.
   - `string? ErrorMessage` — restoration-level error (null if restore succeeded).

   Supporting records:
   - `TableRowCountComparison { TableName, SourceCount, RestoredCount, Matched }`.
   - `TableChecksumComparison { TableName, SourceChecksum, RestoredChecksum, Matched }`.

3. **Create `RestorationTestLog` entity**: Create in `src/UPACIP.Service/Backup/Models/RestorationTestLog.cs`:
   - `Guid Id` (PK).
   - `string BackupFileName` — which backup was tested.
   - `bool OverallSuccess` — whether all validations passed.
   - `bool RowCountsPassed` — row count validation result.
   - `bool ReferentialIntegrityPassed` — FK constraint validation result.
   - `bool ChecksumsPassed` — checksum validation result.
   - `TimeSpan RestorationDuration` — pg_restore execution time.
   - `string? ErrorDetails` — JSON-serialized error details if any check failed.
   - `string PerformedBy` — admin user who triggered the test.
   - `DateTime CreatedAtUtc` — when this test was executed.
   Add `DbSet<RestorationTestLog>` to `ApplicationDbContext`. Create EF Core configuration with index on `CreatedAtUtc`.

4. **Implement `IBackupRestorationTestService` / `BackupRestorationTestService`**: Create in `src/UPACIP.Service/Backup/BackupRestorationTestService.cs` with constructor injection of `IOptionsMonitor<RestorationTestOptions>`, `IOptionsMonitor<BackupOptions>`, `IBackupEncryptionService`, `ApplicationDbContext`, and `ILogger<BackupRestorationTestService>`.

   **Method `Task<RestorationTestResult> RunRestorationTestAsync(string performedBy, CancellationToken ct)`**:

   - (a) **Find latest backup**: Scan the backup directory (from `BackupOptions.BackupDirectory`) for the most recent `.dump.enc` file (or `.dump` if encryption disabled).
   - (b) **Decrypt backup**: If the file is encrypted (`.dump.enc`), call `IBackupEncryptionService.DecryptBackupAsync(encryptedPath, tempDecryptedPath, ct)` to produce a temporary plaintext `.dump` file for restoration.
   - (c) **Drop and recreate test database**: Execute `psql -c "DROP DATABASE IF EXISTS {testDb}"` followed by `psql -c "CREATE DATABASE {testDb}"` using the configured test database credentials. Uses `System.Diagnostics.Process` similar to the `BackupExecutor` pattern from US_088. The `PGPASSWORD` environment variable is set for the process (never in CLI args).
   - (d) **Restore backup**: Execute `pg_restore --host={testHost} --port={testPort} --username={testUser} --dbname={testDb} --no-owner --no-privileges {decryptedFilePath}`. Measure elapsed time via `Stopwatch`. If exit code is non-zero, capture stderr and return `RestorationTestResult { OverallSuccess = false, ErrorMessage = stderr }`.
   - (e) **Validate row counts (AC-4)**: Query both production and test databases for row counts of all application tables:
     ```sql
     SELECT schemaname, relname, n_live_tup
     FROM pg_stat_user_tables
     ORDER BY schemaname, relname;
     ```
     Compare each table's row count. Allow a tolerance of 0 rows difference (exact match required for a point-in-time backup). Record results in `List<TableRowCountComparison>`.
   - (f) **Validate referential integrity (AC-4)**: On the test database, run a comprehensive FK constraint check by attempting to identify orphaned foreign keys:
     ```sql
     SELECT conname, conrelid::regclass AS table_name,
            confrelid::regclass AS referenced_table
     FROM pg_constraint
     WHERE contype = 'f'
       AND NOT convalidated;
     ```
     Additionally, for each FK constraint, execute a validation query to detect orphaned rows. Record any violations in `ReferentialIntegrityErrors`.
   - (g) **Validate checksums (AC-4)**: For each application table, compute a deterministic checksum using `md5(CAST(t.* AS text))` aggregation on both production and test databases:
     ```sql
     SELECT md5(string_agg(md5(CAST(t.* AS text)), '' ORDER BY t.id))
     FROM {table_name} t;
     ```
     Compare checksums between source and restored. Record results in `List<TableChecksumComparison>`.
   - (h) **Cleanup**: Delete the temporary decrypted `.dump` file. Optionally drop the test database after validation (configurable — default: keep for manual inspection for 24 hours).
   - (i) **Persist results**: Save a `RestorationTestLog` entry with all validation outcomes.
   - (j) **Log summary**: `Log.Information("RESTORATION_TEST_COMPLETE: Backup={FileName}, RowCounts={RowCountResult}, Integrity={IntegrityResult}, Checksums={ChecksumResult}, Duration={Duration}")`.

5. **Implement quarterly schedule tracking**: Add a method `Task<QuarterlyTestStatus> GetQuarterlyTestStatusAsync()`:
   - Query the latest `RestorationTestLog` entry from the database.
   - Compute the current quarter boundaries (Q1: Jan-Mar, Q2: Apr-Jun, Q3: Jul-Sep, Q4: Oct-Dec).
   - If no test exists for the current quarter, compute days remaining until quarter end.
   - If days remaining < `QuarterlyAlertDaysBeforeDue`, return `Status = "DuesSoon"` with the deadline date.
   - If the quarter has ended without a test, return `Status = "Overdue"`.
   - If a test exists for the current quarter, return `Status = "Completed"` with test date and result.
   This status is exposed via the API and can trigger Serilog alerts via a lightweight BackgroundService check (weekly).

6. **Create `BackupController` API endpoints**: Create in `src/UPACIP.Api/Controllers/BackupController.cs`:

   **POST `/api/admin/backup/restore-test`** — Admin-only endpoint (requires admin role authorization):
   - Triggers `IBackupRestorationTestService.RunRestorationTestAsync`.
   - Returns `RestorationTestResult` with all validation details.
   - Long-running operation: consider returning `202 Accepted` with a polling endpoint if restoration exceeds typical request timeout.

   **GET `/api/admin/backup/restore-test/status`** — Admin-only endpoint:
   - Returns `QuarterlyTestStatus` with last test date, result, and next due date.
   - Used by admin dashboards to monitor quarterly compliance.

7. **Secure the restoration process**:
   - The test database password is provided via environment variable only — never in `appsettings.json` or logs.
   - The `DROP DATABASE` and `CREATE DATABASE` commands use `PGPASSWORD` environment variable for the child process.
   - The API endpoints require admin role authorization — only security officers and system administrators can trigger restoration tests.
   - The temporary decrypted `.dump` file is deleted immediately after restoration completes (even on failure).
   - Production database connection string is used for read-only row count and checksum queries only.

8. **Register services and bind configuration**: In `Program.cs`: bind `RestorationTestOptions` via `builder.Services.Configure<RestorationTestOptions>(builder.Configuration.GetSection("RestorationTest"))`, register `services.AddScoped<IBackupRestorationTestService, BackupRestorationTestService>()`, register `BackupController`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── BackupController.cs                  ← NEW (this task)
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   │   ├── DatabaseBackupService.cs             ← from US_088 task_001
│   │   │   ├── BackupExecutor.cs                    ← from US_088 task_001
│   │   │   ├── BackupRetentionService.cs            ← from US_088 task_002
│   │   │   ├── BackupEncryptionService.cs           ← from US_089 task_001
│   │   │   ├── BackupReplicationService.cs          ← from US_089 task_002
│   │   │   └── Models/
│   │   │       ├── BackupOptions.cs                 ← from US_088 task_001
│   │   │       ├── BackupResult.cs                  ← from US_088 task_001 + US_089 task_001
│   │   │       ├── BackupLog.cs                     ← from US_088 task_001
│   │   │       ├── BackupRetentionOptions.cs        ← from US_088 task_002
│   │   │       ├── BackupFileInfo.cs                ← from US_088 task_002
│   │   │       ├── EncryptionOptions.cs             ← from US_089 task_001
│   │   │       ├── ReplicationOptions.cs            ← from US_089 task_002
│   │   │       └── ReplicationResult.cs             ← from US_089 task_002
│   │   ├── Monitoring/
│   │   └── Retention/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_088 (backup orchestration/retention), US_063 (AES-256 encryption), US_089 task_001 (backup encryption), and US_089 task_002 (geographic replication) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/BackupRestorationTestService.cs | IBackupRestorationTestService: restore to test DB, row count/checksum/FK validation |
| CREATE | src/UPACIP.Service/Backup/Models/RestorationTestOptions.cs | Config: pg_restore path, test DB name/host/port, quarterly alert threshold |
| CREATE | src/UPACIP.Service/Backup/Models/RestorationTestResult.cs | DTO: validation results with per-table row counts, checksums, FK integrity |
| CREATE | src/UPACIP.Service/Backup/Models/RestorationTestLog.cs | Entity: restoration test audit trail with all validation outcomes |
| CREATE | src/UPACIP.Api/Controllers/BackupController.cs | API: POST restore-test, GET restore-test status (admin-only) |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<RestorationTestLog> |
| MODIFY | src/UPACIP.Api/Program.cs | Bind RestorationTestOptions, register IBackupRestorationTestService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add RestorationTest configuration section |

## External References

- [pg_restore — PostgreSQL 16 Documentation](https://www.postgresql.org/docs/16/app-pgrestore.html)
- [pg_stat_user_tables — PostgreSQL System Catalog](https://www.postgresql.org/docs/16/monitoring-stats.html#MONITORING-PG-STAT-ALL-TABLES-VIEW)
- [pg_constraint — PostgreSQL System Catalog](https://www.postgresql.org/docs/16/catalog-pg-constraint.html)
- [System.Diagnostics.Process — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process)
- [Authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Latest encrypted backup is decrypted and restored to the test database (AC-3)
- [ ] Row counts match between production and restored test database per table (AC-4)
- [ ] Referential integrity validation detects zero FK violations in restored database (AC-4)
- [ ] Table checksums match between production and restored test database (AC-4)
- [ ] Temporary decrypted `.dump` file is deleted after restoration completes
- [ ] RestorationTestLog records all validation outcomes with admin identity
- [ ] API endpoints require admin role authorization
- [ ] Quarterly status correctly reports "Completed", "DueSoon", or "Overdue"
- [ ] Test database credentials are never logged or stored in appsettings.json

## Implementation Checklist

- [ ] Create `RestorationTestOptions` with pg_restore path, test DB config, quarterly alert threshold
- [ ] Create `RestorationTestResult` DTO with row count, checksum, and FK validation results
- [ ] Create `RestorationTestLog` entity and add DbSet to ApplicationDbContext
- [ ] Implement `IBackupRestorationTestService` with decrypt → restore → validate pipeline
- [ ] Implement row count, referential integrity, and checksum validation queries
- [ ] Implement quarterly schedule tracking with alert status
- [ ] Create `BackupController` with admin-only restore-test and status endpoints
- [ ] Register RestorationTestOptions and IBackupRestorationTestService in Program.cs
