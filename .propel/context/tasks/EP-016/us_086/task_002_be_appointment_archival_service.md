# Task - task_002_be_appointment_archival_service

## Requirement Reference

- User Story: us_086
- Story Location: .propel/context/tasks/EP-016/us_086/us_086.md
- Acceptance Criteria:
  - AC-3: Given appointment history records exist, When they are older than 3 years, Then the system archives them to cold storage but maintains references for patient history queries.
  - AC-5: Given cancelled appointment records exist, When they are older than 1 year, Then the system archives them with a reference record retained in the main table.
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

Implement the appointment archival service that moves expired appointment and cancelled appointment records to a PostgreSQL archive schema while maintaining reference records in the main table for patient history queries (DR-018, DR-020). This task delivers two capabilities: (1) **Completed appointment archival** — appointments older than 3 years (configurable via `RetentionPolicyOptions.AppointmentRetentionYears` from task_001) are moved from the `appointments` table to an `archive.appointments` table, with an `ArchivedAppointmentReference` record retained in the main table containing the original appointment ID, patient ID, appointment time, and archive timestamp so that patient history queries can still reference the appointment and navigate to the archive if full details are needed (AC-3); (2) **Cancelled appointment archival** — cancelled appointments older than 1 year (configurable via `RetentionPolicyOptions.CancelledAppointmentRetentionYears`) follow the same archive pattern with a reference record retained (AC-5). Both archival operations respect the audit-log reference protection from task_001's `IRetentionPolicyGuard` — if an appointment is referenced by an audit log within the 7-year window, it is skipped until the audit reference expires (edge case 2). The archival runs as an extension of the `DataRetentionService` nightly batch job (from task_001), executing after the notification log purge.

## Dependent Tasks

- US_008 task_001_be_entity_models — Requires Appointment entity with Status enum and timestamps.
- US_008 task_002_be_efcore_configuration_migrations — Requires ApplicationDbContext with DbSet<Appointment>.
- US_086 task_001_be_retention_policy_engine — Requires RetentionPolicyOptions, IRetentionPolicyGuard, and DataRetentionService BackgroundService.
- US_087 — Requires archive schema pattern (archive PostgreSQL schema). If US_087 is not yet completed, this task creates the minimal archive schema needed.

## Impacted Components

- **NEW** `src/UPACIP.Service/Retention/AppointmentArchivalService.cs` — IAppointmentArchivalService: moves expired appointments to archive schema with reference records
- **NEW** `src/UPACIP.Service/Retention/Models/ArchivedAppointmentReference.cs` — Entity: reference stub retained in main table after archival
- **NEW** `src/UPACIP.Service/Retention/Models/ArchivalResult.cs` — Result DTO: Category, RecordsArchived, RecordsSkipped (audit-protected), duration
- **NEW** `src/UPACIP.DataAccess/Migrations/YYYYMMDD_AddArchiveSchema.cs` — EF Core migration: create archive schema and archive.appointments table
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<ArchivedAppointmentReference>, configure archive schema mapping
- **MODIFY** `src/UPACIP.Service/Retention/DataRetentionService.cs` — Add appointment archival steps after notification purge
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IAppointmentArchivalService

## Implementation Plan

1. **Create `ArchivedAppointmentReference` entity**: Create in `src/UPACIP.Service/Retention/Models/ArchivedAppointmentReference.cs`:
   - `Guid Id` (PK, same as original appointment_id — preserves the original ID for cross-reference per AC-3/AC-5).
   - `Guid PatientId` (FK to Patient — maintained for patient history queries).
   - `DateTime AppointmentTime` (original appointment time — allows display in patient timeline without archive lookup).
   - `string Status` (original status at time of archival: "completed", "cancelled", etc.).
   - `DateTime ArchivedAtUtc` (timestamp of archival operation).
   - `string ArchiveTable` (target archive table: `"archive.appointments"` — allows future archive schema evolution).
   This entity stays in the main `appointments` schema (or a dedicated `archived_appointment_references` table) and is queryable via standard EF Core. Patient history endpoints can join on this to show archived appointments with a "view archived details" option.

2. **Create archive schema migration**: Generate an EF Core migration that:
   - (a) Creates the `archive` PostgreSQL schema: `migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS archive;")`.
   - (b) Creates `archive.appointments` table mirroring the main `appointments` table structure: all columns from the Appointment entity plus `ArchivedAtUtc` timestamp. This is a raw SQL migration since EF Core doesn't natively support multi-schema entity mapping for the same entity type.
   - (c) Creates `archived_appointment_references` table in the default schema with the `ArchivedAppointmentReference` entity structure.
   - (d) Adds an index on `archive.appointments(patient_id, appointment_time)` for archive lookup queries.
   - (e) Adds an index on `archived_appointment_references(patient_id)` for patient history queries.

3. **Create `ArchivalResult` model**: Create in `src/UPACIP.Service/Retention/Models/ArchivalResult.cs`:
   - `RetentionCategory Category` — Appointments or CancelledAppointments.
   - `int RecordsArchived` — count of records moved to archive.
   - `int RecordsSkippedAuditProtected` — count of records skipped due to audit-log reference protection (edge case 2).
   - `DateTime? OldestArchivedDate` — earliest appointment_time archived.
   - `DateTime? NewestArchivedDate` — latest appointment_time archived.
   - `TimeSpan ExecutionDuration` — total archival operation time.

4. **Implement `IAppointmentArchivalService` / `AppointmentArchivalService` (AC-3, AC-5, edge case 2)**: Create in `src/UPACIP.Service/Retention/AppointmentArchivalService.cs` with constructor injection of `ApplicationDbContext`, `IRetentionPolicyGuard`, and `IOptionsMonitor<RetentionPolicyOptions>`.

   Method `Task<ArchivalResult> ArchiveCompletedAppointmentsAsync(CancellationToken ct)` (AC-3):
   - (a) Compute cutoff: `cutoffDate = DateTime.UtcNow.AddYears(-options.CurrentValue.AppointmentRetentionYears)`.
   - (b) Query candidates: `dbContext.Appointments.Where(a => a.Status == AppointmentStatus.Completed && a.CreatedAt < cutoffDate).OrderBy(a => a.CreatedAt).Take(options.CurrentValue.PurgeBatchSize)`.
   - (c) For each candidate: check `IRetentionPolicyGuard.IsProtectedByAuditLogAsync("Appointment", a.Id, ct)`. If protected, skip and increment `RecordsSkippedAuditProtected` counter.
   - (d) For non-protected candidates, execute within a transaction:
     - Insert into `archive.appointments` via raw SQL: `INSERT INTO archive.appointments SELECT *, @archivedAtUtc FROM appointments WHERE id = @id`.
     - Create `ArchivedAppointmentReference` entity with the original ID, patient ID, appointment time, status, and archive timestamp.
     - Delete from main `appointments` table: `dbContext.Appointments.Where(a => a.Id == id).ExecuteDeleteAsync(ct)`.
   - (e) Commit transaction per batch. If a batch fails, log the error and continue to the next batch.
   - (f) Return `ArchivalResult` with aggregated counts and timing.

   Method `Task<ArchivalResult> ArchiveCancelledAppointmentsAsync(CancellationToken ct)` (AC-5):
   - Same pattern as above but with:
     - Cutoff: `DateTime.UtcNow.AddYears(-options.CurrentValue.CancelledAppointmentRetentionYears)`.
     - Filter: `a.Status == AppointmentStatus.Cancelled`.
   - Cancelled appointments follow the same archival mechanics — moved to `archive.appointments` with a reference record retained.

5. **Handle dependent entity archival**: When archiving an appointment, its related `NotificationLog` entries (FK: appointment_id) must also be considered:
   - NotificationLogs older than 90 days are already purged by task_001's nightly job.
   - NotificationLogs younger than 90 days but attached to an appointment being archived: these are NOT orphaned because the `ArchivedAppointmentReference` retains the original appointment ID. However, the FK constraint to the `appointments` table would fail. Resolution: archive the related notification logs to `archive.notification_logs` in the same transaction, or rely on the fact that notification logs are purged first (they are older than the appointment archival threshold by definition — an appointment archived at 3 years will have notification logs already purged at 90 days). Add a safety check: if any notification logs still reference the appointment, skip archival of that appointment until the notification logs are purged. Log: `"ARCHIVAL_DEFERRED: Appointment {Id} has {Count} active notification logs"`.

6. **Extend `DataRetentionService` with archival steps**: Modify the `DataRetentionService` BackgroundService (from task_001) to call `IAppointmentArchivalService` after the notification log purge:
   - Step 1: Notification log purge (from task_001).
   - Step 2: `ArchiveCompletedAppointmentsAsync()` — archive completed appointments older than 3 years.
   - Step 3: `ArchiveCancelledAppointmentsAsync()` — archive cancelled appointments older than 1 year.
   - Log each step's `ArchivalResult` with structured Serilog:
     ```
     Log.Information(
       "RETENTION_ARCHIVAL_COMPLETE: Category={Category}, Archived={Count}, " +
       "Skipped(AuditProtected)={Skipped}, Duration={Duration}",
       result.Category, result.RecordsArchived, result.RecordsSkippedAuditProtected,
       result.ExecutionDuration);
     ```

7. **Provide archive query support for patient history**: Add a method `Task<Appointment?> GetArchivedAppointmentAsync(Guid appointmentId, CancellationToken ct)` to the archival service that queries `archive.appointments` via raw SQL for a specific appointment ID. This allows patient history endpoints to fetch full archived appointment details when a user clicks on an archived reference. The query is read-only and uses `AsNoTracking()` semantics.

8. **Register services**: In `Program.cs`, add `builder.Services.AddScoped<IAppointmentArchivalService, AppointmentArchivalService>()`. Add `DbSet<ArchivedAppointmentReference>` to `ApplicationDbContext`. Run the archive schema migration.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── AppointmentController.cs
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Retention/
│   │   │   ├── Models/
│   │   │   │   ├── RetentionCategory.cs             ← from task_001
│   │   │   │   ├── RetentionPolicyOptions.cs        ← from task_001
│   │   │   │   └── PurgeResult.cs                   ← from task_001
│   │   │   ├── RetentionPolicyGuard.cs              ← from task_001
│   │   │   └── DataRetentionService.cs              ← from task_001
│   │   ├── Validation/
│   │   └── Monitoring/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Appointment.cs                       ← from US_008
│       │   ├── AuditLog.cs                          ← from US_008
│       │   └── NotificationLog.cs                   ← from US_008
│       ├── Configurations/
│       │   └── AppointmentConfiguration.cs          ← from US_008
│       └── Migrations/
├── Server/
│   ├── Services/
│   │   └── AppointmentBookingService.cs
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes task_001 (retention policy engine), US_008 (entities), and US_064 (audit logs) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Retention/AppointmentArchivalService.cs | IAppointmentArchivalService: archive expired appointments with reference records |
| CREATE | src/UPACIP.Service/Retention/Models/ArchivedAppointmentReference.cs | Entity: reference stub with original ID, patient ID, appointment time |
| CREATE | src/UPACIP.Service/Retention/Models/ArchivalResult.cs | Result DTO: archived count, skipped count, dates, duration |
| CREATE | src/UPACIP.DataAccess/Migrations/YYYYMMDD_AddArchiveSchema.cs | Migration: archive schema, archive.appointments table, reference table |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<ArchivedAppointmentReference>, archive schema mapping |
| MODIFY | src/UPACIP.Service/Retention/DataRetentionService.cs | Add appointment archival steps after notification purge |
| MODIFY | src/UPACIP.Api/Program.cs | Register IAppointmentArchivalService |

## External References

- [EF Core Migrations — Raw SQL](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli#adding-raw-sql)
- [PostgreSQL Schemas](https://www.postgresql.org/docs/16/ddl-schemas.html)
- [EF Core 8 ExecuteDeleteAsync](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
- [HIPAA Record Retention Requirements](https://www.hipaajournal.com/hipaa-record-retention-requirements/)
- [BackgroundService Transaction Patterns](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build DataAccess project (includes migration)
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate migration
dotnet ef migrations add AddArchiveSchema --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Archive schema migration creates `archive` schema and `archive.appointments` table
- [ ] Completed appointments older than 3 years are moved to `archive.appointments` (AC-3)
- [ ] `ArchivedAppointmentReference` record is retained in main table with original ID (AC-3)
- [ ] Patient history query can find archived appointment references via patient_id
- [ ] Archived appointment full details retrievable from `archive.appointments` by ID
- [ ] Cancelled appointments older than 1 year are archived with reference record (AC-5)
- [ ] Appointment referenced by audit log within 7-year window is NOT archived (edge case 2)
- [ ] Archival skipped records are counted and logged as audit-protected
- [ ] Changing `AppointmentRetentionYears` takes effect on next nightly cycle (edge case 1)
- [ ] Archival runs in batched transactions — single batch failure does not abort entire operation
- [ ] Appointments with active notification log references are deferred with log message

## Implementation Checklist

- [ ] Create `ArchivedAppointmentReference` entity with original ID, patient ID, appointment time, status, archive timestamp
- [ ] Create archive schema migration with `archive.appointments` table and indexes
- [ ] Create `ArchivalResult` model with archived count, skipped count, dates, duration
- [ ] Implement `IAppointmentArchivalService` with completed appointment archival (3-year threshold)
- [ ] Implement cancelled appointment archival (1-year threshold) in the same service
- [ ] Add audit-log reference protection check before archiving each appointment
- [ ] Extend `DataRetentionService` to call archival service after notification purge
- [ ] Register IAppointmentArchivalService and add DbSet<ArchivedAppointmentReference> to ApplicationDbContext
