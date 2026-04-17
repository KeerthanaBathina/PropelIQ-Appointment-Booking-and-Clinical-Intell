# Task - task_001_be_wal_archival_service

## Requirement Reference

- User Story: us_090
- Story Location: .propel/context/tasks/EP-017/us_090/us_090.md
- Acceptance Criteria:
  - AC-1: Given the PostgreSQL WAL (Write-Ahead Log) is configured, When transaction logs are archived, Then they are backed up every 15 minutes to enable point-in-time recovery.
  - AC-4: Given recovery targets are defined, When a disaster occurs, Then the system achieves RPO of 1 hour and RTO of 4 hours per the design specifications.
- Edge Case:
  - What happens when transaction logs are corrupted? System falls back to the most recent complete backup and logs the gap period as "unrecoverable."

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
| Database | PostgreSQL (WAL archiving) | 16.x |
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

Implement PostgreSQL WAL (Write-Ahead Log) archival configuration and a `WalArchivalMonitoringService` that ensures continuous WAL segment archiving every 15 minutes to enable point-in-time recovery (DR-027, NFR-024). PostgreSQL's native WAL archiving mechanism (`archive_mode` and `archive_command`) handles the actual WAL segment copying — this task configures the PostgreSQL server for continuous archiving via a PowerShell setup script, creates the WAL archive directory structure, and builds a .NET BackgroundService that monitors the WAL archival pipeline health. The monitoring service checks that new WAL segments are arriving in the archive directory at the expected cadence (at least every 15 minutes), detects gaps in the WAL sequence, validates WAL segment integrity via `pg_waldump`, and emits structured Serilog alerts when archiving stalls or corruption is detected (edge case 1). The service also enforces a WAL retention policy aligned with the backup retention window, cleaning up WAL segments older than the daily backup retention period (30 days from US_088 task_002). A `pg_switch_wal()` call is scheduled every 15 minutes to force WAL segment completion even during low-activity periods, guaranteeing the 15-minute RPO cadence required by AC-1. This ensures the RPO ≤ 1 hour target (NFR-024) is exceeded — with 15-minute WAL archives, the actual RPO is ≤ 15 minutes (AC-4).

## Dependent Tasks

- US_088 task_001_be_backup_orchestration_service — Requires BackupOptions (for pg tool paths, backup directory), DatabaseBackupService pipeline.
- US_003 — Requires PostgreSQL database infrastructure with superuser access for WAL configuration.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/WalArchivalMonitoringService.cs` — BackgroundService: WAL archival health monitoring, pg_switch_wal scheduling, gap detection
- **NEW** `src/UPACIP.Service/Backup/Models/WalArchivalOptions.cs` — Configuration: WAL archive directory, monitoring interval, retention
- **NEW** `src/UPACIP.Service/Backup/Models/WalArchivalStatus.cs` — DTO: archival health status, last segment, gap detection results
- **NEW** `scripts/Setup-WalArchiving.ps1` — PowerShell: configure postgresql.conf for continuous archiving
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind WalArchivalOptions, register WalArchivalMonitoringService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add WalArchival configuration section

## Implementation Plan

1. **Create `WalArchivalOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/WalArchivalOptions.cs`:
   - `string WalArchiveDirectory` (default: `"D:\\Backups\\WAL"`) — directory where archived WAL segments are stored. Separate from the daily backup directory for organizational clarity.
   - `int SwitchWalIntervalMinutes` (default: 15, per AC-1 — "every 15 minutes"). Controls the `pg_switch_wal()` scheduling cadence.
   - `int MonitoringCheckIntervalMinutes` (default: 5) — how frequently the monitoring service checks WAL archival health.
   - `int MaxWalArchivalDelayMinutes` (default: 20) — threshold before alerting that WAL archiving has stalled (slightly above the 15-minute interval to allow for processing time).
   - `int WalRetentionDays` (default: 30) — WAL segments older than this are cleaned up (aligned with daily backup retention from US_088).
   - `string PsqlPath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\psql.exe"`) — path to psql for `pg_switch_wal()` calls.
   - `string PgWaldumpPath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_waldump.exe"`) — path to pg_waldump for WAL segment validation.
   Register via `IOptionsMonitor<WalArchivalOptions>`. Add to `appsettings.json`:
   ```json
   "WalArchival": {
     "WalArchiveDirectory": "D:\\Backups\\WAL",
     "SwitchWalIntervalMinutes": 15,
     "MonitoringCheckIntervalMinutes": 5,
     "MaxWalArchivalDelayMinutes": 20,
     "WalRetentionDays": 30,
     "PsqlPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\psql.exe",
     "PgWaldumpPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_waldump.exe"
   }
   ```

2. **Create PostgreSQL WAL archiving setup script**: Create `scripts/Setup-WalArchiving.ps1` — a PowerShell script that configures PostgreSQL 16 for continuous WAL archiving. This is a one-time deployment setup script, not part of the runtime application.

   The script performs:
   - (a) Verify PostgreSQL data directory and `postgresql.conf` location.
   - (b) Set `wal_level = replica` (required for WAL archiving; default in PostgreSQL 16 but explicitly set for clarity).
   - (c) Set `archive_mode = on` — enables WAL archiving.
   - (d) Set `archive_command = 'copy "%p" "D:\\Backups\\WAL\\%f"'` — copies each completed WAL segment to the archive directory. Uses Windows `copy` command for the Windows Server 2022 deployment.
   - (e) Set `archive_timeout = 900` (900 seconds = 15 minutes) — forces a WAL segment switch even if the segment is not full, ensuring at most 15-minute gaps between archived segments. This is a safety net alongside the application-level `pg_switch_wal()` calls.
   - (f) Create the WAL archive directory: `New-Item -ItemType Directory -Force -Path "D:\Backups\WAL"`.
   - (g) Set appropriate NTFS permissions on the archive directory for the PostgreSQL service account.
   - (h) Restart the PostgreSQL service to apply configuration changes: `Restart-Service postgresql-x64-16`.
   - (i) Verify archiving is active: `SELECT * FROM pg_stat_archiver;`.
   - (j) Log all actions for audit trail.

   The script is idempotent — re-running it on an already-configured server detects existing settings and only applies changes if needed.

3. **Implement `pg_switch_wal()` scheduling**: Within `WalArchivalMonitoringService`, implement a timer-based loop that calls `pg_switch_wal()` every 15 minutes:
   - Use `psql -c "SELECT pg_switch_wal();"` via `System.Diagnostics.Process` (same pattern as `BackupExecutor` from US_088).
   - `PGPASSWORD` is set as a process environment variable (never in CLI args).
   - This forces the current WAL segment to be completed and archived, even during low-activity periods (e.g., overnight, weekends). Without this, a quiet database might not fill a 16 MB WAL segment for hours, violating the 15-minute RPO guarantee.
   - Log each switch: `Log.Debug("WAL_SWITCH_EXECUTED: Segment={Segment}")`.
   - If `pg_switch_wal()` fails, log a warning and retry on the next cycle — do not crash the service.

4. **Implement WAL archival health monitoring**: Within `WalArchivalMonitoringService`, implement a monitoring loop (every `MonitoringCheckIntervalMinutes`):

   - (a) **Check latest archived WAL**: Scan `WalArchiveDirectory` for the most recently modified file. If the newest file is older than `MaxWalArchivalDelayMinutes`, emit a critical alert:
     ```
     Log.Error("WAL_ARCHIVAL_STALLED: LastArchivedAt={LastModified}, " +
       "MinutesSinceLastArchive={Minutes}, Threshold={Threshold}min");
     ```
   - (b) **Detect WAL segment gaps**: List all WAL segment files in the archive, parse their timeline and segment numbers (format: `TTTTTTTT00000000SSSSSSSS`), sort them, and check for missing segments in the sequence. If gaps are found:
     ```
     Log.Warning("WAL_SEGMENT_GAP: Missing segments between {Start} and {End}, " +
       "GapCount={Count}. PITR may not cover this period.");
     ```
   - (c) **Query `pg_stat_archiver`**: Execute `psql -c "SELECT last_archived_wal, last_archived_time, failed_count, last_failed_wal, last_failed_time FROM pg_stat_archiver;"` to get PostgreSQL's own archival status. If `failed_count > 0` since last check, log the failure details.
   - (d) **Validate WAL integrity (edge case 1)**: For the most recent WAL segments, run `pg_waldump` to verify they are parseable:
     ```
     pg_waldump {segment_file} --quiet
     ```
     If pg_waldump returns a non-zero exit code, the segment is corrupted. Log:
     ```
     Log.Error("WAL_SEGMENT_CORRUPT: File={FileName}. " +
       "PITR will fall back to most recent complete backup. " +
       "Unrecoverable gap: {GapStart} to {GapEnd}");
     ```

5. **Implement WAL retention cleanup**: Add a daily cleanup step (runs once per day, e.g., after the 2 AM backup cycle):
   - Scan `WalArchiveDirectory` for WAL segments older than `WalRetentionDays` (default 30 days).
   - Delete expired segments in chronological order.
   - Log a summary: `Log.Information("WAL_RETENTION_CLEANUP: Deleted={Count}, FreedBytes={Bytes}, OldestRetained={OldestDate}")`.
   - Ensure at least one full set of WAL segments is retained that covers the gap from the oldest retained daily backup to the present — never delete WAL segments that are needed for PITR from a retained backup.

6. **Create `WalArchivalStatus` DTO**: Create in `src/UPACIP.Service/Backup/Models/WalArchivalStatus.cs`:
   - `bool IsHealthy` — true if archiving is active and no gaps/corruption detected.
   - `string? LastArchivedSegment` — filename of the most recently archived WAL segment.
   - `DateTime? LastArchivedAtUtc` — timestamp of the last archived segment.
   - `int MinutesSinceLastArchive` — minutes since the last archived segment.
   - `int TotalArchivedSegments` — count of WAL segments in the archive directory.
   - `int DetectedGaps` — number of sequence gaps found.
   - `int CorruptSegments` — number of segments that failed `pg_waldump` validation.
   - `long ArchiveDirectorySizeBytes` — total size of the WAL archive.
   This is used by the PITR service (task_002) to pre-validate WAL availability before attempting recovery.

7. **Expose WAL status via health check**: Add a method `Task<WalArchivalStatus> GetArchivalStatusAsync()` to the monitoring service, accessible by the PITR recovery service (task_002) and the existing health check infrastructure (from US_083). If WAL archiving is unhealthy, the health check reports a degraded status.

8. **Register services and bind configuration**: In `Program.cs`: bind `WalArchivalOptions` via `builder.Services.Configure<WalArchivalOptions>(builder.Configuration.GetSection("WalArchival"))`, register `services.AddHostedService<WalArchivalMonitoringService>()`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── BackupController.cs                  ← from US_089 task_003
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
│   │   │       └── RestorationTestLog.cs
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

> Assumes US_088 (backup orchestration/retention) and US_003 (PostgreSQL infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/WalArchivalMonitoringService.cs | BackgroundService: pg_switch_wal scheduling, archival health monitoring, gap/corruption detection |
| CREATE | src/UPACIP.Service/Backup/Models/WalArchivalOptions.cs | Config: WAL archive directory, switch interval, monitoring frequency, retention |
| CREATE | src/UPACIP.Service/Backup/Models/WalArchivalStatus.cs | DTO: archival health status, segment counts, gaps, corruption |
| CREATE | scripts/Setup-WalArchiving.ps1 | PowerShell: configure postgresql.conf for continuous WAL archiving |
| MODIFY | src/UPACIP.Api/Program.cs | Bind WalArchivalOptions, register WalArchivalMonitoringService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add WalArchival configuration section |

## External References

- [Continuous Archiving and PITR — PostgreSQL 16](https://www.postgresql.org/docs/16/continuous-archiving.html)
- [pg_switch_wal — PostgreSQL 16](https://www.postgresql.org/docs/16/functions-admin.html#FUNCTIONS-ADMIN-BACKUP)
- [pg_stat_archiver — PostgreSQL 16](https://www.postgresql.org/docs/16/monitoring-stats.html#MONITORING-PG-STAT-ARCHIVER-VIEW)
- [pg_waldump — PostgreSQL 16](https://www.postgresql.org/docs/16/pgwaldump.html)
- [WAL Configuration — PostgreSQL 16](https://www.postgresql.org/docs/16/runtime-config-wal.html)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Run WAL archiving setup (one-time, requires PostgreSQL superuser)
powershell -ExecutionPolicy Bypass -File scripts/Setup-WalArchiving.ps1
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] PostgreSQL `archive_mode = on` and `archive_command` configured correctly
- [ ] WAL segments appear in the archive directory after database writes
- [ ] `pg_switch_wal()` executes every 15 minutes, forcing segment completion (AC-1)
- [ ] Monitoring detects stalled archiving when no new segments arrive within 20 minutes
- [ ] WAL segment gaps are detected and logged with sequence details
- [ ] Corrupted WAL segments are detected via `pg_waldump` and flagged (edge case 1)
- [ ] WAL segments older than 30 days are cleaned up by retention
- [ ] `pg_stat_archiver` query reports archival status correctly
- [ ] RPO ≤ 15 minutes achieved via forced WAL switching (AC-4, NFR-024)

## Implementation Checklist

- [ ] Create `WalArchivalOptions` with archive directory, switch interval, monitoring frequency, retention
- [ ] Create `Setup-WalArchiving.ps1` PowerShell script for postgresql.conf configuration
- [ ] Implement `pg_switch_wal()` scheduled execution every 15 minutes
- [ ] Implement WAL archival health monitoring with gap and corruption detection
- [ ] Implement WAL retention cleanup aligned with backup retention window
- [ ] Create `WalArchivalStatus` DTO for health reporting
- [ ] Expose WAL archival status via health check integration
- [ ] Register WalArchivalOptions and WalArchivalMonitoringService in Program.cs
