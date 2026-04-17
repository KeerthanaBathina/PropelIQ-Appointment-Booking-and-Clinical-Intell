# Task - task_002_be_csv_import_api

## Requirement Reference

- User Story: us_092
- Story Location: .propel/context/tasks/EP-017/us_092/us_092.md
- Acceptance Criteria:
  - AC-2: Given validation passes, When the import runs, Then the system creates records from each valid row and reports the count of successfully imported records.
  - AC-3: Given validation errors exist, When the import completes, Then the system produces a detailed error report listing failed rows with specific field-level error messages.
- Edge Case:
  - What happens when a CSV file contains 100,000+ rows? System processes in batches of 1000 rows with progress reporting and the ability to pause/resume.

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

Implement the admin-only CSV import API controller with background job processing for large files (100K+ rows), progress tracking, and import history (edge case 1). The API accepts multipart file uploads, validates the file size and entity type, and delegates to `ICsvImportEngine` (from task_001) for processing. For small files (under 10,000 rows), the import executes synchronously and returns the `ImportResult` directly. For large files (10,000+ rows), the import is queued as a background job via a `CsvImportBackgroundService`, returning a `202 Accepted` with an import job ID that the admin can poll for progress. The progress tracking system reports the current batch number, processed rows, success/error counts, and estimated time remaining. Import history is persisted to an `ImportLog` entity for audit trail — recording who uploaded what, when, how many rows succeeded/failed, and the error report. The API also provides a preview endpoint that validates headers and the first 10 rows without persisting any data, enabling admins to verify their CSV format before committing to a full import.

## Dependent Tasks

- US_092 task_001_be_csv_import_engine — Requires ICsvImportEngine, ICsvParser, import profiles, ImportResult, ImportOptions.

## Impacted Components

- **NEW** `src/UPACIP.Api/Controllers/ImportController.cs` — API: POST upload, GET progress, GET history, POST preview (admin-only)
- **NEW** `src/UPACIP.Service/Import/CsvImportBackgroundService.cs` — BackgroundService: process large import jobs from queue
- **NEW** `src/UPACIP.Service/Import/Models/ImportJob.cs` — DTO: import job with status, progress, file path
- **NEW** `src/UPACIP.Service/Import/Models/ImportLog.cs` — Entity: import history audit trail
- **NEW** `src/UPACIP.Service/Import/Models/ImportProgress.cs` — DTO: current batch, processed rows, estimated time
- **MODIFY** `src/UPACIP.Service/Import/CsvImportEngine.cs` — Add progress callback support for batch processing
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<ImportLog>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register CsvImportBackgroundService, bind additional config

## Implementation Plan

1. **Create `ImportLog` entity**: Create in `src/UPACIP.Service/Import/Models/ImportLog.cs`:
   - `Guid Id` (PK).
   - `string EntityType` — which entity was imported ("Patient", "Appointment", "User").
   - `string FileName` — original uploaded filename.
   - `long FileSizeBytes` — uploaded file size.
   - `int TotalRows` — total data rows in the CSV.
   - `int SuccessCount` — rows successfully imported.
   - `int ErrorCount` — rows that failed validation.
   - `int DuplicateCount` — rows skipped due to duplicates.
   - `string Status` — "Completed", "CompletedWithErrors", "Failed", "InProgress", "Queued".
   - `string? ErrorReportJson` — JSON-serialized error details (top 100 errors to limit size; full report stored as file for large imports).
   - `string PerformedBy` — admin user who triggered the import.
   - `TimeSpan Duration` — total processing time.
   - `DateTime CreatedAtUtc` — when the import was initiated.
   - `DateTime? CompletedAtUtc` — when the import finished.
   Add `DbSet<ImportLog>` to `ApplicationDbContext` with an index on `CreatedAtUtc`.

2. **Create `ImportJob` and `ImportProgress` DTOs**: Create in `src/UPACIP.Service/Import/Models/`:

   **`ImportJob`**:
   - `Guid JobId` — unique identifier for the background import job.
   - `string EntityType` — target entity type.
   - `string FilePath` — temporary file path on disk.
   - `string FileName` — original filename.
   - `string PerformedBy` — admin identity.
   - `string Status` — "Queued", "InProgress", "Completed", "Failed".
   - `ImportProgress? Progress` — current progress (null until started).
   - `ImportResult? Result` — final result (null until completed).

   **`ImportProgress`**:
   - `int CurrentBatch` — which batch is currently being processed.
   - `int TotalEstimatedBatches` — estimated total batches (computed from file size heuristic).
   - `int ProcessedRows` — rows processed so far.
   - `int SuccessSoFar` — successful rows so far.
   - `int ErrorsSoFar` — error rows so far.
   - `int DuplicatesSoFar` — duplicates skipped so far.
   - `double PercentComplete` — estimated completion percentage.
   - `TimeSpan? EstimatedTimeRemaining` — computed from average batch processing speed.

3. **Implement `ImportController` API endpoints**: Create in `src/UPACIP.Api/Controllers/ImportController.cs`. All endpoints require admin role authorization.

   **POST `/api/admin/import/{entityType}`** — Upload and import CSV:
   - Accept `IFormFile` via multipart form-data.
   - Validate: file is not null, file size ≤ `MaxFileSizeBytes`, `entityType` is in `AllowedEntityTypes`, file has `.csv` extension.
   - Validate: content type is `text/csv` or `application/octet-stream` (some browsers send generic type).
   - Estimate row count from file size (heuristic: average 200 bytes per row).
   - If estimated rows ≤ 10,000 (synchronous path):
     - Open the file stream, call `ICsvImportEngine.ImportAsync<T>(stream, ct)`.
     - Persist `ImportLog` with results.
     - Return `200 OK` with `ImportResult`.
   - If estimated rows > 10,000 (async path):
     - Save the uploaded file to a temporary directory (`Path.GetTempFileName()` with `.csv` extension).
     - Create an `ImportJob` with `Status = "Queued"`, add to the in-memory job queue (a `ConcurrentDictionary<Guid, ImportJob>`).
     - Return `202 Accepted` with `{ jobId, statusUrl: "/api/admin/import/jobs/{jobId}" }`.

   **GET `/api/admin/import/jobs/{jobId}`** — Check import job progress:
   - Look up the job in the in-memory dictionary.
   - Return `ImportJob` with current `ImportProgress` or final `ImportResult`.
   - If job not found, return `404 Not Found`.

   **POST `/api/admin/import/{entityType}/preview`** — Validate CSV format without importing:
   - Accept `IFormFile`.
   - Read only the header row + first 10 data rows.
   - Validate headers against the target profile's `RequiredColumns`.
   - Validate the 10 sample rows and return validation results.
   - Return `200 OK` with `{ headersValid, sampleErrors, detectedDelimiter, estimatedRowCount }`.
   - No data is persisted — this is a dry-run validation.

   **GET `/api/admin/import/history`** — Import history:
   - Return paginated `ImportLog` entries ordered by `CreatedAtUtc` descending.
   - Support query parameters: `entityType` filter, `status` filter, `page`, `pageSize`.

4. **Implement `CsvImportBackgroundService`**: Create in `src/UPACIP.Service/Import/CsvImportBackgroundService.cs` as a `BackgroundService`.

   The service processes import jobs from the in-memory queue:
   - (a) On each loop iteration, check for queued jobs in the `ConcurrentDictionary`.
   - (b) When a job is found, set `Status = "InProgress"`.
   - (c) Open the temporary file, call `ICsvImportEngine.ImportAsync<T>(stream, progressCallback, ct)`.
   - (d) The `progressCallback` is an `Action<ImportProgress>` that updates the job's `Progress` property in real-time, enabling the polling endpoint to return live data.
   - (e) On completion, set `Status = "Completed"`, store the `ImportResult`, persist an `ImportLog`.
   - (f) On failure, set `Status = "Failed"`, persist error details.
   - (g) Delete the temporary file after processing (success or failure).
   - (h) Log: `Log.Information("CSV_IMPORT_COMPLETE: JobId={JobId}, Entity={Entity}, Success={Success}, Errors={Errors}, Duration={Duration}")`.

5. **Add progress callback to `CsvImportEngine`**: Modify the `ImportAsync` method (from task_001) to accept an optional `Action<ImportProgress>? onProgress` parameter. After each batch is persisted, invoke the callback with current counts:
   ```csharp
   onProgress?.Invoke(new ImportProgress
   {
       CurrentBatch = batchNumber,
       ProcessedRows = processedCount,
       SuccessSoFar = successCount,
       ErrorsSoFar = errorCount,
       DuplicatesSoFar = duplicateCount,
       PercentComplete = (double)processedCount / estimatedTotal * 100
   });
   ```
   Compute `EstimatedTimeRemaining` from the average time per batch multiplied by remaining batches.

6. **File upload security**:
   - Validate file extension is `.csv` only — reject `.exe`, `.dll`, `.ps1`, etc.
   - Validate file content starts with printable ASCII/UTF-8 characters (not binary).
   - Limit file size via `ImportOptions.MaxFileSizeBytes` (50 MB default).
   - Temporary files are stored in a secure directory with restricted NTFS permissions.
   - Temporary files are deleted immediately after processing.
   - Admin authentication and authorization are enforced on all endpoints.
   - PII in error reports is redacted (email shows `u***@example.com` pattern).

7. **Error report handling for large imports**: For imports with more than 100 errors, the `ErrorReportJson` in `ImportLog` stores only the first 100 errors. The full error report is written to a file in the import logs directory (`D:\Logs\Import\{jobId}_errors.json`). The API response includes a download link for the full error report file if it exceeds the inline limit.

8. **Register services and bind configuration**: In `Program.cs`: register `services.AddHostedService<CsvImportBackgroundService>()`, register `services.AddSingleton<ConcurrentDictionary<Guid, ImportJob>>()` (shared job store). Add `DbSet<ImportLog>` to `ApplicationDbContext`. Configure multipart form options: `builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = importOptions.MaxFileSizeBytes)`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── ImportController.cs                  ← NEW (this task)
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Migration/
│   │   ├── Import/
│   │   │   ├── CsvImportEngine.cs                   ← from task_001
│   │   │   ├── CsvParser.cs                         ← from task_001
│   │   │   ├── Profiles/
│   │   │   │   ├── PatientImportProfile.cs           ← from task_001
│   │   │   │   ├── AppointmentImportProfile.cs       ← from task_001
│   │   │   │   └── UserImportProfile.cs              ← from task_001
│   │   │   └── Models/
│   │   │       ├── ImportOptions.cs                  ← from task_001
│   │   │       ├── ImportResult.cs                   ← from task_001
│   │   │       └── RowError.cs                       ← from task_001
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

> Assumes US_092 task_001 (CSV import engine) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Controllers/ImportController.cs | API: POST upload, GET progress, GET history, POST preview (admin-only) |
| CREATE | src/UPACIP.Service/Import/CsvImportBackgroundService.cs | BackgroundService: process large import jobs with progress tracking |
| CREATE | src/UPACIP.Service/Import/Models/ImportJob.cs | DTO: import job with status, progress, file path |
| CREATE | src/UPACIP.Service/Import/Models/ImportLog.cs | Entity: import history audit trail |
| CREATE | src/UPACIP.Service/Import/Models/ImportProgress.cs | DTO: batch progress, processed rows, estimated time remaining |
| MODIFY | src/UPACIP.Service/Import/CsvImportEngine.cs | Add progress callback parameter to ImportAsync |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<ImportLog> |
| MODIFY | src/UPACIP.Api/Program.cs | Register CsvImportBackgroundService, job store, configure form options |

## External References

- [File Uploads in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads)
- [BackgroundService — .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)
- [ConcurrentDictionary — Thread-Safe Collections](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2)
- [FormOptions.MultipartBodyLengthLimit — ASP.NET Core](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.features.formoptions.multipartbodylengthlimit)
- [Authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Small CSV (<10K rows) imports synchronously and returns ImportResult (AC-2)
- [ ] Large CSV (>10K rows) returns 202 Accepted with job ID (edge case 1)
- [ ] Progress endpoint returns real-time batch progress with estimated time remaining
- [ ] Preview endpoint validates headers and sample rows without persisting data
- [ ] Import history endpoint returns paginated results with entity type filter
- [ ] ImportLog records admin identity, file details, success/error counts
- [ ] File upload rejects non-.csv extensions and oversized files
- [ ] Temporary files are deleted after processing
- [ ] API endpoints require admin role authorization
- [ ] Error reports with >100 errors store full report as downloadable file

## Implementation Checklist

- [ ] Create `ImportLog` entity with audit fields and add DbSet to ApplicationDbContext
- [ ] Create `ImportJob` and `ImportProgress` DTOs for background job tracking
- [ ] Implement `ImportController` with upload, progress, preview, and history endpoints
- [ ] Implement `CsvImportBackgroundService` for async large-file processing
- [ ] Add progress callback support to `CsvImportEngine.ImportAsync`
- [ ] Implement file upload validation (extension, size, content type, binary check)
- [ ] Handle error report overflow (>100 errors → file-based full report)
- [ ] Register CsvImportBackgroundService, job store, and configure form options in Program.cs
