# Task - task_001_be_soft_delete_service

## Requirement Reference

- User Story: us_087
- Story Location: .propel/context/tasks/EP-016/us_087/us_087.md
- Acceptance Criteria:
  - AC-1: Given a patient record is "deleted," When the delete operation executes, Then the record's deleted_at field is set to the current timestamp rather than physically removing the row.
  - AC-2: Given soft-deleted records exist, When any query retrieves patient data, Then soft-deleted records are excluded from results by default (global query filter on deleted_at IS NULL).
  - AC-3: Given an admin needs to view deleted records, When they query with "include deleted" flag, Then soft-deleted records are included with visual indication of deleted status.
- Edge Case:
  - What happens when a soft-deleted patient record is referenced by an active appointment? System prevents soft-deletion until all dependent active records are resolved or cancelled.
  - How does the system handle restoring a soft-deleted patient record? Admin can clear the deleted_at timestamp, which restores the record and all its dependent data to active status.

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

Implement the patient soft delete service, dependency guard, admin include-deleted query, and patient restore operation per DR-021 and NFR-033. This task builds on the existing infrastructure: the `Patient` entity already has a `DeletedAt` nullable timestamp (US_008 task_001), the `PatientConfiguration` already applies `HasQueryFilter(p => p.DeletedAt == null)` for global query filtering (US_008 task_002), and the `SoftDeleteReferenceValidator` already prevents new appointments from referencing soft-deleted patients (US_010 task_002). What US_087 adds is the **service-layer orchestration** that makes soft delete operational: (1) **`IPatientSoftDeleteService`** — sets `DeletedAt = DateTime.UtcNow` on the Patient entity instead of issuing a physical DELETE, with a pre-deletion dependency check that prevents soft-deletion when the patient has active (scheduled) appointments, active intake records in processing, or clinical documents currently queued for AI processing (edge case 1); (2) **Admin include-deleted query** — a service method and API endpoint that uses `IgnoreQueryFilters()` to return patients including soft-deleted ones, with each result annotated with `IsDeleted` and `DeletedAt` fields for visual indication (AC-3); (3) **Patient restore** — an admin-only operation that clears `DeletedAt` to null, effectively restoring the patient and all dependent data to active status since dependent entities (appointments, intake, documents) are linked by FK and were never physically deleted (edge case 2). All operations are audit-logged via the existing immutable audit log system (US_064).

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires Patient entity with `DeletedAt` nullable field.
- US_008 task_002_be_efcore_configuration_migrations — Requires `PatientConfiguration` with `HasQueryFilter(p => p.DeletedAt == null)`.
- US_010 task_002_be_validation_error_handling — Requires `SoftDeleteReferenceValidator` for reference protection.
- US_064 — Requires immutable audit log system for recording soft-delete and restore operations.

## Impacted Components

- **NEW** `src/UPACIP.Service/Patients/PatientSoftDeleteService.cs` — IPatientSoftDeleteService: soft delete with dependency guard, restore, include-deleted query
- **NEW** `src/UPACIP.Service/Patients/Models/PatientListItem.cs` — DTO: patient fields + IsDeleted, DeletedAt for admin queries
- **NEW** `src/UPACIP.Service/Patients/Models/SoftDeleteResult.cs` — Result DTO: Success, BlockedReason, ActiveDependencies
- **MODIFY** `src/UPACIP.Api/Controllers/PatientController.cs` — Add DELETE (soft), POST restore, GET with include-deleted flag
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IPatientSoftDeleteService

## Implementation Plan

1. **Create `SoftDeleteResult` model**: Create in `src/UPACIP.Service/Patients/Models/SoftDeleteResult.cs`:
   - `bool Success` — true if soft delete was performed.
   - `string? BlockedReason` — descriptive reason if blocked (e.g., "Patient has 3 active appointments").
   - `List<ActiveDependency> ActiveDependencies` — list of blocking dependencies for transparency.
   - Nested class `ActiveDependency` with `string EntityType` (e.g., "Appointment"), `int Count`, `string Detail` (e.g., "3 scheduled appointments").

2. **Create `PatientListItem` DTO**: Create in `src/UPACIP.Service/Patients/Models/PatientListItem.cs`:
   - `Guid PatientId`
   - `string FullName`
   - `string Email`
   - `DateTime CreatedAt`
   - `bool IsDeleted` — true when `DeletedAt` is not null (AC-3 visual indication).
   - `DateTime? DeletedAt` — the soft-delete timestamp, null for active patients.
   This DTO is returned by the admin "include deleted" query and provides the visual indication of deleted status required by AC-3.

3. **Implement `IPatientSoftDeleteService` / `PatientSoftDeleteService`**: Create in `src/UPACIP.Service/Patients/PatientSoftDeleteService.cs` with constructor injection of `ApplicationDbContext` and `ILogger<PatientSoftDeleteService>`. Three primary methods:

   **Method `SoftDeleteAsync(Guid patientId, Guid performedByUserId, CancellationToken ct)` (AC-1, edge case 1)**:
   - (a) Load the patient using `IgnoreQueryFilters()` to handle re-deletion attempts: `dbContext.Patients.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == patientId, ct)`. Return 404 if not found.
   - (b) If `patient.DeletedAt` is already set, return `SoftDeleteResult { Success = false, BlockedReason = "Patient is already soft-deleted" }` — idempotency guard.
   - (c) **Dependency check (edge case 1)**: Query active dependencies that block deletion:
     - Active appointments: `dbContext.Appointments.CountAsync(a => a.PatientId == patientId && (a.Status == AppointmentStatus.Scheduled), ct)`.
     - Active intake records: `dbContext.IntakeRecords.CountAsync(i => i.PatientId == patientId && i.ProcessingStatus == ProcessingStatus.Processing, ct)` (if intake has a processing state).
     - Queued clinical documents: `dbContext.ClinicalDocuments.CountAsync(d => d.PatientId == patientId && (d.ProcessingStatus == ProcessingStatus.Queued || d.ProcessingStatus == ProcessingStatus.Processing), ct)`.
   - (d) If any active dependencies exist, return `SoftDeleteResult { Success = false, BlockedReason = "Patient has active dependencies that must be resolved first", ActiveDependencies = [...] }`. Do NOT soft-delete.
   - (e) If no active dependencies: set `patient.DeletedAt = DateTime.UtcNow`, call `dbContext.SaveChangesAsync(ct)`.
   - (f) Log the operation: `Log.Information("PATIENT_SOFT_DELETED: PatientId={PatientId}, PerformedBy={UserId}", patientId, performedByUserId)`.
   - (g) Return `SoftDeleteResult { Success = true }`.

   **Method `RestoreAsync(Guid patientId, Guid performedByUserId, CancellationToken ct)` (edge case 2)**:
   - (a) Load the patient using `IgnoreQueryFilters()`: must find a record with `DeletedAt != null`. Return 404 if not found or not soft-deleted.
   - (b) Set `patient.DeletedAt = null`, call `dbContext.SaveChangesAsync(ct)`.
   - (c) Dependent data (appointments, intake, documents, medical codes) is automatically restored because it was never physically deleted — the FK relationships remain intact. The global query filter on Patient was the only barrier hiding this data.
   - (d) Log: `Log.Information("PATIENT_RESTORED: PatientId={PatientId}, PerformedBy={UserId}", patientId, performedByUserId)`.
   - (e) Return success.

   **Method `GetPatientsIncludingDeletedAsync(int page, int pageSize, CancellationToken ct)` (AC-3)**:
   - (a) Query using `IgnoreQueryFilters()`: `dbContext.Patients.IgnoreQueryFilters().OrderBy(p => p.FullName).Skip((page - 1) * pageSize).Take(pageSize)`.
   - (b) Project to `PatientListItem` DTOs with `IsDeleted = p.DeletedAt != null` and `DeletedAt = p.DeletedAt`.
   - (c) Return paginated result with total count (including deleted).

4. **Verify existing global query filter coverage (AC-2)**: AC-2 is already implemented by `PatientConfiguration.HasQueryFilter(p => p.DeletedAt == null)` from US_008 task_002. This task validates that the filter works correctly with the new soft delete service: when a patient is soft-deleted via `SoftDeleteAsync()`, all subsequent standard queries (`dbContext.Patients.Where(...)`) automatically exclude the record. No code changes needed for AC-2 — the existing infrastructure handles it. Document this in the implementation validation strategy.

5. **Add soft delete and restore API endpoints**: Modify `PatientController.cs` to add:

   **`DELETE /api/patients/{patientId}`** (AC-1):
   - Authorize: staff or admin role.
   - Calls `IPatientSoftDeleteService.SoftDeleteAsync(patientId, userId, ct)`.
   - Returns 200 with `SoftDeleteResult` if successful, 409 Conflict with `SoftDeleteResult` (including `ActiveDependencies`) if blocked by edge case 1, 404 if patient not found.
   - Response body includes `BlockedReason` and `ActiveDependencies` when blocked, so the caller knows exactly what to resolve.

   **`POST /api/patients/{patientId}/restore`** (edge case 2):
   - Authorize: admin role only (restore is a privileged operation).
   - Calls `IPatientSoftDeleteService.RestoreAsync(patientId, userId, ct)`.
   - Returns 200 on success, 404 if patient not found or not soft-deleted.

   **`GET /api/patients?includeDeleted=true`** (AC-3):
   - Authorize: admin role only.
   - When `includeDeleted` query parameter is true, calls `GetPatientsIncludingDeletedAsync(page, pageSize, ct)`.
   - When false or absent, uses the standard patient list query (global filter applies).
   - Returns `List<PatientListItem>` with `IsDeleted` and `DeletedAt` annotations.

6. **Integrate audit logging**: After each soft delete and restore operation, create an `AuditLog` entry via the existing audit log system (US_064):
   - Soft delete: `Action = AuditAction.DataDelete`, `ResourceType = "Patient"`, `ResourceId = patientId`.
   - Restore: `Action = AuditAction.DataModify`, `ResourceType = "Patient"`, `ResourceId = patientId`.
   This ensures HIPAA-compliant tracking of who deleted and restored patient records. The audit log's 7-year retention (US_086 task_001) provides the compliance trail.

7. **Register service in DI**: In `Program.cs`, add `builder.Services.AddScoped<IPatientSoftDeleteService, PatientSoftDeleteService>()`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── PatientController.cs                 ← from US_012/US_013
│   │   │   └── AppointmentController.cs
│   │   ├── Middleware/
│   │   │   └── GlobalExceptionHandlerMiddleware.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Validation/
│   │   │   ├── SoftDeleteReferenceValidator.cs      ← from US_010
│   │   │   ├── EmailValidator.cs
│   │   │   └── AppointmentDateValidator.cs
│   │   └── Retention/
│   │       ├── RetentionPolicyGuard.cs              ← from US_086
│   │       └── DataRetentionService.cs              ← from US_086
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Patient.cs                           ← from US_008 (has DeletedAt)
│       │   ├── Appointment.cs                       ← from US_008
│       │   ├── IntakeData.cs                        ← from US_008
│       │   ├── ClinicalDocument.cs                  ← from US_008
│       │   └── AuditLog.cs                          ← from US_008
│       └── Configurations/
│           └── PatientConfiguration.cs              ← from US_008 (HasQueryFilter)
├── Server/
│   ├── Services/
│   │   └── PatientService.cs
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (entities + query filter), US_010 (SoftDeleteReferenceValidator), US_064 (audit log), and US_086 (retention policy) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Patients/PatientSoftDeleteService.cs | IPatientSoftDeleteService: soft delete, restore, include-deleted query |
| CREATE | src/UPACIP.Service/Patients/Models/PatientListItem.cs | DTO with IsDeleted, DeletedAt for admin queries |
| CREATE | src/UPACIP.Service/Patients/Models/SoftDeleteResult.cs | Result with Success, BlockedReason, ActiveDependencies |
| MODIFY | src/UPACIP.Api/Controllers/PatientController.cs | Add DELETE, POST restore, GET includeDeleted endpoints |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPatientSoftDeleteService |

## External References

- [EF Core — Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core — IgnoreQueryFilters](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.ignorequeryfilters)
- [ASP.NET Core — Authorization Policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [Soft Delete Pattern — Best Practices](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties)

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
- [ ] DELETE /api/patients/{id} sets DeletedAt to current timestamp (AC-1)
- [ ] Patient is NOT physically removed from the database after soft delete (AC-1)
- [ ] Standard patient queries exclude soft-deleted records without any code changes (AC-2 — existing query filter)
- [ ] GET /api/patients?includeDeleted=true returns soft-deleted patients with IsDeleted=true (AC-3)
- [ ] Soft-deleted patient shows DeletedAt timestamp in the response (AC-3)
- [ ] Soft delete is blocked when patient has active scheduled appointments (edge case 1)
- [ ] Soft delete response includes list of blocking dependencies when blocked
- [ ] POST /api/patients/{id}/restore clears DeletedAt and restores patient to active status (edge case 2)
- [ ] Restored patient's dependent data (appointments, intake, documents) is immediately queryable
- [ ] Audit log entries are created for both soft delete and restore operations
- [ ] Restore endpoint is restricted to admin role only

## Implementation Checklist

- [ ] Create `SoftDeleteResult` model with Success, BlockedReason, ActiveDependencies
- [ ] Create `PatientListItem` DTO with IsDeleted and DeletedAt for admin queries
- [ ] Implement `IPatientSoftDeleteService.SoftDeleteAsync` with dependency guard for active appointments/intake/documents
- [ ] Implement `IPatientSoftDeleteService.RestoreAsync` to clear DeletedAt for admin restore
- [ ] Implement `IPatientSoftDeleteService.GetPatientsIncludingDeletedAsync` using IgnoreQueryFilters
- [ ] Add DELETE, POST restore, and GET includeDeleted endpoints to PatientController
- [ ] Integrate audit logging for soft delete and restore operations
- [ ] Register IPatientSoftDeleteService in Program.cs
