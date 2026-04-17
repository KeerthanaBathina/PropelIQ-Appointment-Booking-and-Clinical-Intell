# Task - task_002_be_pitr_recovery_service

## Requirement Reference

- User Story: us_090
- Story Location: .propel/context/tasks/EP-017/us_090/us_090.md
- Acceptance Criteria:
  - AC-2: Given a data corruption or error is detected, When an admin initiates point-in-time recovery, Then the system can restore the database to any timestamp within the retention window.
  - AC-3: Given recovery is initiated, When the process completes, Then the admin verifies data integrity with checksum verification and row count validation.
  - AC-4: Given recovery targets are defined, When a disaster occurs, Then the system achieves RPO of 1 hour and RTO of 4 hours per the design specifications.
- Edge Case:
  - What happens when transaction logs are corrupted? System falls back to the most recent complete backup and logs the gap period as "unrecoverable."
  - How does the system handle recovery when the target timestamp falls between two transaction log checkpoints? System applies all logs up to the closest checkpoint before the target timestamp.

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
| Database | PostgreSQL (pg_restore, pg_basebackup) | 16.x |
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

Implement a `PointInTimeRecoveryService` that enables administrators to restore the PostgreSQL database to any specific timestamp within the WAL retention window (DR-027, FR-100). The recovery flow is admin-triggered via an API endpoint, performs pre-flight validation (WAL availability, target timestamp feasibility), executes the recovery to a separate test/recovery database, runs post-recovery integrity verification (AC-3), and produces a detailed recovery report. The PITR process uses PostgreSQL's native recovery mechanism: restore the most recent base backup (daily `.dump.enc` file from US_088/US_089) that predates the target timestamp, decrypt it (via `IBackupEncryptionService` from US_089 task_001), configure `recovery_target_time` in `postgresql.conf` (or `postgresql.auto.conf`) on the recovery database instance, and let PostgreSQL replay archived WAL segments up to the specified timestamp. When the target timestamp falls between two WAL checkpoints (edge case 2), PostgreSQL natively replays all WAL records up to the closest point before the target — this is handled by the `recovery_target_time` parameter with `recovery_target_action = 'pause'`, allowing the admin to verify before promoting. If WAL segments are corrupted (edge case 1), the service detects this during pre-flight via `WalArchivalMonitoringService.GetArchivalStatusAsync()` and falls back to the nearest complete base backup, logging the unrecoverable gap period. Post-recovery, the service validates row counts and checksums against a reference snapshot, matching the integrity validation pattern from the quarterly restoration test (US_089 task_003). The system achieves NFR-024 (RPO ≤ 1 hour) via 15-minute WAL archiving (from task_001) and NFR-025 (RTO ≤ 4 hours) via the automated recovery pipeline.

## Dependent Tasks

- US_090 task_001_be_wal_archival_service — Requires WalArchivalMonitoringService for WAL health status and archived WAL segments.
- US_088 task_001_be_backup_orchestration_service — Requires BackupOptions for base backup directory and pg tool paths.
- US_089 task_001_be_backup_encryption_service — Requires IBackupEncryptionService.DecryptBackupAsync for decrypting base backup files.
- US_089 task_003_be_quarterly_restoration_testing — Reuses integrity validation patterns (row counts, checksums).

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/PointInTimeRecoveryService.cs` — IPointInTimeRecoveryService: PITR orchestration with pre-flight, restore, WAL replay, validation
- **NEW** `src/UPACIP.Service/Backup/Models/PitrOptions.cs` — Configuration: recovery database, pg tool paths, timeout
- **NEW** `src/UPACIP.Service/Backup/Models/PitrRequest.cs` — DTO: admin request with target timestamp and options
- **NEW** `src/UPACIP.Service/Backup/Models/PitrResult.cs` — DTO: recovery result with integrity validation, WAL coverage, timing
- **NEW** `src/UPACIP.Service/Backup/Models/RecoveryLog.cs` — Entity: PITR audit trail
- **MODIFY** `src/UPACIP.Api/Controllers/BackupController.cs` — Add POST pitr endpoint, GET pitr-feasibility endpoint
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<RecoveryLog>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind PitrOptions, register IPointInTimeRecoveryService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add PitrRecovery configuration section

## Implementation Plan

1. **Create `PitrOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/PitrOptions.cs`:
   - `string RecoveryDatabaseName` (default: `"upacip_pitr_recovery"`) — separate database for PITR; NEVER the production database.
   - `string RecoveryDataDirectory` (default: `"D:\\Recovery\\PgData"`) — temporary PostgreSQL data directory for the recovery instance.
   - `string PgBasebackupPath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_basebackup.exe"`) — path to pg_basebackup.
   - `string PgRestorePath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_restore.exe"`) — path to pg_restore.
   - `string PgCtlPath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_ctl.exe"`) — path to pg_ctl for managing recovery instance.
   - `int RecoveryPort` (default: `5433`) — port for the recovery PostgreSQL instance (separate from production 5432).
   - `int MaxRecoveryTimeoutMinutes` (default: 240) — 4 hours max, aligned with NFR-025 RTO target.
   - `bool AutoDropRecoveryDb` (default: `false`) — whether to automatically drop the recovery database after validation (default: keep for admin inspection).
   Register via `IOptionsMonitor<PitrOptions>`. Add to `appsettings.json`:
   ```json
   "PitrRecovery": {
     "RecoveryDatabaseName": "upacip_pitr_recovery",
     "RecoveryDataDirectory": "D:\\Recovery\\PgData",
     "PgBasebackupPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_basebackup.exe",
     "PgRestorePath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_restore.exe",
     "PgCtlPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_ctl.exe",
     "RecoveryPort": 5433,
     "MaxRecoveryTimeoutMinutes": 240,
     "AutoDropRecoveryDb": false
   }
   ```

2. **Create `PitrRequest` DTO**: Create in `src/UPACIP.Service/Backup/Models/PitrRequest.cs`:
   - `DateTime TargetTimestampUtc` — the exact point in time to recover to. Must be within the WAL retention window.
   - `string PerformedBy` — admin user initiating the recovery (for audit trail).
   - `bool DryRun` (default: `false`) — if true, perform only pre-flight validation without actual recovery.
   - `bool ValidateIntegrity` (default: `true`) — whether to run post-recovery row count and checksum validation.

3. **Create `PitrResult` DTO**: Create in `src/UPACIP.Service/Backup/Models/PitrResult.cs`:
   - `bool Success` — true if recovery completed and integrity validated.
   - `DateTime TargetTimestampUtc` — requested recovery point.
   - `DateTime ActualRecoveryPointUtc` — actual point achieved (may differ slightly from target per WAL granularity).
   - `string BaseBackupUsed` — filename of the base backup that was restored.
   - `int WalSegmentsReplayed` — count of WAL segments applied during recovery.
   - `TimeSpan RecoveryDuration` — total elapsed time for the recovery pipeline.
   - `bool IntegrityValidationPassed` — true if row counts and checksums match.
   - `List<TableRowCountComparison> RowCountResults` — per-table row count comparison (reuse type from US_089 task_003).
   - `List<TableChecksumComparison> ChecksumResults` — per-table checksum comparison.
   - `string? FallbackReason` — if WAL corruption caused fallback to base backup only, explains the gap (edge case 1).
   - `string? ErrorMessage` — recovery-level error, null on success.

4. **Create `RecoveryLog` entity**: Create in `src/UPACIP.Service/Backup/Models/RecoveryLog.cs`:
   - `Guid Id` (PK).
   - `DateTime TargetTimestampUtc` — requested recovery point.
   - `DateTime ActualRecoveryPointUtc` — achieved recovery point.
   - `string BaseBackupUsed` — which base backup was restored.
   - `int WalSegmentsReplayed` — WAL segments applied.
   - `bool Success` — overall success.
   - `bool IntegrityPassed` — integrity validation result.
   - `TimeSpan RecoveryDuration` — total time.
   - `string? FallbackReason` — if WAL fallback occurred.
   - `string PerformedBy` — admin who triggered recovery.
   - `DateTime CreatedAtUtc` — when this record was created.
   Add `DbSet<RecoveryLog>` to `ApplicationDbContext` with an index on `CreatedAtUtc`.

5. **Implement `IPointInTimeRecoveryService` / `PointInTimeRecoveryService`**: Create in `src/UPACIP.Service/Backup/PointInTimeRecoveryService.cs` with constructor injection of `IOptionsMonitor<PitrOptions>`, `IOptionsMonitor<BackupOptions>`, `IOptionsMonitor<WalArchivalOptions>`, `IBackupEncryptionService`, `WalArchivalMonitoringService`, `ApplicationDbContext`, and `ILogger<PointInTimeRecoveryService>`.

   **Method `Task<PitrResult> ExecuteRecoveryAsync(PitrRequest request, CancellationToken ct)`**:

   **Phase 1 — Pre-flight validation:**
   - (a) Validate `TargetTimestampUtc` is in the past and within the WAL retention window (not older than `WalRetentionDays` from the current date).
   - (b) Call `WalArchivalMonitoringService.GetArchivalStatusAsync()` to check WAL health. If `CorruptSegments > 0` affecting the target period (edge case 1): log the gap, set `FallbackReason`, and adjust the achievable recovery point to the nearest base backup timestamp.
   - (c) Find the appropriate base backup: scan `BackupOptions.BackupDirectory` for `.dump.enc` (or `.dump`) files, select the most recent backup whose timestamp is before `TargetTimestampUtc`.
   - (d) Verify WAL segment continuity from the base backup timestamp to `TargetTimestampUtc` — use the gap detection data from `WalArchivalStatus`.
   - (e) If `DryRun = true`: return a `PitrResult` with feasibility assessment (which backup, how many WAL segments, estimated recovery time) without executing.

   **Phase 2 — Base backup restoration:**
   - (f) Decrypt the selected base backup: `IBackupEncryptionService.DecryptBackupAsync(encryptedPath, tempDecryptedPath, ct)`.
   - (g) Prepare the recovery data directory: ensure `RecoveryDataDirectory` is clean. Initialize a new PostgreSQL data directory using `pg_basebackup` or by restoring the custom-format dump via `pg_restore --create --clean` into a new database on the recovery port.
   - (h) For the custom-format `.dump` approach: create the recovery database (`CREATE DATABASE {RecoveryDatabaseName}`), then `pg_restore --host=localhost --port={RecoveryPort} --dbname={RecoveryDatabaseName} --no-owner --no-privileges {decryptedFilePath}`.

   **Phase 3 — WAL replay to target timestamp (edge case 2):**
   - (i) Configure WAL recovery on the recovery instance by writing a `recovery.signal` file and setting parameters in `postgresql.auto.conf`:
     ```
     restore_command = 'copy "D:\\Backups\\WAL\\%f" "%p"'
     recovery_target_time = '{TargetTimestampUtc:yyyy-MM-dd HH:mm:ss.ffffff+00}'
     recovery_target_action = 'pause'
     ```
     The `recovery_target_time` parameter instructs PostgreSQL to replay WAL records up to (but not past) the specified timestamp. When the target falls between two WAL checkpoints (edge case 2), PostgreSQL applies all individual WAL records up to the closest record before the target — this is native PostgreSQL behavior and requires no special handling.
   - (j) Start the recovery PostgreSQL instance on `RecoveryPort` using `pg_ctl start -D {RecoveryDataDirectory} -o "-p {RecoveryPort}"`.
   - (k) Monitor recovery progress by polling `pg_is_in_recovery()` on the recovery instance. Recovery is complete when PostgreSQL reaches the target time and pauses.
   - (l) Count WAL segments that were replayed (based on file access timestamps in the WAL archive directory during recovery).

   **Phase 4 — Post-recovery integrity verification (AC-3):**
   - (m) Run row count validation: query `pg_stat_user_tables` on both the production database and the recovery database. Compare row counts per table. For PITR to a past point, production may have more rows (new inserts since recovery point) — the validation confirms counts are consistent with the target timestamp, not an exact match with current production.
   - (n) Run checksum validation: compute per-table checksums on the recovery database using the same `md5(string_agg(...))` approach from US_089 task_003. Compare against a reference (if available from the base backup log) or validate internal consistency (no orphaned rows, no broken sequences).
   - (o) Run referential integrity check: verify all FK constraints are valid on the recovered database.

   **Phase 5 — Finalization:**
   - (p) Delete the temporary decrypted `.dump` file.
   - (q) If `AutoDropRecoveryDb = true`: stop the recovery instance and remove the recovery data directory. Otherwise, leave the recovery instance running on `RecoveryPort` for admin inspection.
   - (r) Persist a `RecoveryLog` entry with all results.
   - (s) Log summary: `Log.Information("PITR_COMPLETE: Target={Target}, Actual={Actual}, BaseBackup={Backup}, WalReplayed={Count}, Duration={Duration}, IntegrityPassed={Integrity}")`.

6. **Add PITR API endpoints to `BackupController`**: Extend the existing `BackupController` (from US_089 task_003):

   **POST `/api/admin/backup/pitr`** — Admin-only endpoint:
   - Accepts `PitrRequest` (target timestamp, dry run flag).
   - Returns `PitrResult` with full recovery details.
   - Long-running: return `202 Accepted` with a recovery ID for polling on large databases.

   **GET `/api/admin/backup/pitr/feasibility?targetTimestamp={utc}`** — Admin-only endpoint:
   - Returns a lightweight feasibility check: which base backup would be used, WAL segment availability for the period, estimated recovery time, any detected gaps.
   - Calls `ExecuteRecoveryAsync` with `DryRun = true`.

   **GET `/api/admin/backup/pitr/history`** — Admin-only endpoint:
   - Returns paginated `RecoveryLog` entries for audit.

7. **Secure the recovery process**:
   - API endpoints require admin role authorization — only system administrators can trigger PITR.
   - Database credentials for the recovery instance are provided via environment variables (never in `appsettings.json` or logs).
   - The temporary decrypted backup file is deleted immediately after restoration (even on failure).
   - Recovery never runs against the production database — always uses a separate `RecoveryDatabaseName` on a separate `RecoveryPort`.
   - `PGPASSWORD` is set as a process environment variable for all `pg_restore`, `pg_ctl`, and `psql` child processes.

8. **Register services and bind configuration**: In `Program.cs`: bind `PitrOptions` via `builder.Services.Configure<PitrOptions>(builder.Configuration.GetSection("PitrRecovery"))`, register `services.AddScoped<IPointInTimeRecoveryService, PointInTimeRecoveryService>()`. Add `DbSet<RecoveryLog>` to `ApplicationDbContext`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── BackupController.cs                  ← from US_089 task_003 (extend)
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
│   │   │   ├── BackupRestorationTestService.cs      ← from US_089 task_003
│   │   │   ├── WalArchivalMonitoringService.cs      ← from US_090 task_001
│   │   │   └── Models/
│   │   │       ├── BackupOptions.cs
│   │   │       ├── BackupResult.cs
│   │   │       ├── BackupLog.cs
│   │   │       ├── BackupRetentionOptions.cs
│   │   │       ├── BackupFileInfo.cs
│   │   │       ├── EncryptionOptions.cs
│   │   │       ├── ReplicationOptions.cs
│   │   │       ├── ReplicationResult.cs
│   │   │       ├── RestorationTestOptions.cs
│   │   │       ├── RestorationTestResult.cs
│   │   │       ├── RestorationTestLog.cs
│   │   │       ├── WalArchivalOptions.cs            ← from US_090 task_001
│   │   │       └── WalArchivalStatus.cs             ← from US_090 task_001
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
│   └── Setup-WalArchiving.ps1                       ← from US_090 task_001
```

> Assumes US_088 (backup orchestration/retention), US_089 (encryption/replication/restoration testing), US_090 task_001 (WAL archival), and US_003 (PostgreSQL infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/PointInTimeRecoveryService.cs | IPointInTimeRecoveryService: 5-phase PITR pipeline (pre-flight → restore → WAL replay → validate → finalize) |
| CREATE | src/UPACIP.Service/Backup/Models/PitrOptions.cs | Config: recovery database, pg tool paths, recovery port, timeout |
| CREATE | src/UPACIP.Service/Backup/Models/PitrRequest.cs | DTO: target timestamp, admin identity, dry run flag |
| CREATE | src/UPACIP.Service/Backup/Models/PitrResult.cs | DTO: recovery outcome with WAL coverage, integrity validation, timing |
| CREATE | src/UPACIP.Service/Backup/Models/RecoveryLog.cs | Entity: PITR audit trail with all recovery details |
| MODIFY | src/UPACIP.Api/Controllers/BackupController.cs | Add POST pitr, GET pitr/feasibility, GET pitr/history endpoints |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<RecoveryLog> |
| MODIFY | src/UPACIP.Api/Program.cs | Bind PitrOptions, register IPointInTimeRecoveryService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add PitrRecovery configuration section |

## External References

- [Continuous Archiving and PITR — PostgreSQL 16](https://www.postgresql.org/docs/16/continuous-archiving.html)
- [Recovery Target Settings — PostgreSQL 16](https://www.postgresql.org/docs/16/runtime-config-wal.html#RUNTIME-CONFIG-WAL-RECOVERY-TARGET)
- [pg_restore — PostgreSQL 16](https://www.postgresql.org/docs/16/app-pgrestore.html)
- [pg_ctl — PostgreSQL 16](https://www.postgresql.org/docs/16/app-pg-ctl.html)
- [recovery.signal — PostgreSQL 16](https://www.postgresql.org/docs/16/runtime-config-wal.html#RUNTIME-CONFIG-WAL-ARCHIVE-RECOVERY)
- [pg_is_in_recovery — PostgreSQL 16](https://www.postgresql.org/docs/16/functions-admin.html#FUNCTIONS-RECOVERY-INFO-TABLE)

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
- [ ] PITR restores database to a specific past timestamp within the WAL retention window (AC-2)
- [ ] Row count validation runs against recovered database and reports per-table results (AC-3)
- [ ] Checksum validation runs against recovered database (AC-3)
- [ ] Referential integrity validation detects zero FK violations in recovered database (AC-3)
- [ ] Corrupted WAL segments trigger fallback to nearest base backup with gap logging (edge case 1)
- [ ] Target timestamp between WAL checkpoints correctly replays to closest prior record (edge case 2)
- [ ] Recovery uses separate database and port — never modifies production (security)
- [ ] Dry run returns feasibility assessment without executing recovery
- [ ] RecoveryLog records all PITR attempts with admin identity and validation outcomes
- [ ] API endpoints require admin role authorization
- [ ] Recovery completes within 4-hour RTO target (AC-4, NFR-025)

## Implementation Checklist

- [ ] Create `PitrOptions` with recovery database, pg tool paths, recovery port, timeout
- [ ] Create `PitrRequest` and `PitrResult` DTOs for recovery request/response
- [ ] Create `RecoveryLog` entity and add DbSet to ApplicationDbContext
- [ ] Implement pre-flight validation (WAL availability, base backup selection, gap detection)
- [ ] Implement base backup restoration with decryption and WAL replay via recovery_target_time
- [ ] Implement post-recovery integrity verification (row counts, checksums, FK validation)
- [ ] Add PITR, feasibility, and history endpoints to BackupController
- [ ] Register PitrOptions and IPointInTimeRecoveryService in Program.cs
