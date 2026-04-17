# Task - task_002_be_backup_retention_management

## Requirement Reference

- User Story: us_088
- Story Location: .propel/context/tasks/EP-017/us_088/us_088.md
- Acceptance Criteria:
  - AC-2: Given backups are stored, When the retention policy is applied, Then daily backups are retained for 30 days, weekly backups for 90 days, and monthly backups for 1 year.
- Edge Case:
  - What happens when the backup storage is full? System sends a critical alert before running out of space (80% threshold) and skips the backup with an error log (handled by task_001; retention cleanup in this task reduces storage pressure).

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

Implement the tiered backup retention management service that enforces the three-tier backup retention policy: daily backups retained for 30 days, weekly backups (Sunday) retained for 90 days, and monthly backups (1st of month) retained for 1 year (DR-023). This service runs as part of the `DatabaseBackupService` nightly cycle (from task_001) after each successful backup. The retention logic classifies each backup file by tier based on its timestamp: a backup taken on the 1st of any month is promoted to the "monthly" tier, a backup taken on a Sunday is promoted to the "weekly" tier, and all other backups remain in the "daily" tier. Each tier has its own retention period. When the retention cleanup runs, it scans the backup directory, classifies each file, computes its age, and deletes files that have exceeded their tier's retention period. The system logs a structured summary of deleted files (count, freed bytes) and retains a record in the `BackupLog` table. The promotion model ensures that weekly and monthly backups are simply daily backups that are kept longer — no separate backup process is needed. This design means a backup from "Sunday, January 1st" qualifies for all three tiers and is retained for 1 year (the longest applicable period).

## Dependent Tasks

- US_088 task_001_be_backup_orchestration_service — Requires BackupOptions, BackupLog entity, backup directory, and DatabaseBackupService.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/BackupRetentionService.cs` — IBackupRetentionService: tiered retention classification and cleanup
- **NEW** `src/UPACIP.Service/Backup/Models/BackupRetentionOptions.cs` — Configuration: daily/weekly/monthly retention periods
- **NEW** `src/UPACIP.Service/Backup/Models/BackupFileInfo.cs` — DTO: parsed backup file with timestamp, tier classification, age
- **MODIFY** `src/UPACIP.Service/Backup/DatabaseBackupService.cs` — Call retention cleanup after successful backup
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add BackupRetention configuration section
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind BackupRetentionOptions, register IBackupRetentionService

## Implementation Plan

1. **Create `BackupRetentionOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/BackupRetentionOptions.cs`:
   - `int DailyRetentionDays` (default: 30, per AC-2). Daily backups older than 30 days are deleted unless promoted to weekly or monthly.
   - `int WeeklyRetentionDays` (default: 90, per AC-2). Backups taken on Sunday are retained for 90 days.
   - `int MonthlyRetentionDays` (default: 365, per AC-2 — "1 year"). Backups taken on the 1st of the month are retained for 1 year.
   - `DayOfWeek WeeklyBackupDay` (default: `DayOfWeek.Sunday`). Configurable day for weekly tier promotion.
   - `int MonthlyBackupDayOfMonth` (default: 1). Configurable day for monthly tier promotion.
   Register via `IOptionsMonitor<BackupRetentionOptions>`. Add to `appsettings.json`:
   ```json
   "BackupRetention": {
     "DailyRetentionDays": 30,
     "WeeklyRetentionDays": 90,
     "MonthlyRetentionDays": 365,
     "WeeklyBackupDay": "Sunday",
     "MonthlyBackupDayOfMonth": 1
   }
   ```

2. **Create `BackupFileInfo` DTO**: Create in `src/UPACIP.Service/Backup/Models/BackupFileInfo.cs`:
   - `string FileName` — e.g., `upacip_backup_20260417_020000.dump`.
   - `string FullPath` — absolute file path.
   - `DateTime BackupTimestamp` — parsed from filename.
   - `long FileSizeBytes` — file size.
   - `BackupTier Tier` — classified tier (Daily, Weekly, Monthly).
   - `int AgeDays` — age in days from current date.
   - `bool IsExpired` — true if age exceeds the tier's retention period.

   Create enum `BackupTier`: `Daily`, `Weekly`, `Monthly`.

3. **Implement `IBackupRetentionService` / `BackupRetentionService`**: Create in `src/UPACIP.Service/Backup/BackupRetentionService.cs` with constructor injection of `IOptionsMonitor<BackupRetentionOptions>`, `IOptionsMonitor<BackupOptions>` (for backup directory path), and `ILogger<BackupRetentionService>`.

   **Method `Task<RetentionCleanupResult> ApplyRetentionPolicyAsync(CancellationToken ct)`**:

   - (a) **Scan backup directory**: List all `.dump` files in the configured backup directory using `Directory.GetFiles(backupDir, "upacip_backup_*.dump")`.
   - (b) **Parse timestamps**: Extract `DateTime` from each filename using regex `upacip_backup_(\d{8}_\d{6})\.dump` → parse with `DateTime.ParseExact(match, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)`. Skip files that don't match the expected naming pattern (log a warning for unexpected files).
   - (c) **Classify tiers**: For each backup file, determine the highest applicable tier:
     - If `backupDate.Day == options.MonthlyBackupDayOfMonth` → `BackupTier.Monthly` (highest retention).
     - Else if `backupDate.DayOfWeek == options.WeeklyBackupDay` → `BackupTier.Weekly`.
     - Else → `BackupTier.Daily`.
     A file can only belong to one tier (the highest one), ensuring it gets the longest applicable retention period.
   - (d) **Compute expiration**: For each classified file:
     - `Daily`: expired if `ageDays > options.DailyRetentionDays`.
     - `Weekly`: expired if `ageDays > options.WeeklyRetentionDays`.
     - `Monthly`: expired if `ageDays > options.MonthlyRetentionDays`.
   - (e) **Delete expired files**: For each expired file, call `File.Delete(filePath)`. Accumulate: deleted count, freed bytes. Log each deletion: `Log.Information("BACKUP_RETENTION_DELETE: File={FileName}, Tier={Tier}, Age={AgeDays}d, Size={SizeBytes}", ...)`.
   - (f) **Summary logging**: After cleanup, log:
     ```
     Log.Information(
       "BACKUP_RETENTION_COMPLETE: Scanned={TotalFiles}, Deleted={DeletedCount}, " +
       "FreedBytes={FreedBytes}, Retained={RetainedCount} " +
       "(Daily={DailyCount}, Weekly={WeeklyCount}, Monthly={MonthlyCount})",
       ...);
     ```
   - (g) Return `RetentionCleanupResult` with counts and freed bytes.

4. **Define `RetentionCleanupResult`**: Inline in `BackupRetentionService.cs` or as a nested class:
   - `int TotalScanned` — total backup files found.
   - `int DeletedCount` — files deleted.
   - `long FreedBytes` — total bytes freed.
   - `int RetainedDaily` — daily-tier files retained.
   - `int RetainedWeekly` — weekly-tier files retained.
   - `int RetainedMonthly` — monthly-tier files retained.

5. **Handle edge cases in tier classification**:
   - **Sunday the 1st**: A backup on Sunday January 1st qualifies for both weekly and monthly tiers. The classification algorithm picks `Monthly` (highest retention = 365 days), which is the correct behavior — the file is retained for the longest applicable period.
   - **Leap year / short months**: The `MonthlyBackupDayOfMonth = 1` default avoids issues with months that have fewer than 28/29/30/31 days. If configured to day 31, months without a 31st simply have no monthly-tier backup that month — the weekly backup provides the next longest retention.
   - **Missing backups**: If the server was down on a Sunday or the 1st, there is no weekly/monthly backup for that period. The retention policy still applies to whatever files exist — no gap-filling is attempted. The daily backup closest to the missing date is retained for its 30-day window.

6. **Integrate retention cleanup into `DatabaseBackupService`**: Modify the `DatabaseBackupService` (from task_001) to call `IBackupRetentionService.ApplyRetentionPolicyAsync(ct)` after each successful backup:
   ```csharp
   // After successful backup (or successful retry):
   var retentionResult = await _retentionService.ApplyRetentionPolicyAsync(ct);
   Log.Information("BACKUP_CYCLE_COMPLETE: BackupFile={FileName}, RetentionDeleted={Deleted}",
     backupResult.FileName, retentionResult.DeletedCount);
   ```
   If retention cleanup fails (e.g., file permission error), log the error but do NOT fail the backup cycle — the backup itself was successful.

7. **Persist retention activity to BackupLog**: After retention cleanup, optionally persist a `BackupLog` entry with `Status = "RetentionCleanup"` and metadata about deleted files. This provides an audit trail of when files were deleted and how much storage was freed. Use the existing `BackupLog` entity from task_001 with a `Status` value distinguishing retention cleanup from actual backups.

8. **Register services and bind configuration**: In `Program.cs`: bind `BackupRetentionOptions` via `builder.Services.Configure<BackupRetentionOptions>(builder.Configuration.GetSection("BackupRetention"))`, register `services.AddSingleton<IBackupRetentionService, BackupRetentionService>()`. Add the `BackupRetention` section to `appsettings.json`.

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
│   │   │   ├── DatabaseBackupService.cs             ← from task_001
│   │   │   ├── BackupExecutor.cs                    ← from task_001
│   │   │   └── Models/
│   │   │       ├── BackupOptions.cs                 ← from task_001
│   │   │       ├── BackupResult.cs                  ← from task_001
│   │   │       └── BackupLog.cs                     ← from task_001
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

> Assumes task_001 (backup orchestration service) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/BackupRetentionService.cs | IBackupRetentionService: tier classification, expiration check, cleanup |
| CREATE | src/UPACIP.Service/Backup/Models/BackupRetentionOptions.cs | Config: daily 30d, weekly 90d, monthly 365d retention periods |
| CREATE | src/UPACIP.Service/Backup/Models/BackupFileInfo.cs | DTO: parsed backup file with tier, age, expiration status |
| MODIFY | src/UPACIP.Service/Backup/DatabaseBackupService.cs | Call retention cleanup after successful backup |
| MODIFY | src/UPACIP.Api/appsettings.json | Add BackupRetention configuration section |
| MODIFY | src/UPACIP.Api/Program.cs | Bind BackupRetentionOptions, register IBackupRetentionService |

## External References

- [Directory.GetFiles — .NET File Enumeration](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.getfiles)
- [DateTime.ParseExact — Date Parsing](https://learn.microsoft.com/en-us/dotnet/api/system.datetime.parseexact)
- [pg_dump Backup Strategies — PostgreSQL Wiki](https://wiki.postgresql.org/wiki/Automated_Backup_on_Windows)
- [IOptionsMonitor — Runtime Configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)

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
- [ ] Daily backups older than 30 days are deleted (unless promoted to weekly/monthly) (AC-2)
- [ ] Sunday backups are classified as weekly tier and retained for 90 days (AC-2)
- [ ] 1st-of-month backups are classified as monthly tier and retained for 1 year (AC-2)
- [ ] Backup on Sunday the 1st is classified as monthly (highest tier, longest retention)
- [ ] Retention cleanup logs structured summary with deleted count and freed bytes
- [ ] Files not matching `upacip_backup_*.dump` pattern are skipped with warning
- [ ] Retention cleanup failure does not fail the backup cycle
- [ ] BackupLog entry records retention cleanup activity
- [ ] Changing retention periods in appsettings takes effect on next backup cycle

## Implementation Checklist

- [ ] Create `BackupRetentionOptions` with daily/weekly/monthly retention periods
- [ ] Create `BackupFileInfo` DTO with tier classification and `BackupTier` enum
- [ ] Implement `IBackupRetentionService` with directory scan, tier classification, and expiration logic
- [ ] Handle tier promotion edge cases (Sunday 1st → monthly, missing backups)
- [ ] Implement expired file deletion with per-file and summary logging
- [ ] Integrate retention cleanup into DatabaseBackupService after successful backup
- [ ] Persist retention cleanup activity to BackupLog
- [ ] Register BackupRetentionOptions and IBackupRetentionService in Program.cs
