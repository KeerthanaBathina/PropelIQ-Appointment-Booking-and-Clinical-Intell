# Task - task_001_be_csv_import_engine

## Requirement Reference

- User Story: us_092
- Story Location: .propel/context/tasks/EP-017/us_092/us_092.md
- Acceptance Criteria:
  - AC-1: Given an admin uploads a CSV file, When the import handler processes it, Then the system validates column headers, data types, and required fields against the target entity schema.
  - AC-2: Given validation passes, When the import runs, Then the system creates records from each valid row and reports the count of successfully imported records.
  - AC-3: Given validation errors exist, When the import completes, Then the system produces a detailed error report listing failed rows with specific field-level error messages.
  - AC-4: Given the import is executed, When duplicate detection runs, Then rows that would violate unique constraints (email, patient_id + appointment_time) are skipped and logged.
- Edge Case:
  - How does the system handle CSV files with different delimiters or encodings? System auto-detects common delimiters (comma, semicolon, tab) and supports UTF-8 and ASCII encoding.

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

Implement the core CSV import engine that parses, validates, and persists CSV data into the application's entity tables (DR-033). The engine supports three target entity types for initial system population: Patient, Appointment, and User (staff/admin). It provides a pluggable architecture where each entity type has a dedicated `ICsvImportProfile<T>` that defines column-to-property mappings, validation rules, and duplicate detection logic. The engine reads CSV files using a streaming parser, validates each row against the target entity schema (AC-1) — checking column headers match expected names, data types parse correctly, and required fields are present. Valid rows are persisted via EF Core with batched `SaveChangesAsync` (AC-2). Invalid rows are collected into a structured error report with row number, column name, and field-level error message (AC-3). Duplicate detection checks unique constraints (Patient.email per DR-001, Appointment.patient_id + appointment_time per DR-014) before insert, skipping and logging duplicates (AC-4). The engine also auto-detects CSV delimiters (comma, semicolon, tab) and handles UTF-8 and ASCII encodings (edge case 2).

## Dependent Tasks

- US_008 — Requires EF Core entity models (Patient, Appointment, User) and ApplicationDbContext.
- US_010 — Requires integrity constraints and validation rules for entity fields.

## Impacted Components

- **NEW** `src/UPACIP.Service/Import/CsvImportEngine.cs` — ICsvImportEngine: core parse → validate → persist pipeline
- **NEW** `src/UPACIP.Service/Import/CsvParser.cs` — ICsvParser: streaming CSV reader with delimiter auto-detection and encoding support
- **NEW** `src/UPACIP.Service/Import/Profiles/PatientImportProfile.cs` — ICsvImportProfile<Patient>: column mapping, validation, duplicate check for patients
- **NEW** `src/UPACIP.Service/Import/Profiles/AppointmentImportProfile.cs` — ICsvImportProfile<Appointment>: column mapping, validation, duplicate check for appointments
- **NEW** `src/UPACIP.Service/Import/Profiles/UserImportProfile.cs` — ICsvImportProfile<User>: column mapping, validation for staff/admin users
- **NEW** `src/UPACIP.Service/Import/Models/ImportResult.cs` — DTO: success count, error report, duplicate count
- **NEW** `src/UPACIP.Service/Import/Models/ImportOptions.cs` — Configuration: batch size, max file size, allowed entity types
- **NEW** `src/UPACIP.Service/Import/Models/RowError.cs` — DTO: row number, column, error message, raw value
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register ICsvImportEngine, ICsvParser, import profiles, bind ImportOptions

## Implementation Plan

1. **Create `ImportOptions` configuration model**: Create in `src/UPACIP.Service/Import/Models/ImportOptions.cs`:
   - `int BatchSize` (default: 1000, per edge case 1 — "batches of 1000 rows"). Controls how many rows are persisted per `SaveChangesAsync` call.
   - `long MaxFileSizeBytes` (default: 52428800 — 50 MB). Rejects files exceeding this limit to prevent memory exhaustion.
   - `int MaxErrorsBeforeAbort` (default: 1000). Stops processing if the error count exceeds this threshold to avoid unbounded error reports.
   - `List<string> AllowedEntityTypes` (default: `["Patient", "Appointment", "User"]`). Restricts which entity types can be imported.
   Register via `IOptionsMonitor<ImportOptions>`. Add to `appsettings.json`:
   ```json
   "CsvImport": {
     "BatchSize": 1000,
     "MaxFileSizeBytes": 52428800,
     "MaxErrorsBeforeAbort": 1000,
     "AllowedEntityTypes": ["Patient", "Appointment", "User"]
   }
   ```

2. **Implement `ICsvParser` / `CsvParser` (edge case 2)**: Create in `src/UPACIP.Service/Import/CsvParser.cs` with constructor injection of `ILogger<CsvParser>`.

   **Method `IAsyncEnumerable<Dictionary<string, string>> ParseAsync(Stream csvStream, CancellationToken ct)`**:
   - (a) **Encoding detection**: Read the first 4 bytes to check for a BOM (Byte Order Mark). If UTF-8 BOM (EF BB BF) is found, use UTF-8. If no BOM, default to UTF-8 (handles ASCII as a subset). Use `StreamReader` with detected encoding.
   - (b) **Delimiter auto-detection**: Read the first line (header row). Count occurrences of comma (`,`), semicolon (`;`), and tab (`\t`). Select the delimiter with the highest count. If counts are equal, default to comma. Log the detected delimiter: `Log.Debug("CSV_DELIMITER_DETECTED: Delimiter={Delimiter}")`.
   - (c) **Header parsing**: Split the header row by the detected delimiter. Trim whitespace and normalize header names to lowercase for case-insensitive matching. Validate that at least 2 columns exist.
   - (d) **Row streaming**: For each subsequent line, split by the detected delimiter, map values to column names from the header, and yield a `Dictionary<string, string>` representing the row. Handle quoted fields (values containing the delimiter enclosed in double quotes per RFC 4180).
   - (e) **Line counting**: Track the current line number (1-based, starting from 2 since line 1 is the header) for error reporting.
   - (f) Use `IAsyncEnumerable` to stream rows lazily — do not load the entire file into memory (critical for 100K+ row files per edge case 1).

3. **Define `ICsvImportProfile<T>` interface**: Create in `src/UPACIP.Service/Import/Profiles/`:
   ```csharp
   public interface ICsvImportProfile<T> where T : class
   {
       string EntityTypeName { get; }
       string[] RequiredColumns { get; }
       string[] OptionalColumns { get; }
       T MapRow(Dictionary<string, string> row);
       List<RowError> ValidateRow(Dictionary<string, string> row, int rowNumber);
       Task<bool> IsDuplicateAsync(T entity, ApplicationDbContext context, CancellationToken ct);
   }
   ```
   - `RequiredColumns` — columns that must be present and non-empty.
   - `OptionalColumns` — columns that may be present but are not required.
   - `MapRow` — converts the raw string dictionary into a typed entity.
   - `ValidateRow` — performs field-level validation and returns a list of errors (empty if valid).
   - `IsDuplicateAsync` — checks the database for existing records that would violate unique constraints.

4. **Implement `PatientImportProfile` (AC-1, AC-4)**: Create in `src/UPACIP.Service/Import/Profiles/PatientImportProfile.cs`:
   - `RequiredColumns`: `["email", "full_name", "date_of_birth"]`.
   - `OptionalColumns`: `["phone_number", "emergency_contact"]`.
   - `MapRow`: Parse `email` (string, trimmed, lowercased), `full_name` (string), `date_of_birth` (DateTime, formats: `yyyy-MM-dd`, `MM/dd/yyyy`, `dd/MM/yyyy`), `phone_number` (string, optional), `emergency_contact` (string, optional). Generate `patient_id` as `Guid.NewGuid()`. Set `password_hash` to a temporary bcrypt hash of a random password (admin must reset). Set `created_at` and `updated_at` to `DateTime.UtcNow`.
   - `ValidateRow`:
     - Email: validate format using regex (DR-011), max 254 characters. Error: `"Invalid email format"`.
     - Full name: non-empty, max 200 characters. Error: `"Full name is required"`.
     - Date of birth: parseable date, not in the future, not before 1900-01-01. Error: `"Invalid date of birth"`.
     - Phone number (optional): if present, validate as 10-15 digit pattern.
   - `IsDuplicateAsync`: Check `context.Patients.AnyAsync(p => p.Email == entity.Email)` — enforces DR-001 unique email constraint. If duplicate, skip and log: `"Patient with email {email} already exists"` (AC-4).

5. **Implement `AppointmentImportProfile` (AC-1, AC-4)**: Create in `src/UPACIP.Service/Import/Profiles/AppointmentImportProfile.cs`:
   - `RequiredColumns`: `["patient_email", "appointment_time", "status"]`.
   - `OptionalColumns`: `["is_walk_in"]`.
   - `MapRow`: Look up `patient_id` by `patient_email` (resolve FK via EF Core query). Parse `appointment_time` (DateTime, ISO 8601 format preferred). Parse `status` as enum (`scheduled`, `completed`, `cancelled`, `no-show`). Parse `is_walk_in` as boolean (default: false). Generate `appointment_id` as `Guid.NewGuid()`.
   - `ValidateRow`:
     - Patient email: must be non-empty and correspond to an existing patient. Error: `"Patient with email {email} not found"`.
     - Appointment time: parseable datetime, not in the past for status `scheduled`. Error: `"Invalid appointment time"`.
     - Status: must be one of the allowed enum values. Error: `"Invalid status. Allowed: scheduled, completed, cancelled, no-show"`.
   - `IsDuplicateAsync`: Check `context.Appointments.AnyAsync(a => a.PatientId == entity.PatientId && a.AppointmentTime == entity.AppointmentTime)` — enforces DR-014 unique constraint (AC-4).

6. **Implement `UserImportProfile` (AC-1, AC-4)**: Create in `src/UPACIP.Service/Import/Profiles/UserImportProfile.cs`:
   - `RequiredColumns`: `["email", "role"]`.
   - `OptionalColumns`: `["full_name"]`.
   - `MapRow`: Parse `email` (trimmed, lowercased), `role` as enum (`staff`, `admin`). `patient` role is excluded from bulk import — patients register themselves. Generate `user_id` as `Guid.NewGuid()`. Set `password_hash` to a bcrypt hash of a random temporary password. Set `failed_login_attempts = 0`, `mfa_enabled = false`.
   - `ValidateRow`:
     - Email: same validation as PatientImportProfile.
     - Role: must be `staff` or `admin` only. Error: `"Invalid role. Allowed: staff, admin"`.
   - `IsDuplicateAsync`: Check `context.Users.AnyAsync(u => u.Email == entity.Email)`.

7. **Implement `ICsvImportEngine` / `CsvImportEngine` (AC-2, AC-3)**: Create in `src/UPACIP.Service/Import/CsvImportEngine.cs` with constructor injection of `ICsvParser`, `ApplicationDbContext`, `IOptionsMonitor<ImportOptions>`, `IServiceProvider` (to resolve profiles), and `ILogger<CsvImportEngine>`.

   **Method `Task<ImportResult> ImportAsync<T>(Stream csvStream, CancellationToken ct) where T : class`**:
   - (a) Resolve `ICsvImportProfile<T>` from DI.
   - (b) Parse header row via `ICsvParser`. Validate that all `RequiredColumns` are present in the header (AC-1). If missing columns, return early with `ImportResult { Status = "HeaderValidationFailed", Errors = [missing column details] }`.
   - (c) Stream rows via `ICsvParser.ParseAsync`. For each row:
     - Call `profile.ValidateRow(row, rowNumber)`. If errors, add to the error list and skip the row.
     - Call `profile.MapRow(row)` to create the entity.
     - Call `profile.IsDuplicateAsync(entity, context, ct)`. If duplicate, increment duplicate counter, add to `SkippedDuplicates` list, and skip (AC-4).
     - Add entity to `context.Set<T>()` via `context.Add(entity)`.
   - (d) **Batch persistence (AC-2)**: Every `BatchSize` valid rows, call `await context.SaveChangesAsync(ct)`. Wrap each batch in a try-catch: if a batch fails (e.g., constraint violation that slipped past pre-check), log the error, discard the batch's change tracker entries via `context.ChangeTracker.Clear()`, and add all rows in the batch to the error list.
   - (e) After all rows processed, call a final `SaveChangesAsync` for any remaining rows.
   - (f) If `errors.Count >= MaxErrorsBeforeAbort`, stop processing early and note in the result.
   - (g) Return `ImportResult` with success count, error list, duplicate count (AC-2, AC-3).

8. **Create result DTOs**: Create in `src/UPACIP.Service/Import/Models/`:

   **`ImportResult`**:
   - `string Status` — "Completed", "CompletedWithErrors", "HeaderValidationFailed", "Aborted".
   - `int TotalRows` — total rows in the CSV (excluding header).
   - `int SuccessCount` — rows successfully imported (AC-2).
   - `int ErrorCount` — rows that failed validation (AC-3).
   - `int DuplicateCount` — rows skipped due to duplicate detection (AC-4).
   - `List<RowError> Errors` — detailed error report (AC-3).
   - `List<string> SkippedDuplicates` — summary of skipped duplicate identifiers.
   - `TimeSpan Duration` — total import time.
   - `string EntityType` — which entity was imported.

   **`RowError`**:
   - `int RowNumber` — 1-based row number in the CSV (header = row 1, data starts at row 2).
   - `string ColumnName` — which column had the error.
   - `string ErrorMessage` — what went wrong.
   - `string? RawValue` — the original value from the CSV (truncated to 100 chars for log safety, PII-redacted for sensitive fields like email).

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
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   └── Retention/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs                   ← from US_008 (entity models)
│       ├── Entities/
│       │   ├── Patient.cs
│       │   ├── Appointment.cs
│       │   ├── User.cs
│       │   └── ...
│       ├── Configurations/
│       └── Migrations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (EF Core entity models) and US_010 (integrity constraints) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Import/CsvImportEngine.cs | ICsvImportEngine: parse → validate → persist pipeline with batching |
| CREATE | src/UPACIP.Service/Import/CsvParser.cs | ICsvParser: streaming CSV reader with delimiter auto-detection |
| CREATE | src/UPACIP.Service/Import/Profiles/PatientImportProfile.cs | Patient column mapping, validation, email duplicate check |
| CREATE | src/UPACIP.Service/Import/Profiles/AppointmentImportProfile.cs | Appointment column mapping, FK resolution, duplicate check |
| CREATE | src/UPACIP.Service/Import/Profiles/UserImportProfile.cs | Staff/admin column mapping, role validation, email duplicate check |
| CREATE | src/UPACIP.Service/Import/Models/ImportResult.cs | DTO: success count, error report, duplicate count |
| CREATE | src/UPACIP.Service/Import/Models/ImportOptions.cs | Config: batch size, max file size, allowed entity types |
| CREATE | src/UPACIP.Service/Import/Models/RowError.cs | DTO: row number, column, error message, raw value |
| MODIFY | src/UPACIP.Api/Program.cs | Register ICsvImportEngine, ICsvParser, profiles, bind ImportOptions |

## External References

- [StreamReader Encoding Detection — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader.-ctor#system-io-streamreader-ctor(system-io-stream-system-text-encoding-system-boolean))
- [RFC 4180 — CSV Format Specification](https://datatracker.ietf.org/doc/html/rfc4180)
- [EF Core Bulk Operations — SaveChangesAsync](https://learn.microsoft.com/en-us/ef/core/saving/basic)
- [IAsyncEnumerable — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1)
- [SHA256 — .NET Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] CSV with valid headers and data imports all rows successfully (AC-1, AC-2)
- [ ] CSV with missing required columns returns HeaderValidationFailed with column names (AC-1)
- [ ] Invalid rows produce per-row, per-field error messages in the error report (AC-3)
- [ ] Duplicate email for Patient import is skipped and logged (AC-4, DR-001)
- [ ] Duplicate patient_id + appointment_time for Appointment is skipped and logged (AC-4, DR-014)
- [ ] Semicolon and tab delimiters are auto-detected (edge case 2)
- [ ] UTF-8 and ASCII encoded files parse correctly (edge case 2)
- [ ] Quoted fields containing the delimiter are handled per RFC 4180
- [ ] Sensitive fields (email) are PII-redacted in error report raw values

## Implementation Checklist

- [ ] Create `ImportOptions` with batch size, max file size, max errors, allowed entity types
- [ ] Implement `ICsvParser` with streaming read, delimiter auto-detection, encoding support
- [ ] Define `ICsvImportProfile<T>` interface with mapping, validation, duplicate check
- [ ] Implement `PatientImportProfile` with email validation, DOB parsing, email duplicate check
- [ ] Implement `AppointmentImportProfile` with FK resolution, status enum, composite duplicate check
- [ ] Implement `UserImportProfile` with role restriction (staff/admin only), email duplicate check
- [ ] Implement `ICsvImportEngine` with batch persist, error collection, and result reporting
- [ ] Create `ImportResult` and `RowError` DTOs with PII-safe raw value handling
