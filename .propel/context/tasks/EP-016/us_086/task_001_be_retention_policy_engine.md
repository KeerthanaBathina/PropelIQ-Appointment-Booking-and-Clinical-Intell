# Task - task_001_be_retention_policy_engine

## Requirement Reference

- User Story: us_086
- Story Location: .propel/context/tasks/EP-016/us_086/us_086.md
- Acceptance Criteria:
  - AC-1: Given audit log entries exist, When the retention policy is checked, Then audit logs are retained for a minimum of 7 years with no automatic deletion.
  - AC-2: Given clinical records (documents, extracted data, medical codes) exist, When the retention policy is checked, Then they are retained indefinitely without automatic archival.
  - AC-4: Given notification log entries exist, When they are older than 90 days, Then the system purges them in a nightly batch job with a summary count logged.
- Edge Case:
  - What happens when a retention policy change is applied retroactively? System applies the new policy starting from the next scheduled retention job; previously purged data cannot be recovered.
  - How does the system handle retention for data referenced by audit logs? Data referenced in audit logs is retained for the audit log's retention period (7 years) regardless of its own category policy.

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
| Caching | Upstash Redis | 7.x |

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

Implement the configurable data retention policy engine and the nightly notification log purge job per HIPAA retention requirements (DR-016 through DR-019, NFR-043). This task delivers three capabilities: (1) **Retention policy configuration model** — a strongly-typed `RetentionPolicyOptions` loaded from `appsettings.json` via `IOptionsMonitor<T>` with per-category retention periods (audit logs 7 years, clinical records indefinite, appointments 3 years, notifications 90 days, cancelled appointments 1 year), supporting runtime config changes that take effect on the next scheduled job cycle (edge case 1); (2) **Retention policy enforcement guards** — an `IRetentionPolicyGuard` service that prevents any automated or manual deletion of audit logs younger than 7 years (AC-1) and blocks deletion/archival of clinical records entirely (AC-2), and additionally enforces audit-log reference protection so that any entity referenced by an audit log entry is retained for the 7-year audit retention period regardless of its own category policy (edge case 2); (3) **Notification log purge job** — a `DataRetentionService` BackgroundService that runs nightly at a configurable time (default: 3:00 AM local time) to batch-delete notification log entries older than the configured 90-day threshold (AC-4), logging a structured summary with counts of purged records per notification type. The appointment and cancelled appointment archival (AC-3, AC-5) are handled in task_002.

## Dependent Tasks

- US_008 task_001_be_entity_models — Requires AuditLog, NotificationLog, ClinicalDocument, ExtractedData, MedicalCode, Appointment entities.
- US_008 task_002_be_efcore_configuration_migrations — Requires ApplicationDbContext with DbSets for all entities.
- US_064 — Requires immutable audit log system with AuditLog entity and append-only semantics.

## Impacted Components

- **NEW** `src/UPACIP.Service/Retention/Models/RetentionPolicyOptions.cs` — Configuration: per-category retention periods, nightly job schedule, batch size
- **NEW** `src/UPACIP.Service/Retention/RetentionPolicyGuard.cs` — IRetentionPolicyGuard: blocks premature deletion of audit logs and clinical records, enforces audit-log reference protection
- **NEW** `src/UPACIP.Service/Retention/DataRetentionService.cs` — BackgroundService: nightly notification log purge with batch processing and structured summary logging
- **NEW** `src/UPACIP.Service/Retention/Models/RetentionCategory.cs` — Enum: AuditLogs, ClinicalRecords, Appointments, Notifications, CancelledAppointments
- **NEW** `src/UPACIP.Service/Retention/Models/PurgeResult.cs` — Result DTO: Category, RecordsPurged, OldestPurgedDate, ExecutionDuration
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind RetentionPolicyOptions, register IRetentionPolicyGuard and DataRetentionService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add RetentionPolicy configuration section

## Implementation Plan

1. **Define `RetentionCategory` enum**: Create in `src/UPACIP.Service/Retention/Models/RetentionCategory.cs` with values: `AuditLogs`, `ClinicalRecords`, `Appointments`, `Notifications`, `CancelledAppointments`. This enum is used as the key for category-specific retention periods and for structured logging of purge operations.

2. **Create `RetentionPolicyOptions` configuration model (edge case 1)**: Create in `src/UPACIP.Service/Retention/Models/RetentionPolicyOptions.cs`:
   - `int AuditLogRetentionYears` (default: 7, per DR-016). Minimum value enforced: 7 — the setter clamps values below 7 to 7 and logs a warning, because HIPAA requires a minimum 7-year retention.
   - `bool ClinicalRecordsIndefiniteRetention` (default: true, per DR-017). When true, clinical records (ClinicalDocument, ExtractedData, MedicalCode) are never automatically deleted or archived.
   - `int AppointmentRetentionYears` (default: 3, per DR-018). Used by task_002 for archival threshold.
   - `int NotificationLogRetentionDays` (default: 90, per DR-019). Threshold for nightly purge.
   - `int CancelledAppointmentRetentionYears` (default: 1, per DR-020). Used by task_002 for archival threshold.
   - `string NightlyJobScheduleLocal` (default: `"03:00"` — 3:00 AM local time). Configurable execution time.
   - `int PurgeBatchSize` (default: 1000). Number of records deleted per batch to avoid long-running transactions.
   - `bool EnforceAuditLogReferenceProtection` (default: true, per edge case 2). When true, entities referenced by audit logs are retained for the audit log retention period.
   Register via `IOptionsMonitor<RetentionPolicyOptions>` (not `IOptions<T>`) so configuration changes in `appsettings.json` take effect on the next scheduled job cycle without application restart (edge case 1). Add to `appsettings.json`:
   ```json
   "RetentionPolicy": {
     "AuditLogRetentionYears": 7,
     "ClinicalRecordsIndefiniteRetention": true,
     "AppointmentRetentionYears": 3,
     "NotificationLogRetentionDays": 90,
     "CancelledAppointmentRetentionYears": 1,
     "NightlyJobScheduleLocal": "03:00",
     "PurgeBatchSize": 1000,
     "EnforceAuditLogReferenceProtection": true
   }
   ```

3. **Implement `IRetentionPolicyGuard` / `RetentionPolicyGuard` (AC-1, AC-2, edge case 2)**: Create in `src/UPACIP.Service/Retention/RetentionPolicyGuard.cs` with constructor injection of `IOptionsMonitor<RetentionPolicyOptions>` and `ApplicationDbContext`. Methods:

   - `bool CanDeleteAsync(RetentionCategory category, DateTime recordCreatedAt, CancellationToken ct)`:
     - `AuditLogs`: Always returns `false` if `recordCreatedAt > DateTime.UtcNow.AddYears(-options.AuditLogRetentionYears)`. Audit logs younger than 7 years cannot be deleted (AC-1). Audit logs older than 7 years still return `false` — per AC-1 "no automatic deletion" means audit logs are never auto-deleted; only a manual compliance officer action (out of scope for this task) can remove them.
     - `ClinicalRecords`: Always returns `false` when `ClinicalRecordsIndefiniteRetention` is true (AC-2). Clinical documents, extracted data, and medical codes are never automatically deleted or archived.
     - `Notifications`: Returns `true` if `recordCreatedAt < DateTime.UtcNow.AddDays(-options.NotificationLogRetentionDays)`.
     - `Appointments` / `CancelledAppointments`: Delegates to task_002's archival logic (returns threshold check result).

   - `bool IsProtectedByAuditLogAsync(string resourceType, Guid resourceId, CancellationToken ct)` (edge case 2):
     - Queries `ApplicationDbContext.AuditLogs.AnyAsync(a => a.ResourceType == resourceType && a.ResourceId == resourceId && a.Timestamp > DateTime.UtcNow.AddYears(-options.AuditLogRetentionYears), ct)`.
     - If any audit log entry references this resource within the 7-year window, the resource is protected from deletion/archival regardless of its own category policy.
     - This prevents the scenario where a notification log is purged at 90 days but an audit trail entry references it within the 7-year window — the notification is retained until the referencing audit log itself expires.

4. **Create `PurgeResult` model**: Create in `src/UPACIP.Service/Retention/Models/PurgeResult.cs`:
   - `RetentionCategory Category`
   - `int RecordsPurged`
   - `DateTime? OldestPurgedDate`
   - `DateTime? NewestPurgedDate`
   - `TimeSpan ExecutionDuration`
   - `Dictionary<string, int> BreakdownByType` — for notification logs, counts per `NotificationType` (confirmation, reminder_24h, reminder_2h, slot_swap).

5. **Implement `DataRetentionService` BackgroundService (AC-4)**: Create in `src/UPACIP.Service/Retention/DataRetentionService.cs` as a `BackgroundService` that runs the nightly purge job.

   - **Schedule calculation**: On each loop iteration, compute the next execution time by parsing `NightlyJobScheduleLocal` (e.g., "03:00") and converting to UTC using `TimeZoneInfo`. If the target time has already passed today, schedule for tomorrow. Use `Task.Delay(nextRunUtc - DateTime.UtcNow, stoppingToken)` to wait until the scheduled time.

   - **Notification log purge (AC-4)**: The primary purge operation:
     - (a) Compute cutoff: `cutoffDate = DateTime.UtcNow.AddDays(-options.CurrentValue.NotificationLogRetentionDays)`.
     - (b) Query candidate records: `dbContext.NotificationLogs.Where(n => n.CreatedAt < cutoffDate)`.
     - (c) For each candidate batch (size = `PurgeBatchSize`): check `IRetentionPolicyGuard.IsProtectedByAuditLogAsync()` if `EnforceAuditLogReferenceProtection` is enabled — skip protected records.
     - (d) Execute batch delete: `dbContext.NotificationLogs.Where(n => candidateIds.Contains(n.Id)).ExecuteDeleteAsync(ct)` using EF Core 8 bulk delete (no entity tracking overhead).
     - (e) Continue batching until no more candidates remain.
     - (f) Build `PurgeResult` with total records purged, date range, execution duration, and per-type breakdown.

   - **Structured summary logging (AC-4)**: After purge completes, emit:
     ```
     Log.Information(
       "RETENTION_PURGE_COMPLETE: Category={Category}, RecordsPurged={Count}, " +
       "OldestPurged={OldestDate}, Duration={Duration}, Breakdown={Breakdown}",
       result.Category, result.RecordsPurged, result.OldestPurgedDate,
       result.ExecutionDuration, result.BreakdownByType);
     ```
     If zero records purged, log at Debug level instead.

   - **Error handling**: Wrap the purge operation in a try-catch. On failure, log the exception via `Log.Error("RETENTION_PURGE_FAILED: Category={Category}, Error={Error}", ...)` and continue to the next scheduled cycle. Do not retry within the same cycle to avoid cascading failures during database issues.

6. **Enforce audit log immutability guard (AC-1)**: The `RetentionPolicyGuard` is designed to be called by any service that attempts entity deletion. For audit logs specifically, the guard always returns `false` for `CanDeleteAsync(RetentionCategory.AuditLogs, ...)`, ensuring no automated process can delete audit log entries. The existing immutable audit log system (US_064) prevents manual modification/deletion at the application level; this guard adds the retention policy layer that prevents automated cleanup jobs from touching audit logs. Log a warning when an automated deletion attempt is blocked: `Log.Warning("RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record created at {CreatedAt}", category, recordCreatedAt)`.

7. **Enforce clinical records indefinite retention (AC-2)**: The `RetentionPolicyGuard.CanDeleteAsync(RetentionCategory.ClinicalRecords, ...)` always returns `false` when `ClinicalRecordsIndefiniteRetention` is true. This covers ClinicalDocument, ExtractedData, and MedicalCode entities. No purge job processes these categories. If a future requirement changes this to a finite retention period, the guard respects the updated configuration (edge case 1 — retroactive policy change).

8. **Register services and bind configuration**: In `Program.cs`: bind `RetentionPolicyOptions` via `builder.Services.Configure<RetentionPolicyOptions>(builder.Configuration.GetSection("RetentionPolicy"))`, register `services.AddScoped<IRetentionPolicyGuard, RetentionPolicyGuard>()`, and register `services.AddHostedService<DataRetentionService>()`. Add the `RetentionPolicy` section to `appsettings.json`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   └── GlobalExceptionHandlerMiddleware.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Validation/                              ← from US_010/US_085
│   │   ├── Monitoring/
│   │   │   └── UptimeMonitoringService.cs           ← from US_083 (BackgroundService pattern)
│   │   └── Caching/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Patient.cs                           ← from US_008
│       │   ├── Appointment.cs                       ← from US_008
│       │   ├── AuditLog.cs                          ← from US_008
│       │   ├── NotificationLog.cs                   ← from US_008
│       │   ├── ClinicalDocument.cs                  ← from US_008
│       │   ├── ExtractedData.cs                     ← from US_008
│       │   └── MedicalCode.cs                       ← from US_008
│       └── Configurations/
│           ├── AuditLogConfiguration.cs             ← from US_008
│           └── NotificationLogConfiguration.cs      ← from US_008
├── Server/
│   ├── Services/
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (entities + DbContext), US_064 (immutable audit log), and US_083 (BackgroundService pattern) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Retention/Models/RetentionCategory.cs | Enum: AuditLogs, ClinicalRecords, Appointments, Notifications, CancelledAppointments |
| CREATE | src/UPACIP.Service/Retention/Models/RetentionPolicyOptions.cs | Config: per-category retention periods, nightly schedule, batch size |
| CREATE | src/UPACIP.Service/Retention/Models/PurgeResult.cs | Result DTO: Category, RecordsPurged, dates, duration, type breakdown |
| CREATE | src/UPACIP.Service/Retention/RetentionPolicyGuard.cs | IRetentionPolicyGuard: blocks premature deletion, audit-log reference protection |
| CREATE | src/UPACIP.Service/Retention/DataRetentionService.cs | BackgroundService: nightly notification log purge with batched EF Core bulk delete |
| MODIFY | src/UPACIP.Api/Program.cs | Bind RetentionPolicyOptions, register IRetentionPolicyGuard and DataRetentionService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add RetentionPolicy configuration section |

## External References

- [IOptionsMonitor — Configuration Change Notifications](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
- [EF Core 8 Bulk Operations — ExecuteDeleteAsync](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
- [BackgroundService — .NET Hosted Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)
- [HIPAA Retention Requirements — 45 CFR § 164.530(j)](https://www.law.cornell.edu/cfr/text/45/part-164/subpart-E)
- [Serilog Structured Logging](https://github.com/serilog/serilog/wiki/Structured-Data)

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
- [ ] Audit log entries are never deleted by any automated process (AC-1)
- [ ] `RetentionPolicyGuard.CanDeleteAsync(AuditLogs, ...)` always returns `false`
- [ ] Clinical records (ClinicalDocument, ExtractedData, MedicalCode) are never auto-deleted or archived (AC-2)
- [ ] Notification logs older than 90 days are purged by the nightly job (AC-4)
- [ ] Nightly purge job logs structured summary with per-type breakdown and total count
- [ ] Purge runs in batches of configurable size (default 1000) to avoid long-running transactions
- [ ] Changing `NotificationLogRetentionDays` in appsettings takes effect on next cycle without restart (edge case 1)
- [ ] Notification log referenced by audit log within 7-year window is NOT purged (edge case 2)
- [ ] Purge job failure logs error and continues to next scheduled cycle without retry
- [ ] `AuditLogRetentionYears` below 7 is clamped to 7 with a warning log

## Implementation Checklist

- [ ] Create `RetentionCategory` enum with all 5 data categories
- [ ] Create `RetentionPolicyOptions` with per-category retention periods and HIPAA minimum enforcement
- [ ] Create `PurgeResult` model with category, counts, dates, duration, and type breakdown
- [ ] Implement `IRetentionPolicyGuard` / `RetentionPolicyGuard` with audit log and clinical record protection
- [ ] Implement audit-log reference protection via `IsProtectedByAuditLogAsync`
- [ ] Implement `DataRetentionService` BackgroundService with nightly schedule and notification log purge
- [ ] Add RetentionPolicy configuration section to appsettings.json
- [ ] Register RetentionPolicyOptions, IRetentionPolicyGuard, and DataRetentionService in Program.cs
