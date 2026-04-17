# Task - task_002_be_patient_data_deletion_service

## Requirement Reference

- User Story: us_094
- Story Location: .propel/context/tasks/EP-018/us_094/us_094.md
- Acceptance Criteria:
  - AC-3: Given a patient requests data deletion, When the request is processed, Then all patient data is removed from active systems within 30 days and an audit log entry records the deletion.
  - AC-4: Given data deletion is executed, When the process completes, Then the system verifies no patient data remains in active tables, caches, or file storage (audit logs are retained per legal requirements).
- Edge Case:
  - What happens when a patient requests deletion but has pending appointments? System cancels pending appointments, notifies relevant staff, then proceeds with deletion.
  - How does the system handle deletion requests for data shared with other entities (e.g., consolidated profiles)? System removes the patient's data from consolidated views; shared data points are anonymized rather than deleted.

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
| Caching | Upstash Redis | 7.x |
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

Implement the HIPAA right-to-deletion patient data removal service (NFR-045) that executes a comprehensive, multi-phase deletion pipeline covering all patient data across active database tables, Redis cache, and file storage within a 30-day SLA (AC-3). The deletion pipeline follows a strict order: (1) cancel pending appointments and notify staff (edge case 1), (2) anonymize shared/consolidated data points rather than deleting them (edge case 2), (3) soft-delete the Patient record and hard-delete all dependent entities (Appointments, IntakeData, ClinicalDocuments, ExtractedData, MedicalCodes, QueueEntries, NotificationLogs), (4) purge patient-related Redis cache keys, (5) delete uploaded clinical document files from disk, (6) run a post-deletion verification scan confirming no patient data remains in active tables, caches, or file storage (AC-4). Audit logs referencing the patient are retained per DR-016 (7-year retention) with the patient identity anonymized. Every deletion step is recorded in the audit trail (AC-3).

## Dependent Tasks

- US_008 — Requires all domain entities (Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode).
- US_064 — Requires audit log system for recording deletion events.
- US_094 task_001 — Reuses DataAccessRequest entity (with RequestType = "DataDeletion").

## Impacted Components

- **NEW** `src/UPACIP.Service/PatientRights/PatientDataDeletionService.cs` — IPatientDataDeletionService: multi-phase deletion pipeline
- **NEW** `src/UPACIP.Service/PatientRights/Deletion/PendingAppointmentHandler.cs` — Cancels pending appointments and notifies staff
- **NEW** `src/UPACIP.Service/PatientRights/Deletion/SharedDataAnonymizer.cs` — Anonymizes shared/consolidated data points
- **NEW** `src/UPACIP.Service/PatientRights/Deletion/CacheCleanupService.cs` — Purges patient-related Redis cache keys
- **NEW** `src/UPACIP.Service/PatientRights/Deletion/FileCleanupService.cs` — Deletes clinical document files from disk
- **NEW** `src/UPACIP.Service/PatientRights/Deletion/DeletionVerificationService.cs` — Post-deletion scan across tables, cache, files
- **NEW** `src/UPACIP.Service/PatientRights/Models/DeletionResult.cs` — DTO: per-phase results, verification status
- **MODIFY** `src/UPACIP.Api/Controllers/PatientRightsController.cs` — Add deletion request and verification endpoints
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register deletion service and sub-services

## Implementation Plan

1. **Implement `PendingAppointmentHandler` (edge case 1)**: Create in `src/UPACIP.Service/PatientRights/Deletion/PendingAppointmentHandler.cs`. Constructor injection of `ApplicationDbContext`, `ILogger<PendingAppointmentHandler>`.

   **`Task<int> CancelPendingAppointmentsAsync(Guid patientId, CancellationToken ct)`**:
   - Query `context.Appointments.Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.Scheduled)`.
   - For each pending appointment:
     - Set `Status = AppointmentStatus.Cancelled`.
     - Set `updated_at = DateTime.UtcNow`.
     - Log: `Log.Information("DELETION_APPOINTMENT_CANCELLED: AppointmentId={Id}, PatientId={PatientId}")`.
   - Call `SaveChangesAsync`.
   - Return the count of cancelled appointments.
   - Note: Staff notification is handled by the existing notification system — the cancellation status change triggers the standard appointment cancellation notification workflow.

2. **Implement `SharedDataAnonymizer` (edge case 2)**: Create in `src/UPACIP.Service/PatientRights/Deletion/SharedDataAnonymizer.cs`. Constructor injection of `ApplicationDbContext`, `ILogger<SharedDataAnonymizer>`.

   **`Task<int> AnonymizeSharedDataAsync(Guid patientId, CancellationToken ct)`**:
   - Identify data that is shared with other entities or consolidated views:
     - **ExtractedData linked to ClinicalDocuments uploaded by staff**: If `ClinicalDocument.uploader_user_id != null` (uploaded by staff, not the patient), the extracted data may be referenced in consolidated clinical views. Anonymize rather than delete:
       - Set `data_content` JSON field to replace patient-identifiable values with `"[ANONYMIZED]"`.
       - Retain clinical data structure for aggregate analytics.
     - **MedicalCodes approved by staff**: If `MedicalCode.approved_by_user_id != null`, the code approval record has a shared context. Anonymize the `patient_id` FK by setting it to a sentinel "DELETED_PATIENT" UUID rather than null (preserves referential integrity for staff audit trail).
   - Log: `Log.Information("DELETION_DATA_ANONYMIZED: PatientId={PatientId}, RecordsAnonymized={Count}")`.
   - Return the count of anonymized records.

3. **Implement `CacheCleanupService`**: Create in `src/UPACIP.Service/PatientRights/Deletion/CacheCleanupService.cs`. Constructor injection of `IConnectionMultiplexer` (StackExchange.Redis), `ILogger<CacheCleanupService>`.

   **`Task<int> PurgePatientCacheAsync(Guid patientId, CancellationToken ct)`**:
   - Define patient-related cache key patterns:
     - `patient:{patientId}:*` — patient profile cache.
     - `appointments:{patientId}:*` — appointment slot cache.
     - `intake:{patientId}:*` — intake data cache.
   - Use Redis `SCAN` command (not `KEYS` — SCAN is non-blocking) to find matching keys.
   - Delete all matched keys using `KeyDeleteAsync` batch.
   - Log: `Log.Information("DELETION_CACHE_PURGED: PatientId={PatientId}, KeysDeleted={Count}")`.
   - Return the count of deleted cache keys.
   - If Redis is unavailable (connection error), log warning and continue — cache entries will expire via TTL (5-min per NFR-030).

4. **Implement `FileCleanupService`**: Create in `src/UPACIP.Service/PatientRights/Deletion/FileCleanupService.cs`. Constructor injection of `ApplicationDbContext`, `ILogger<FileCleanupService>`.

   **`Task<int> DeletePatientFilesAsync(Guid patientId, CancellationToken ct)`**:
   - Query `context.ClinicalDocuments.Where(d => d.PatientId == patientId).Select(d => d.FilePath)`.
   - For each file path:
     - Verify the path is within the allowed documents directory (prevent path traversal — resolve canonical path and ensure it starts with the configured documents root).
     - If file exists, delete it: `File.Delete(filePath)`.
     - Log: `Log.Information("DELETION_FILE_REMOVED: FilePath={Path}")`.
   - Also check for any export files from data access requests: delete the export ZIP if it exists.
   - Return the count of deleted files.
   - If a file is locked or inaccessible, log warning and add to the result as a non-fatal issue.

5. **Implement `DeletionVerificationService` (AC-4)**: Create in `src/UPACIP.Service/PatientRights/Deletion/DeletionVerificationService.cs`. Constructor injection of `ApplicationDbContext`, `IConnectionMultiplexer`, `ILogger<DeletionVerificationService>`.

   **`Task<DeletionVerificationResult> VerifyDeletionAsync(Guid patientId, CancellationToken ct)`**:
   - (a) **Active table scan**: Query each entity table for any remaining records with the patient ID:
     - `context.Patients.AnyAsync(p => p.PatientId == patientId && p.DeletedAt == null)` — should be false.
     - `context.Appointments.AnyAsync(a => a.PatientId == patientId)` — should be false (hard-deleted).
     - `context.IntakeData.AnyAsync(i => i.PatientId == patientId)` — should be false.
     - `context.ClinicalDocuments.AnyAsync(d => d.PatientId == patientId)` — should be false.
     - `context.MedicalCodes.AnyAsync(m => m.PatientId == patientId && m.PatientId != DELETED_PATIENT_SENTINEL)` — should be false (anonymized records excluded).
   - (b) **Cache scan**: Use Redis SCAN for `patient:{patientId}:*` — should return zero keys.
   - (c) **File storage scan**: Re-check file paths from step 4 — all should be deleted.
   - (d) **Audit log retention check**: Verify `context.AuditLogs.AnyAsync(a => a.UserId == patientUserId)` — MUST return true (audit logs retained per DR-016). Verify the audit log entries have been anonymized (patient name replaced with `[DELETED_PATIENT]`).
   - Return `DeletionVerificationResult`: `bool FullyDeleted`, `List<string> RemainingDataLocations` (if any), `bool AuditLogsRetained`, `int TablesVerified`, `int CacheKeysRemaining`, `int FilesRemaining`.

6. **Implement `IPatientDataDeletionService`**: Create in `src/UPACIP.Service/PatientRights/PatientDataDeletionService.cs`. Constructor injection of `PendingAppointmentHandler`, `SharedDataAnonymizer`, `CacheCleanupService`, `FileCleanupService`, `DeletionVerificationService`, `ApplicationDbContext`, `ILogger<PatientDataDeletionService>`.

   **`Task<DataAccessRequest> SubmitDeletionRequestAsync(Guid patientId, string requestedBy, CancellationToken ct)`**:
   - Create `DataAccessRequest` with `RequestType = "DataDeletion"`, `Status = "Submitted"`, `DeadlineUtc = DateTime.UtcNow.AddDays(30)`.
   - Record audit log: action = `data_delete`, resource_type = `DataDeletionRequest`.
   - Return the request.

   **`Task<DeletionResult> ProcessDeletionAsync(Guid requestId, CancellationToken ct)`**:
   Execute the six-phase deletion pipeline within a transaction where possible:

   - **Phase 1 — Cancel pending appointments** (edge case 1):
     - Call `PendingAppointmentHandler.CancelPendingAppointmentsAsync()`.
     - Record cancelled count in `DeletionResult`.

   - **Phase 2 — Anonymize shared data** (edge case 2):
     - Call `SharedDataAnonymizer.AnonymizeSharedDataAsync()`.
     - Record anonymized count in `DeletionResult`.

   - **Phase 3 — Delete dependent entities**:
     - Delete in FK-safe order (children first):
       1. `NotificationLogs` (via Appointment FK) — `context.NotificationLogs.Where(n => n.Appointment.PatientId == patientId)`.
       2. `QueueEntries` (via Appointment FK) — `context.QueueEntries.Where(q => q.Appointment.PatientId == patientId)`.
       3. `ExtractedData` (via ClinicalDocument FK) — non-anonymized records only.
       4. `MedicalCodes` — non-anonymized records only.
       5. `ClinicalDocuments`.
       6. `IntakeData`.
       7. `Appointments`.
     - Call `SaveChangesAsync` after each entity type for transactional safety.
     - Record deleted counts per entity in `DeletionResult`.

   - **Phase 4 — Soft-delete Patient record** (DR-021):
     - Set `Patient.DeletedAt = DateTime.UtcNow`.
     - Anonymize PII: set `Email = "deleted_{patientId}@removed.local"`, `FullName = "[DELETED]"`, `PhoneNumber = null`, `EmergencyContact = null`, `PasswordHash = null`.
     - Call `SaveChangesAsync`.

   - **Phase 5 — Purge cache and files**:
     - Call `CacheCleanupService.PurgePatientCacheAsync()`.
     - Call `FileCleanupService.DeletePatientFilesAsync()`.

   - **Phase 6 — Anonymize audit log references**:
     - Update `AuditLog` entries where `UserId` matches the patient's user ID:
       - Do NOT delete (retained per DR-016 for 7 years).
       - Replace any patient-identifiable information in the log with `[DELETED_PATIENT_{patientId}]`.

   - **Verification**: Call `DeletionVerificationService.VerifyDeletionAsync()`.
   - Update `DataAccessRequest`: `Status = "Completed"` (or "Failed" if verification fails), `CompletedAtUtc`.
   - Record audit log: action = `data_delete`, resource_type = `PatientData`, detail includes deletion summary.
   - Return `DeletionResult`.

7. **Create `DeletionResult` DTO**: Create in `src/UPACIP.Service/PatientRights/Models/DeletionResult.cs`:
   - `Guid RequestId`.
   - `Guid PatientId`.
   - `string Status` — "Completed", "CompletedWithWarnings", "Failed".
   - `int AppointmentsCancelled` — phase 1 count.
   - `int RecordsAnonymized` — phase 2 count.
   - `Dictionary<string, int> EntitiesDeleted` — phase 3 per-entity counts (e.g., `{"Appointments": 5, "IntakeData": 2, ...}`).
   - `bool PatientSoftDeleted` — phase 4 success.
   - `int CacheKeysDeleted` — phase 5 cache count.
   - `int FilesDeleted` — phase 5 file count.
   - `bool AuditLogsAnonymized` — phase 6 success.
   - `DeletionVerificationResult Verification` — phase 6 verification results (AC-4).
   - `List<string> Warnings` — non-fatal issues (locked files, unavailable cache).
   - `TimeSpan Duration`.

8. **Extend `PatientRightsController` with deletion endpoints**: Add to `src/UPACIP.Api/Controllers/PatientRightsController.cs` (created in task_001).

   **POST `/api/patients/{patientId}/data-deletion-request`** — Submit deletion request:
   - Authorization: Patient can request their own deletion. Admin can submit on behalf.
   - Call `SubmitDeletionRequestAsync(patientId, currentUser, ct)`.
   - Return `202 Accepted` with `{ requestId, deadline, statusUrl }`.

   **GET `/api/patients/{patientId}/data-deletion-request/{requestId}`** — Check deletion status:
   - Authorization: Patient or Admin.
   - Return `DataAccessRequest` with status and deadline.

   **POST `/api/admin/data-deletion-requests/{requestId}/process`** — Admin triggers deletion:
   - Authorization: Admin only.
   - Call `ProcessDeletionAsync(requestId, ct)`.
   - Return `200 OK` with `DeletionResult` including per-phase counts and verification result.

   **GET `/api/admin/data-deletion-requests/{requestId}/verify`** — Re-run verification:
   - Authorization: Admin only.
   - Call `DeletionVerificationService.VerifyDeletionAsync()`.
   - Return `DeletionVerificationResult`.

   **GET `/api/admin/data-deletion-requests`** — List deletion requests:
   - Authorization: Admin only.
   - Return paginated list. Support `status`, `overdue` filters.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── PatientRightsController.cs            ← from task_001
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/
│   │   ├── Import/
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   └── PatientRights/
│   │       ├── PatientDataExportService.cs           ← from task_001
│   │       ├── DataCollectors/
│   │       │   ├── PatientProfileCollector.cs         ← from task_001
│   │       │   ├── AppointmentCollector.cs            ← from task_001
│   │       │   └── ClinicalDataCollector.cs           ← from task_001
│   │       ├── Export/
│   │       │   ├── JsonExportGenerator.cs             ← from task_001
│   │       │   └── PdfExportGenerator.cs              ← from task_001
│   │       └── Models/
│   │           ├── DataAccessRequest.cs               ← from task_001
│   │           └── PatientDataPackage.cs              ← from task_001
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_094 task_001 (data export service) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/PatientRights/PatientDataDeletionService.cs | IPatientDataDeletionService: six-phase deletion pipeline |
| CREATE | src/UPACIP.Service/PatientRights/Deletion/PendingAppointmentHandler.cs | Cancels pending appointments for deletion targets |
| CREATE | src/UPACIP.Service/PatientRights/Deletion/SharedDataAnonymizer.cs | Anonymizes shared/consolidated data points |
| CREATE | src/UPACIP.Service/PatientRights/Deletion/CacheCleanupService.cs | Purges patient-related Redis cache keys via SCAN |
| CREATE | src/UPACIP.Service/PatientRights/Deletion/FileCleanupService.cs | Deletes clinical document files from disk |
| CREATE | src/UPACIP.Service/PatientRights/Deletion/DeletionVerificationService.cs | Post-deletion scan across tables, cache, files |
| CREATE | src/UPACIP.Service/PatientRights/Models/DeletionResult.cs | DTO: per-phase counts, verification status, warnings |
| MODIFY | src/UPACIP.Api/Controllers/PatientRightsController.cs | Add deletion request, process, verify, list endpoints |
| MODIFY | src/UPACIP.Api/Program.cs | Register deletion service and five sub-services |

## External References

- [HIPAA §164.526 — Right to Amend / §164.524 Right of Access](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/access/index.html)
- [EF Core Cascade Delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete)
- [StackExchange.Redis SCAN Command](https://stackexchange.github.io/StackExchange.Redis/KeysScan.html)
- [File.Delete — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.delete)
- [Path Traversal Prevention — OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Path_Traversal_Cheat_Sheet.html)

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
- [ ] Deletion request creates with 30-day deadline (AC-3, NFR-045)
- [ ] Pending appointments are cancelled before deletion proceeds (edge case 1)
- [ ] Shared/consolidated data is anonymized instead of deleted (edge case 2)
- [ ] All dependent entities are hard-deleted in FK-safe order (AC-3)
- [ ] Patient record is soft-deleted with PII anonymized (DR-021)
- [ ] Redis cache keys matching patient ID are purged (AC-4)
- [ ] Clinical document files are deleted from disk with path traversal prevention (AC-4)
- [ ] Post-deletion verification confirms no data in active tables, cache, or files (AC-4)
- [ ] Audit logs are retained but anonymized (DR-016)
- [ ] Audit log records each deletion phase (AC-3)

## Implementation Checklist

- [ ] Implement PendingAppointmentHandler to cancel scheduled appointments and trigger notifications
- [ ] Implement SharedDataAnonymizer for staff-linked extracted data and approved medical codes
- [ ] Implement CacheCleanupService with Redis SCAN-based patient key purge
- [ ] Implement FileCleanupService with path traversal prevention and locked file handling
- [ ] Implement DeletionVerificationService scanning tables, cache, and file storage
- [ ] Implement PatientDataDeletionService with six-phase pipeline and DeletionResult
- [ ] Extend PatientRightsController with deletion submit, process, verify, and list endpoints
- [ ] Register all deletion sub-services in Program.cs DI container
