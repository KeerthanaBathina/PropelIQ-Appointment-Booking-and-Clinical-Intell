# Task - task_002_be_patient_archival_pipeline

## Requirement Reference

- User Story: us_087
- Story Location: .propel/context/tasks/EP-016/us_087/us_087.md
- Acceptance Criteria:
  - AC-4: Given the archival pipeline runs, When records exceed their retention period, Then they are moved to an archive schema with original IDs preserved for cross-reference.
- Edge Case:
  - What happens when a soft-deleted patient record is referenced by an active appointment? System prevents soft-deletion until all dependent active records are resolved or cancelled (handled by task_001; archival pipeline only processes patients already soft-deleted with no active dependencies).
  - How does the system handle restoring a soft-deleted patient record? Admin can clear the deleted_at timestamp, which restores the record and all its dependent data to active status (handled by task_001; once archived, restoration requires archive retrieval — out of scope for this task, documented as a future enhancement).

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

Implement the patient record archival pipeline that moves soft-deleted patient records and their dependent data to the PostgreSQL `archive` schema (established by US_086 task_002) when they exceed a configurable retention period after soft deletion (DR-021, NFR-033). This task extends the existing `DataRetentionService` nightly batch job (US_086 task_001) with a patient archival step that processes soft-deleted patients whose `DeletedAt` timestamp is older than the configured retention threshold. Unlike appointment archival (US_086 task_002) which archives individual appointments, patient archival is a **cascading operation** — when a patient is archived, all dependent entities (appointments, intake data, clinical documents, extracted data, medical codes) are moved to their respective archive tables in a single transaction, preserving original IDs and FK relationships for cross-reference. An `ArchivedPatientReference` record is retained in the main `patients` table with the original patient ID, name, and archive timestamp so that audit log entries referencing this patient remain resolvable. The archival respects `IRetentionPolicyGuard.IsProtectedByAuditLogAsync()` — if any audit log references the patient within the 7-year window, the patient is skipped until all audit references expire. Clinical records attached to the patient are archived (not purged) even though DR-017 specifies indefinite clinical record retention — the archive schema preserves them indefinitely in cold storage rather than the hot `public` schema.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires all domain entities with FK relationships.
- US_008 task_002_be_efcore_configuration_migrations — Requires ApplicationDbContext with DbSets and configurations.
- US_086 task_001_be_retention_policy_engine — Requires RetentionPolicyOptions, IRetentionPolicyGuard, DataRetentionService.
- US_086 task_002_be_appointment_archival_service — Requires archive schema and archive.appointments table pattern.
- US_087 task_001_be_soft_delete_service — Requires IPatientSoftDeleteService (patients must be soft-deleted before archival).

## Impacted Components

- **NEW** `src/UPACIP.Service/Retention/PatientArchivalService.cs` — IPatientArchivalService: cascading patient + dependent entity archival to archive schema
- **NEW** `src/UPACIP.Service/Retention/Models/ArchivedPatientReference.cs` — Entity: reference stub in main patients table after archival
- **NEW** `src/UPACIP.DataAccess/Migrations/YYYYMMDD_AddPatientArchiveTables.cs` — Migration: archive.patients, archive.intake_data, archive.clinical_documents, archive.extracted_data, archive.medical_codes tables
- **MODIFY** `src/UPACIP.Service/Retention/Models/RetentionPolicyOptions.cs` — Add SoftDeletedPatientArchivalDays (configurable retention after soft delete)
- **MODIFY** `src/UPACIP.Service/Retention/DataRetentionService.cs` — Add patient archival step after appointment archival
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<ArchivedPatientReference>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IPatientArchivalService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add SoftDeletedPatientArchivalDays to RetentionPolicy section

## Implementation Plan

1. **Add `SoftDeletedPatientArchivalDays` to `RetentionPolicyOptions`**: Modify the existing `RetentionPolicyOptions` (from US_086 task_001) to add:
   - `int SoftDeletedPatientArchivalDays` (default: 365 — archive soft-deleted patients after 1 year of being in soft-deleted state). This is the period a soft-deleted patient remains in the main schema for potential restore. After this period, the patient is moved to the archive schema.
   Update `appsettings.json`:
   ```json
   "RetentionPolicy": {
     ...existing properties...,
     "SoftDeletedPatientArchivalDays": 365
   }
   ```

2. **Create `ArchivedPatientReference` entity**: Create in `src/UPACIP.Service/Retention/Models/ArchivedPatientReference.cs`:
   - `Guid Id` (PK, same as original patient_id — preserves ID for audit log cross-reference).
   - `string FullName` (original patient name — for display in audit log views without archive lookup).
   - `string Email` (original email — for identification).
   - `DateTime DeletedAtUtc` (when the patient was soft-deleted).
   - `DateTime ArchivedAtUtc` (when the patient was moved to archive).
   - `string ArchiveSchema` (default: `"archive"` — allows future schema evolution).
   This reference stays in the main `patients` table (or a dedicated `archived_patient_references` table) so that audit log entries that reference this patient ID can still resolve to a meaningful display name.

3. **Create archive schema migration**: Generate an EF Core migration that creates archive tables for all patient-dependent entities:
   - (a) `archive.patients` — mirrors `patients` table structure plus `ArchivedAtUtc`.
   - (b) `archive.intake_data` — mirrors `intake_data` table plus `ArchivedAtUtc`.
   - (c) `archive.clinical_documents` — mirrors `clinical_documents` table plus `ArchivedAtUtc`.
   - (d) `archive.extracted_data` — mirrors `extracted_data` table plus `ArchivedAtUtc`.
   - (e) `archive.medical_codes` — mirrors `medical_codes` table plus `ArchivedAtUtc`.
   - (f) `archived_patient_references` table in the default schema.
   - (g) Index on `archive.patients(patient_id)` for lookup queries.
   - (h) Index on `archived_patient_references(id)` (PK) for audit log resolution.
   The `archive.appointments` table already exists from US_086 task_002. All archive tables use raw SQL in the migration since EF Core does not natively support multi-schema entity mapping for the same entity type.

4. **Implement `IPatientArchivalService` / `PatientArchivalService`**: Create in `src/UPACIP.Service/Retention/PatientArchivalService.cs` with constructor injection of `ApplicationDbContext`, `IRetentionPolicyGuard`, `IOptionsMonitor<RetentionPolicyOptions>`, and `ILogger<PatientArchivalService>`.

   Method `Task<ArchivalResult> ArchiveSoftDeletedPatientsAsync(CancellationToken ct)`:
   - (a) Compute cutoff: `cutoffDate = DateTime.UtcNow.AddDays(-options.CurrentValue.SoftDeletedPatientArchivalDays)`.
   - (b) Query candidates using `IgnoreQueryFilters()`: `dbContext.Patients.IgnoreQueryFilters().Where(p => p.DeletedAt != null && p.DeletedAt < cutoffDate).OrderBy(p => p.DeletedAt).Take(options.CurrentValue.PurgeBatchSize)`.
   - (c) For each candidate patient:
     - Check `IRetentionPolicyGuard.IsProtectedByAuditLogAsync("Patient", patient.Id, ct)`. If protected, skip and increment skipped counter.
     - Verify no active appointments exist (safety check — task_001 should have prevented soft-delete with active dependencies, but guard against data inconsistency).
   - (d) For non-protected candidates, execute cascading archival within a transaction:
     - Insert patient into `archive.patients` via raw SQL.
     - Query and insert all dependent records: `appointments` → `archive.appointments`, `intake_data` → `archive.intake_data`, `clinical_documents` → `archive.clinical_documents`, `extracted_data` → `archive.extracted_data` (via document FK), `medical_codes` → `archive.medical_codes`.
     - Create `ArchivedPatientReference` with original ID, name, email, timestamps.
     - Delete dependent records from main tables (bottom-up to respect FK constraints): extracted_data → clinical_documents → medical_codes → intake_data → appointments (that aren't already archived by US_086) → patient.
     - Use `ExecuteDeleteAsync` for bulk operations where possible.
   - (e) Commit transaction. If a single patient's archival fails, log the error and continue to the next patient.
   - (f) Return `ArchivalResult` with counts and timing.

5. **Handle notification logs for archived patients**: NotificationLogs reference appointments (not patients directly). Since US_086 task_001 purges notification logs after 90 days, and this pipeline archives patients after 365 days of soft-deletion, all notification logs for the archived patient's appointments will already be purged. No additional handling needed. Add a safety comment documenting this assumption.

6. **Handle clinical records indefinite retention (DR-017)**: DR-017 requires indefinite clinical record retention. Moving clinical records to the `archive` schema satisfies this — the archive schema is permanent cold storage, not a deletion pipeline. The `IRetentionPolicyGuard.CanDeleteAsync(ClinicalRecords, ...)` still returns `false`, but archival is not deletion. Document this distinction: archival preserves data in a different schema; the retention guard prevents permanent deletion.

7. **Extend `DataRetentionService` with patient archival step**: Modify the nightly batch job (from US_086) to add a fourth step after appointment archival:
   - Step 1: Notification log purge (US_086 task_001).
   - Step 2: Completed appointment archival (US_086 task_002).
   - Step 3: Cancelled appointment archival (US_086 task_002).
   - Step 4: Soft-deleted patient archival (this task).
   Log the `ArchivalResult`:
   ```
   Log.Information(
     "RETENTION_PATIENT_ARCHIVAL_COMPLETE: Archived={Count}, " +
     "Skipped(AuditProtected)={Skipped}, Duration={Duration}",
     result.RecordsArchived, result.RecordsSkippedAuditProtected,
     result.ExecutionDuration);
   ```

8. **Register services and update configuration**: In `Program.cs`, add `builder.Services.AddScoped<IPatientArchivalService, PatientArchivalService>()`. Add `DbSet<ArchivedPatientReference>` to `ApplicationDbContext`. Update `appsettings.json` with `SoftDeletedPatientArchivalDays`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── PatientController.cs                 ← modified by task_001
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Patients/
│   │   │   ├── PatientSoftDeleteService.cs          ← from task_001
│   │   │   └── Models/
│   │   │       ├── PatientListItem.cs               ← from task_001
│   │   │       └── SoftDeleteResult.cs              ← from task_001
│   │   ├── Retention/
│   │   │   ├── Models/
│   │   │   │   ├── RetentionCategory.cs             ← from US_086
│   │   │   │   ├── RetentionPolicyOptions.cs        ← from US_086
│   │   │   │   ├── PurgeResult.cs                   ← from US_086
│   │   │   │   ├── ArchivalResult.cs                ← from US_086
│   │   │   │   └── ArchivedAppointmentReference.cs  ← from US_086
│   │   │   ├── RetentionPolicyGuard.cs              ← from US_086
│   │   │   ├── DataRetentionService.cs              ← from US_086
│   │   │   └── AppointmentArchivalService.cs        ← from US_086
│   │   └── Validation/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Patient.cs                           ← from US_008
│       │   ├── Appointment.cs
│       │   ├── IntakeData.cs
│       │   ├── ClinicalDocument.cs
│       │   ├── ExtractedData.cs
│       │   ├── MedicalCode.cs
│       │   └── AuditLog.cs
│       ├── Configurations/
│       └── Migrations/
│           └── YYYYMMDD_AddArchiveSchema.cs         ← from US_086
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes task_001 (soft delete service), US_086 (retention policy + appointment archival), US_008 (entities), and US_064 (audit log) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Retention/PatientArchivalService.cs | IPatientArchivalService: cascading patient + dependent entity archival |
| CREATE | src/UPACIP.Service/Retention/Models/ArchivedPatientReference.cs | Reference stub with original ID, name, email, timestamps |
| CREATE | src/UPACIP.DataAccess/Migrations/YYYYMMDD_AddPatientArchiveTables.cs | Archive tables for patients, intake_data, clinical_documents, extracted_data, medical_codes |
| MODIFY | src/UPACIP.Service/Retention/Models/RetentionPolicyOptions.cs | Add SoftDeletedPatientArchivalDays configuration |
| MODIFY | src/UPACIP.Service/Retention/DataRetentionService.cs | Add patient archival as step 4 in nightly batch |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<ArchivedPatientReference> |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPatientArchivalService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add SoftDeletedPatientArchivalDays to RetentionPolicy |

## External References

- [EF Core — IgnoreQueryFilters](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.ignorequeryfilters)
- [EF Core — Raw SQL Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli#adding-raw-sql)
- [PostgreSQL — Schema-based Data Partitioning](https://www.postgresql.org/docs/16/ddl-schemas.html)
- [EF Core 8 — ExecuteDeleteAsync Bulk Operations](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
- [HIPAA — Record Retention After Patient Deactivation](https://www.hipaajournal.com/hipaa-record-retention-requirements/)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build DataAccess project (includes migration)
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate migration
dotnet ef migrations add AddPatientArchiveTables --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Archive migration creates archive.patients, archive.intake_data, archive.clinical_documents, archive.extracted_data, archive.medical_codes tables
- [ ] Soft-deleted patient older than SoftDeletedPatientArchivalDays is moved to archive.patients (AC-4)
- [ ] All dependent records (appointments, intake, documents, extracted data, medical codes) are archived in same transaction
- [ ] Original IDs are preserved in archive tables for cross-reference (AC-4)
- [ ] ArchivedPatientReference is retained in main schema with patient ID and name
- [ ] Audit log entries referencing archived patient ID still resolve via ArchivedPatientReference
- [ ] Patient protected by audit-log reference within 7-year window is NOT archived
- [ ] Clinical records are archived (not deleted) per DR-017 indefinite retention
- [ ] FK constraints are respected during deletion (bottom-up order)
- [ ] Single patient archival failure does not abort the entire batch
- [ ] Changing SoftDeletedPatientArchivalDays takes effect on next nightly cycle

## Implementation Checklist

- [ ] Add SoftDeletedPatientArchivalDays to RetentionPolicyOptions and appsettings.json
- [ ] Create ArchivedPatientReference entity with original ID, name, email, timestamps
- [ ] Create archive migration for patients, intake_data, clinical_documents, extracted_data, medical_codes
- [ ] Implement IPatientArchivalService with cascading archival in transaction
- [ ] Add audit-log reference protection check before archiving each patient
- [ ] Handle FK dependency order during main-table deletion (bottom-up)
- [ ] Extend DataRetentionService nightly batch with patient archival step
- [ ] Register IPatientArchivalService and add DbSet<ArchivedPatientReference> to ApplicationDbContext
