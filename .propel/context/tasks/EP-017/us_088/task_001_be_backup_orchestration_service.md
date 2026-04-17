# Task - task_001_be_backup_orchestration_service

## Requirement Reference

- User Story: us_088
- Story Location: .propel/context/tasks/EP-017/us_088/us_088.md
- Acceptance Criteria:
  - AC-1: Given the backup schedule is configured, When 2 AM local time arrives daily, Then an automated full database backup is executed and stored in a designated backup directory.
  - AC-3: Given a backup completes, When the result is logged, Then the system records backup size, duration, completion status, and checksum.
  - AC-4: Given a backup fails, When the failure is detected, Then the system retries once after 15 minutes and sends an alert to administrators if the retry also fails.
- Edge Case:
  - What happens when the backup storage is full? System sends a critical alert before running out of space (80% threshold) and skips the backup with an error log.
  - How does the system handle backups during high-load periods? Backups run at 2 AM (low-traffic) using a read replica or snapshot to minimize production impact.

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
| Database | PostgreSQL (pg_dump) | 16.x |
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

Implement a `DatabaseBackupService` BackgroundService that orchestrates automated daily PostgreSQL backups at 2 AM local time using `pg_dump`, with structured metadata logging, single-retry failure handling, disk space pre-check, and administrator alerting (DR-022). The service runs as part of the .NET backend Windows Service (TR-016) and invokes `pg_dump` via `System.Diagnostics.Process` to create a full custom-format backup (`.dump`) in a designated backup directory. Each backup is identified by a timestamped filename (`upacip_backup_YYYYMMDD_HHMMSS.dump`). Upon completion, the service computes the SHA-256 checksum of the output file, measures the backup duration, records the file size, and logs all metadata to a structured Serilog event and a `BackupLog` database table for audit and monitoring (AC-3). If the backup process fails (non-zero exit code or exception), the service waits 15 minutes and retries once (AC-4). If the retry also fails, the service sends a critical alert via the existing notification infrastructure (Serilog structured alert for Seq monitoring, plus an admin notification). Before each backup attempt, the service checks available disk space on the backup volume — if usage exceeds 80%, the backup is skipped and a critical storage alert is emitted (edge case 1). The `pg_dump` invocation uses `--no-owner --no-privileges` flags and connects via the configured PostgreSQL connection string. The 2 AM schedule is timezone-aware using the server's local time (edge case 2 — 2 AM is inherently low-traffic; no read replica needed for Phase 1 single-server architecture).

## Dependent Tasks

- US_003 — Requires PostgreSQL database infrastructure with connection string configured.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/DatabaseBackupService.cs` — BackgroundService: daily 2 AM pg_dump orchestration with retry and alerting
- **NEW** `src/UPACIP.Service/Backup/Models/BackupOptions.cs` — Configuration: pg_dump path, backup directory, schedule, retry delay, disk threshold
- **NEW** `src/UPACIP.Service/Backup/Models/BackupLog.cs` — Entity: backup metadata (filename, size, duration, status, checksum, timestamp)
- **NEW** `src/UPACIP.Service/Backup/Models/BackupResult.cs` — DTO: Success, FilePath, FileSize, Duration, Checksum, ErrorMessage
- **NEW** `src/UPACIP.Service/Backup/BackupExecutor.cs` — IBackupExecutor: pg_dump process invocation, checksum computation
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<BackupLog>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind BackupOptions, register DatabaseBackupService and IBackupExecutor
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add DatabaseBackup configuration section

## Implementation Plan

1. **Create `BackupOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/BackupOptions.cs`:
   - `string PgDumpPath` (default: `"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_dump.exe"` — standard Windows PostgreSQL installation path).
   - `string BackupDirectory` (default: `"D:\\Backups\\Database"` — designated backup directory on a separate volume from the database).
   - `string ScheduleLocalTime` (default: `"02:00"` — 2 AM local time per DR-022).
   - `int RetryDelayMinutes` (default: 15, per AC-4).
   - `int MaxRetries` (default: 1, per AC-4 — "retries once").
   - `double DiskSpaceThresholdPercent` (default: 80.0 — skip backup and alert when usage exceeds this, per edge case 1).
   - `string DatabaseName` (default: `"upacip"` — extracted from connection string or configured explicitly).
   - `string BackupFormat` (default: `"custom"` — pg_dump custom format for compression and selective restore).
   Register via `IOptionsMonitor<BackupOptions>` for runtime configuration changes. Add to `appsettings.json`:
   ```json
   "DatabaseBackup": {
     "PgDumpPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_dump.exe",
     "BackupDirectory": "D:\\Backups\\Database",
     "ScheduleLocalTime": "02:00",
     "RetryDelayMinutes": 15,
     "MaxRetries": 1,
     "DiskSpaceThresholdPercent": 80.0,
     "DatabaseName": "upacip",
     "BackupFormat": "custom"
   }
   ```

2. **Create `BackupResult` DTO**: Create in `src/UPACIP.Service/Backup/Models/BackupResult.cs`:
   - `bool Success` — true if pg_dump completed with exit code 0.
   - `string FilePath` — full path to the backup file.
   - `string FileName` — backup filename (e.g., `upacip_backup_20260417_020000.dump`).
   - `long FileSizeBytes` — backup file size in bytes.
   - `TimeSpan Duration` — elapsed time for the pg_dump execution.
   - `string Checksum` — SHA-256 hex string of the backup file.
   - `string? ErrorMessage` — pg_dump stderr output on failure, null on success.
   - `bool IsRetry` — true if this was a retry attempt.
   - `DateTime CompletedAtUtc` — when the backup completed.

3. **Create `BackupLog` entity (AC-3)**: Create in `src/UPACIP.Service/Backup/Models/BackupLog.cs`:
   - `Guid Id` (PK).
   - `string FileName` — backup filename.
   - `long FileSizeBytes` — backup file size.
   - `TimeSpan Duration` — backup execution time.
   - `string Status` — "Completed", "Failed", "Skipped".
   - `string Checksum` — SHA-256 of the backup file (null for failed/skipped).
   - `string? ErrorMessage` — failure reason (null for successful).
   - `bool WasRetry` — whether this was a retry attempt.
   - `DateTime CreatedAtUtc` — when this log entry was created.
   Add `DbSet<BackupLog>` to `ApplicationDbContext`. Create a simple EF Core configuration with an index on `CreatedAtUtc` for chronological queries.

4. **Implement `IBackupExecutor` / `BackupExecutor`**: Create in `src/UPACIP.Service/Backup/BackupExecutor.cs` with constructor injection of `IOptionsMonitor<BackupOptions>` and `ILogger<BackupExecutor>`. This service encapsulates the pg_dump process invocation and checksum computation.

   Method `Task<BackupResult> ExecuteBackupAsync(CancellationToken ct)`:
   - (a) Generate filename: `upacip_backup_{DateTime.Now:yyyyMMdd_HHmmss}.dump`.
   - (b) Build full output path: `Path.Combine(options.BackupDirectory, fileName)`.
   - (c) Ensure backup directory exists: `Directory.CreateDirectory(options.BackupDirectory)`.
   - (d) Extract connection parameters from configuration (host, port, username, password from the PostgreSQL connection string). Set `PGPASSWORD` environment variable for the process (avoid password in command-line arguments — security best practice).
   - (e) Build `pg_dump` arguments: `--host={host} --port={port} --username={username} --format={options.BackupFormat} --no-owner --no-privileges --file="{outputPath}" {options.DatabaseName}`.
   - (f) Start `Process` with `UseShellExecute = false`, `RedirectStandardError = true`, `CreateNoWindow = true`. Capture stderr for error reporting.
   - (g) Wait for process exit with the cancellation token. Measure elapsed time via `Stopwatch`.
   - (h) If exit code is 0: compute SHA-256 checksum of the output file using `SHA256.HashData(File.ReadAllBytes(outputPath))` and return `BackupResult { Success = true, ... }`.
   - (i) If exit code is non-zero: read stderr, return `BackupResult { Success = false, ErrorMessage = stderr }`.
   - (j) Clear `PGPASSWORD` from the process environment after completion (security hygiene).

5. **Implement disk space pre-check (edge case 1)**: Add a private method `CheckDiskSpaceAsync()` to `DatabaseBackupService`:
   - (a) Resolve the drive from `BackupDirectory` using `new DriveInfo(Path.GetPathRoot(options.BackupDirectory))`.
   - (b) Compute usage: `usagePercent = (1 - (drive.AvailableFreeSpace / (double)drive.TotalSize)) * 100`.
   - (c) If `usagePercent >= options.DiskSpaceThresholdPercent`: log `Log.Error("BACKUP_STORAGE_CRITICAL: UsagePercent={Usage}%, Threshold={Threshold}%, Drive={Drive}", ...)`. Persist a `BackupLog` entry with `Status = "Skipped"` and `ErrorMessage = "Backup storage at {usage}% capacity"`. Return false to skip the backup.
   - (d) If usage is above 70% but below threshold: log a warning for proactive monitoring.
   - (e) Return true to proceed with backup.

6. **Implement `DatabaseBackupService` BackgroundService (AC-1, AC-4)**: Create in `src/UPACIP.Service/Backup/DatabaseBackupService.cs` as a `BackgroundService`.

   **Schedule loop**:
   - Parse `ScheduleLocalTime` (e.g., "02:00") to a `TimeOnly`.
   - On each iteration: compute next execution as today (or tomorrow if past schedule time) at the configured time in local timezone.
   - `await Task.Delay(nextRun - DateTime.Now, stoppingToken)`.

   **Backup execution flow** (per cycle):
   - (a) Check disk space via `CheckDiskSpaceAsync()`. If false, skip and continue to next cycle.
   - (b) Execute `IBackupExecutor.ExecuteBackupAsync(ct)`.
   - (c) If successful: persist `BackupLog` with `Status = "Completed"`, log structured metadata:
     ```
     Log.Information(
       "BACKUP_COMPLETED: FileName={FileName}, Size={SizeBytes}, " +
       "Duration={Duration}, Checksum={Checksum}",
       result.FileName, result.FileSizeBytes, result.Duration, result.Checksum);
     ```
   - (d) If failed (AC-4): log the error, wait `RetryDelayMinutes` (15 min), then retry once:
     ```
     Log.Warning("BACKUP_FAILED: Attempt=1, Error={Error}. Retrying in {Delay} minutes.",
       result.ErrorMessage, options.RetryDelayMinutes);
     await Task.Delay(TimeSpan.FromMinutes(options.RetryDelayMinutes), stoppingToken);
     var retryResult = await backupExecutor.ExecuteBackupAsync(ct);
     ```
   - (e) If retry succeeds: persist `BackupLog` with `WasRetry = true`, `Status = "Completed"`.
   - (f) If retry also fails: persist `BackupLog` with `Status = "Failed"`, `WasRetry = true`. Emit critical alert:
     ```
     Log.Error(
       "BACKUP_CRITICAL_FAILURE: Both attempts failed. " +
       "FirstError={FirstError}, RetryError={RetryError}. " +
       "Administrator action required.",
       firstResult.ErrorMessage, retryResult.ErrorMessage);
     ```
     The Serilog Seq sink captures this as a critical event for administrator dashboards (existing Seq monitoring from US_083).

7. **Secure pg_dump credential handling**: The PostgreSQL password is extracted from the existing connection string in configuration (not stored separately). The `BackupExecutor` sets `PGPASSWORD` as a process-level environment variable for the `pg_dump` child process only — it is never logged, never included in command-line arguments (visible in process lists), and cleared after the process completes. The connection string in `appsettings.json` is already protected by environment-specific configuration (Development vs. Production).

8. **Register services and bind configuration**: In `Program.cs`: bind `BackupOptions` via `builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection("DatabaseBackup"))`, register `services.AddSingleton<IBackupExecutor, BackupExecutor>()`, and register `services.AddHostedService<DatabaseBackupService>()`. Add `DbSet<BackupLog>` to `ApplicationDbContext`.

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
│   │   ├── Monitoring/
│   │   │   └── UptimeMonitoringService.cs           ← from US_083 (BackgroundService pattern)
│   │   ├── Retention/
│   │   │   └── DataRetentionService.cs              ← from US_086 (nightly job pattern)
│   │   └── Caching/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   ├── Services/
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_003 (PostgreSQL infrastructure) is completed. pg_dump is available at the configured path on the Windows Server 2022 deployment.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/DatabaseBackupService.cs | BackgroundService: daily 2 AM orchestration with retry and alerting |
| CREATE | src/UPACIP.Service/Backup/BackupExecutor.cs | IBackupExecutor: pg_dump process invocation, SHA-256 checksum |
| CREATE | src/UPACIP.Service/Backup/Models/BackupOptions.cs | Config: pg_dump path, backup dir, schedule, retry, disk threshold |
| CREATE | src/UPACIP.Service/Backup/Models/BackupResult.cs | DTO: Success, FilePath, FileSize, Duration, Checksum, ErrorMessage |
| CREATE | src/UPACIP.Service/Backup/Models/BackupLog.cs | Entity: backup metadata for audit trail |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<BackupLog> |
| MODIFY | src/UPACIP.Api/Program.cs | Bind BackupOptions, register DatabaseBackupService and IBackupExecutor |
| MODIFY | src/UPACIP.Api/appsettings.json | Add DatabaseBackup configuration section |

## External References

- [pg_dump — PostgreSQL 16 Documentation](https://www.postgresql.org/docs/16/app-pgdump.html)
- [System.Diagnostics.Process — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process)
- [SHA256 — .NET Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)
- [DriveInfo — Disk Space Monitoring](https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo)
- [BackgroundService — .NET Hosted Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)

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
- [ ] Backup executes at configured schedule time and produces a `.dump` file in the backup directory (AC-1)
- [ ] Backup filename follows `upacip_backup_YYYYMMDD_HHMMSS.dump` naming convention
- [ ] BackupLog record contains filename, file size, duration, status, and SHA-256 checksum (AC-3)
- [ ] Structured Serilog event emitted with backup metadata on completion (AC-3)
- [ ] Failed backup is retried once after 15 minutes (AC-4)
- [ ] Double failure emits critical alert via Serilog (AC-4)
- [ ] Backup is skipped with error log when disk usage exceeds 80% threshold (edge case 1)
- [ ] Disk space warning emitted when usage exceeds 70%
- [ ] PostgreSQL password is never visible in command-line arguments or logs
- [ ] pg_dump runs with `--no-owner --no-privileges` flags

## Implementation Checklist

- [ ] Create `BackupOptions` with pg_dump path, backup directory, schedule, retry delay, disk threshold
- [ ] Create `BackupResult` DTO with success, file path, size, duration, checksum, error
- [ ] Create `BackupLog` entity with metadata fields and add DbSet to ApplicationDbContext
- [ ] Implement `IBackupExecutor` with pg_dump Process invocation and SHA-256 checksum computation
- [ ] Implement disk space pre-check with 80% threshold and critical alert
- [ ] Implement `DatabaseBackupService` BackgroundService with 2 AM schedule and retry logic
- [ ] Secure credential handling via PGPASSWORD environment variable (not command-line)
- [ ] Register BackupOptions, IBackupExecutor, and DatabaseBackupService in Program.cs
