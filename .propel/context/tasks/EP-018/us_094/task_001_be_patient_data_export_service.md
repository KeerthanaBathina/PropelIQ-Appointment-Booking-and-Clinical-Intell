# Task - task_001_be_patient_data_export_service

## Requirement Reference

- User Story: us_094
- Story Location: .propel/context/tasks/EP-018/us_094/us_094.md
- Acceptance Criteria:
  - AC-1: Given a patient requests data access, When the request is submitted, Then the system generates a comprehensive data export (appointments, intake, documents, extracted data, medical codes) within 30 days.
  - AC-2: Given the data export is generated, When it is delivered, Then the export is in a human-readable format (PDF + JSON) with all data categories clearly labeled.

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
| Library | QuestPDF | 2024.x |
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

Implement the HIPAA right-to-access patient data export service (NFR-044) that collects all patient data across six entity categories — Patient profile, Appointments, IntakeData, ClinicalDocuments, ExtractedData, and MedicalCodes — and generates a dual-format export (JSON for machine-readability and PDF for human-readability) with clearly labeled data categories (AC-1, AC-2). The service tracks export requests via a `DataAccessRequest` entity with a 30-day SLA deadline, supports background processing for large datasets, and delivers the export as a downloadable ZIP archive. An admin-only API manages request submission and fulfillment, while a patient-facing endpoint allows patients to submit access requests and download completed exports. Each export is logged in the audit trail for HIPAA compliance evidence.

## Dependent Tasks

- US_008 — Requires all domain entities (Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode).
- US_064 — Requires audit log system for recording data access request events.

## Impacted Components

- **NEW** `src/UPACIP.Service/PatientRights/PatientDataExportService.cs` — IPatientDataExportService: collects and exports patient data across all categories
- **NEW** `src/UPACIP.Service/PatientRights/DataCollectors/PatientProfileCollector.cs` — Collects Patient entity fields
- **NEW** `src/UPACIP.Service/PatientRights/DataCollectors/AppointmentCollector.cs` — Collects all appointments with queue entries and notification logs
- **NEW** `src/UPACIP.Service/PatientRights/DataCollectors/ClinicalDataCollector.cs` — Collects intake data, clinical documents, extracted data, medical codes
- **NEW** `src/UPACIP.Service/PatientRights/Export/JsonExportGenerator.cs` — Generates structured JSON export with labeled categories
- **NEW** `src/UPACIP.Service/PatientRights/Export/PdfExportGenerator.cs` — Generates human-readable PDF export using QuestPDF
- **NEW** `src/UPACIP.Service/PatientRights/Models/DataAccessRequest.cs` — Entity: tracks export requests with 30-day SLA
- **NEW** `src/UPACIP.Service/PatientRights/Models/PatientDataPackage.cs` — DTO: aggregated patient data across all categories
- **NEW** `src/UPACIP.Api/Controllers/PatientRightsController.cs` — API: submit access request, check status, download export
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<DataAccessRequest>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register PatientDataExportService, collectors, generators

## Implementation Plan

1. **Create `DataAccessRequest` entity**: Create in `src/UPACIP.Service/PatientRights/Models/DataAccessRequest.cs`:
   - `Guid Id` (PK).
   - `Guid PatientId` (FK to Patient).
   - `string RequestType` — "DataAccess" (this task) or "DataDeletion" (task_002).
   - `string Status` — "Submitted", "Processing", "Completed", "Failed".
   - `DateTime RequestedAtUtc` — when the patient submitted the request.
   - `DateTime DeadlineUtc` — `RequestedAtUtc + 30 days` (NFR-044 SLA).
   - `DateTime? CompletedAtUtc` — when the export was generated.
   - `string? ExportFilePath` — path to the generated ZIP archive.
   - `long? ExportFileSizeBytes` — size of the generated archive.
   - `string? FailureReason` — error details if export failed.
   - `string RequestedBy` — patient email or admin identity.
   - `string? ProcessedBy` — admin who fulfilled the request (null for auto-processed).
   Add `DbSet<DataAccessRequest>` to `ApplicationDbContext` with indexes on `PatientId` and `Status`.

2. **Create `PatientDataPackage` DTO**: Create in `src/UPACIP.Service/PatientRights/Models/PatientDataPackage.cs`. This aggregates all patient data for export:
   - `PatientProfileData Profile` — patient demographics (full_name, email, date_of_birth, phone_number, emergency_contact, created_at).
   - `List<AppointmentData> Appointments` — all appointments with status, time, walk-in flag. Each includes nested `List<QueueEntryData>` and `List<NotificationLogData>`.
   - `List<IntakeDataRecord> IntakeRecords` — intake method, mandatory/optional fields, insurance info.
   - `List<ClinicalDocumentData> ClinicalDocuments` — document category, upload date, processing status. Each includes nested `List<ExtractedDataRecord>` (data type, content, confidence score, source attribution).
   - `List<MedicalCodeData> MedicalCodes` — code type, value, description, justification, AI confidence score.
   - `DateTime ExportedAtUtc` — timestamp of export generation.
   - `string ExportVersion` — "1.0" for schema versioning.
   All sub-DTOs exclude internal fields (password_hash, version, deleted_at) — export only patient-facing data.

3. **Implement data collectors**: Create in `src/UPACIP.Service/PatientRights/DataCollectors/`. Each collector is a focused service that queries a specific data category.

   **`PatientProfileCollector`**: Constructor injection of `ApplicationDbContext`.
   - `Task<PatientProfileData> CollectAsync(Guid patientId, CancellationToken ct)`:
     - Query `context.Patients.Where(p => p.PatientId == patientId)`.
     - Map to `PatientProfileData` excluding `password_hash` and `deleted_at`.
     - Throw `PatientNotFoundException` if not found.

   **`AppointmentCollector`**: Constructor injection of `ApplicationDbContext`.
   - `Task<List<AppointmentData>> CollectAsync(Guid patientId, CancellationToken ct)`:
     - Query `context.Appointments.Where(a => a.PatientId == patientId).Include(a => a.QueueEntries).Include(a => a.NotificationLogs)`.
     - Map all appointments including nested queue entries and notification history.
     - Return empty list if no appointments (valid scenario).

   **`ClinicalDataCollector`**: Constructor injection of `ApplicationDbContext`.
   - `Task<ClinicalDataPackage> CollectAsync(Guid patientId, CancellationToken ct)`:
     - Query `context.IntakeData.Where(i => i.PatientId == patientId)`.
     - Query `context.ClinicalDocuments.Where(d => d.PatientId == patientId).Include(d => d.ExtractedData)`.
     - Query `context.MedicalCodes.Where(m => m.PatientId == patientId)`.
     - Return aggregated `ClinicalDataPackage` with all three collections.

4. **Implement `JsonExportGenerator` (AC-2 — JSON format)**: Create in `src/UPACIP.Service/PatientRights/Export/JsonExportGenerator.cs`. Constructor injection of `ILogger<JsonExportGenerator>`.

   **`Task<byte[]> GenerateAsync(PatientDataPackage data, CancellationToken ct)`**:
   - Serialize `PatientDataPackage` to JSON using `System.Text.Json.JsonSerializer` with `JsonSerializerOptions`:
     - `WriteIndented = true` — human-readable formatting.
     - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.
     - `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`.
   - Structure the JSON with top-level category labels:
     ```json
     {
       "exportMetadata": { "exportedAt": "...", "version": "1.0", "patientId": "..." },
       "patientProfile": { ... },
       "appointments": [ ... ],
       "intakeRecords": [ ... ],
       "clinicalDocuments": [ ... ],
       "medicalCodes": [ ... ]
     }
     ```
   - Return UTF-8 encoded byte array.

5. **Implement `PdfExportGenerator` (AC-2 — PDF format)**: Create in `src/UPACIP.Service/PatientRights/Export/PdfExportGenerator.cs`. Constructor injection of `ILogger<PdfExportGenerator>`.

   Uses QuestPDF library (open-source .NET PDF generator, MIT license) to create a structured, human-readable PDF:

   **`Task<byte[]> GenerateAsync(PatientDataPackage data, CancellationToken ct)`**:
   - (a) **Cover page**: Title "Patient Data Export Report", patient name, export date, HIPAA reference statement: "This document is provided in accordance with HIPAA §164.524 — Right of Access".
   - (b) **Table of Contents**: Links to each data category section.
   - (c) **Section 1 — Patient Profile**: Table with demographic fields (Name, Email, DOB, Phone, Emergency Contact, Account Created).
   - (d) **Section 2 — Appointments**: Table listing all appointments with columns: Date/Time, Status, Walk-In, Queue Wait Time. Each appointment links to its notification history.
   - (e) **Section 3 — Intake Records**: For each intake record, display intake method and structured JSON fields rendered as key-value tables.
   - (f) **Section 4 — Clinical Documents**: Table of uploaded documents with category, upload date, processing status. Each document's extracted data shown as sub-table (data type, content summary, confidence score).
   - (g) **Section 5 — Medical Codes**: Table with code type (ICD-10/CPT), code value, description, justification, AI confidence.
   - (h) **Footer**: Page numbers, export timestamp, "Confidential — Protected Health Information".
   - Return PDF as byte array.

6. **Implement `IPatientDataExportService`**: Create in `src/UPACIP.Service/PatientRights/PatientDataExportService.cs`. Constructor injection of `PatientProfileCollector`, `AppointmentCollector`, `ClinicalDataCollector`, `JsonExportGenerator`, `PdfExportGenerator`, `ApplicationDbContext`, `ILogger<PatientDataExportService>`.

   **`Task<DataAccessRequest> SubmitRequestAsync(Guid patientId, string requestedBy, CancellationToken ct)`**:
   - Create `DataAccessRequest` with `Status = "Submitted"`, `DeadlineUtc = DateTime.UtcNow.AddDays(30)`.
   - Persist and return the request.
   - Log: `Log.Information("DATA_ACCESS_REQUEST_SUBMITTED: PatientId={PatientId}, Deadline={Deadline}")`.
   - Record audit log: action = `data_access`, resource_type = `DataAccessRequest`.

   **`Task<DataAccessRequest> ProcessRequestAsync(Guid requestId, CancellationToken ct)`**:
   - (a) Load the `DataAccessRequest`. Set `Status = "Processing"`.
   - (b) Collect data from all three collectors into a `PatientDataPackage`.
   - (c) Generate JSON export via `JsonExportGenerator`.
   - (d) Generate PDF export via `PdfExportGenerator`.
   - (e) Create a ZIP archive containing both files: `patient_data_{patientId}.json` and `patient_data_{patientId}.pdf`.
   - (f) Save the ZIP to a secure export directory (`D:\Exports\PatientData\{requestId}\`).
   - (g) Update request: `Status = "Completed"`, `CompletedAtUtc`, `ExportFilePath`, `ExportFileSizeBytes`.
   - (h) Log: `Log.Information("DATA_ACCESS_REQUEST_COMPLETED: RequestId={RequestId}, SizeBytes={Size}")`.
   - (i) On failure, set `Status = "Failed"`, `FailureReason`, log error.

   **`Task<Stream> DownloadExportAsync(Guid requestId, Guid patientId, CancellationToken ct)`**:
   - Verify the request belongs to the requesting patient (authorization check).
   - Verify `Status = "Completed"` and file exists.
   - Return the ZIP file as a stream for download.
   - Record audit log: action = `data_access`, resource_type = `PatientDataExport`.

7. **Implement `PatientRightsController`**: Create in `src/UPACIP.Api/Controllers/PatientRightsController.cs`.

   **POST `/api/patients/{patientId}/data-access-request`** — Submit data access request:
   - Authorization: Patient can request their own data (`patientId` must match authenticated user). Admin can submit on behalf.
   - Call `SubmitRequestAsync(patientId, currentUser, ct)`.
   - Return `202 Accepted` with `{ requestId, deadline, statusUrl }`.

   **GET `/api/patients/{patientId}/data-access-request/{requestId}`** — Check request status:
   - Authorization: Patient or Admin.
   - Return `DataAccessRequest` with status, deadline, completion date.

   **GET `/api/patients/{patientId}/data-access-request/{requestId}/download`** — Download completed export:
   - Authorization: Patient or Admin.
   - Return ZIP file with `Content-Type: application/zip`, `Content-Disposition: attachment; filename="patient_data_export.zip"`.
   - Return `404 Not Found` if not completed or file missing.

   **POST `/api/admin/data-access-requests/{requestId}/process`** — Admin triggers export generation:
   - Authorization: Admin only.
   - Call `ProcessRequestAsync(requestId, ct)`.
   - Return `200 OK` with `DataAccessRequest`.

   **GET `/api/admin/data-access-requests`** — List all pending requests:
   - Authorization: Admin only.
   - Return paginated list filtered by status. Highlight requests approaching 30-day deadline.
   - Support query: `status`, `overdue` (boolean — past deadline and not completed).

8. **Register services in DI**: In `Program.cs`:
   - `services.AddScoped<PatientProfileCollector>()`.
   - `services.AddScoped<AppointmentCollector>()`.
   - `services.AddScoped<ClinicalDataCollector>()`.
   - `services.AddScoped<JsonExportGenerator>()`.
   - `services.AddScoped<PdfExportGenerator>()`.
   - `services.AddScoped<IPatientDataExportService, PatientDataExportService>()`.
   - Configure QuestPDF license: `QuestPDF.Settings.License = LicenseType.Community`.

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
│   │   ├── Compliance/
│   │   ├── Import/
│   │   ├── Migration/
│   │   └── Monitoring/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs                   ← from US_008
│       ├── Entities/
│       │   ├── Patient.cs
│       │   ├── Appointment.cs
│       │   ├── IntakeData.cs
│       │   ├── ClinicalDocument.cs
│       │   ├── ExtractedData.cs
│       │   ├── MedicalCode.cs
│       │   ├── QueueEntry.cs
│       │   ├── NotificationLog.cs
│       │   └── AuditLog.cs
│       ├── Configurations/
│       └── Migrations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (domain entities) and US_064 (audit logging) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/PatientRights/PatientDataExportService.cs | IPatientDataExportService: data collection, JSON+PDF generation, ZIP packaging |
| CREATE | src/UPACIP.Service/PatientRights/DataCollectors/PatientProfileCollector.cs | Collects patient demographics excluding sensitive fields |
| CREATE | src/UPACIP.Service/PatientRights/DataCollectors/AppointmentCollector.cs | Collects appointments with queue entries and notification logs |
| CREATE | src/UPACIP.Service/PatientRights/DataCollectors/ClinicalDataCollector.cs | Collects intake data, clinical documents, extracted data, medical codes |
| CREATE | src/UPACIP.Service/PatientRights/Export/JsonExportGenerator.cs | Generates structured JSON with labeled categories |
| CREATE | src/UPACIP.Service/PatientRights/Export/PdfExportGenerator.cs | Generates human-readable PDF via QuestPDF with sections and TOC |
| CREATE | src/UPACIP.Service/PatientRights/Models/DataAccessRequest.cs | Entity: tracks export requests with 30-day SLA deadline |
| CREATE | src/UPACIP.Service/PatientRights/Models/PatientDataPackage.cs | DTO: aggregated patient data across all six categories |
| CREATE | src/UPACIP.Api/Controllers/PatientRightsController.cs | API: submit, status, download, admin process, admin list |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<DataAccessRequest> |
| MODIFY | src/UPACIP.Api/Program.cs | Register export service, collectors, generators, QuestPDF license |

## External References

- [HIPAA §164.524 — Right of Access](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/access/index.html)
- [QuestPDF — .NET PDF Generation Library](https://www.questpdf.com/getting-started.html)
- [System.Text.Json Serialization — .NET 8](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/how-to)
- [ZipArchive — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive)
- [File Download in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads#file-download)

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
- [ ] Data access request creates with 30-day deadline (AC-1, NFR-044)
- [ ] Export collects all six data categories: profile, appointments, intake, documents, extracted data, medical codes (AC-1)
- [ ] JSON export is well-structured with labeled categories and camelCase naming (AC-2)
- [ ] PDF export contains cover page, TOC, and five labeled sections (AC-2)
- [ ] ZIP archive contains both JSON and PDF files
- [ ] Patient can only download their own export (authorization check)
- [ ] Admin can list pending requests and filter by overdue status
- [ ] Audit log records data access request submission and export download
- [ ] Internal fields (password_hash, deleted_at, version) are excluded from export

## Implementation Checklist

- [ ] Create DataAccessRequest entity with 30-day SLA deadline and DbSet registration
- [ ] Create PatientDataPackage DTO with sub-DTOs for all six entity categories
- [ ] Implement PatientProfileCollector, AppointmentCollector, ClinicalDataCollector
- [ ] Implement JsonExportGenerator with labeled categories and indented formatting
- [ ] Implement PdfExportGenerator with QuestPDF (cover page, TOC, five sections)
- [ ] Implement PatientDataExportService with submit, process, and download operations
- [ ] Implement PatientRightsController with patient and admin endpoints
- [ ] Register all services and configure QuestPDF community license in Program.cs
