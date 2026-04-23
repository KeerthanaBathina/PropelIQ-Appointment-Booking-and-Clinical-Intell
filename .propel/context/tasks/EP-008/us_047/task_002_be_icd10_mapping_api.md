# Task - TASK_002

## Requirement Reference

- User Story: US_047
- Story Location: .propel/context/tasks/EP-008/us_047/us_047.md
- Acceptance Criteria:
  - AC-1: **Given** clinical diagnoses have been extracted from patient documents, **When** AI coding runs, **Then** the system maps each diagnosis to the most appropriate ICD-10 code with a justification explaining the mapping rationale.
  - AC-3: **Given** the ICD-10 code library is maintained, **When** a quarterly update is applied, **Then** the system refreshes the code library and revalidates any pending codes against the updated library.
  - AC-4: **Given** multiple ICD-10 codes apply to a single diagnosis, **When** the AI identifies this, **Then** the system presents all applicable codes ranked by relevance.
- Edge Case:
  - What happens when the AI cannot find a matching ICD-10 code? System assigns "uncodable" status with a confidence of 0.00 and flags for manual coding.
  - How does the system handle deprecated ICD-10 codes after a library update? System flags existing records using deprecated codes and suggests replacement codes for staff review.

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
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Message Queue | Redis Queue (Upstash) | 7.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

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

Implement the backend API layer for ICD-10 diagnosis code mapping. This includes the service layer orchestrating AI-driven code generation, REST endpoints for triggering mapping, retrieving pending codes, quarterly library refresh, and revalidation of existing codes. The service delegates AI inference to the AI Gateway (task_003) and persists results to the `MedicalCode` and `Icd10CodeLibrary` tables (task_001). It handles the "uncodable" edge case by assigning a 0.00 confidence placeholder and flags deprecated codes for staff review during library refresh.

## Dependent Tasks

- task_001_db_icd10_code_library_schema.md — Database schema must exist before API reads/writes.
- US_040 (Cross-Epic EP-006) — Requires extracted diagnosis data from clinical documents.

## Impacted Components

- **NEW** `Icd10CodingService` — Service class orchestrating ICD-10 mapping workflow
- **NEW** `Icd10LibraryService` — Service class for library refresh and revalidation
- **NEW** `CodingController` — API controller exposing ICD-10 coding endpoints
- **NEW** DTOs — `Icd10MappingRequestDto`, `Icd10MappingResponseDto`, `Icd10CodeDto`, `LibraryRefreshResultDto`
- **MODIFY** DI registration — Register new services in `Program.cs`

## Implementation Plan

1. **Create DTOs** for request/response contracts:
   - `Icd10MappingRequestDto`: `patient_id` (UUID), `diagnosis_ids` (UUID[]) — references extracted diagnosis data from US_040.
   - `Icd10CodeDto`: `code_value`, `description`, `confidence_score`, `justification`, `relevance_rank`, `validation_status`.
   - `Icd10MappingResponseDto`: `patient_id`, `codes` (List<Icd10CodeDto>), `unmapped_diagnoses` (list of diagnoses assigned "uncodable").
   - `LibraryRefreshResultDto`: `version`, `codes_added`, `codes_deprecated`, `pending_codes_revalidated`, `deprecated_records_flagged`.

2. **Implement `Icd10CodingService`**:
   - `GenerateIcd10CodesAsync(patientId, diagnosisIds)`: Read patient clinical data from DB, invoke AI Gateway (via interface `IAiCodingGateway`), receive code suggestions, validate each against `Icd10CodeLibrary` (DR-015, AIR-S02), rank multiple codes by relevance (AC-4), persist to `MedicalCode` table with `suggested_by_ai = true`, log to audit trail (AIR-S04).
   - Handle uncodable diagnoses: When AI returns no match or confidence < threshold, create `MedicalCode` entry with `code_value = "UNCODABLE"`, `ai_confidence_score = 0.00`, `justification = "No matching ICD-10 code found"`, and flag for manual review.
   - Use asynchronous processing via Redis Queue for long-running batch operations (NFR-029, AIR-O07).

3. **Implement `Icd10LibraryService`**:
   - `RefreshLibraryAsync(newVersion, codeData)`: Mark existing entries as `is_current = false` for codes not in new dataset, insert new entries with `library_version`, flag deprecated codes with `deprecated_date` and `replacement_code` (AC-3).
   - `RevalidatePendingCodesAsync()`: Query `MedicalCode` entries where `approved_by_user_id IS NULL`, check each `code_value` against current `Icd10CodeLibrary`, update `revalidation_status` to `deprecated_replaced` if code no longer current, set suggested replacement (edge case).

4. **Implement `CodingController`** with endpoints:
   - `POST /api/coding/icd10/generate` — Trigger ICD-10 mapping. Accepts `Icd10MappingRequestDto`. Returns `202 Accepted` with job ID for async processing (NFR-029). Idempotent via request deduplication (NFR-034).
   - `GET /api/coding/icd10/pending?patientId={id}` — Retrieve pending ICD-10 codes for review. Returns `Icd10MappingResponseDto`. Cached with 5-min TTL (NFR-030).
   - `POST /api/coding/icd10/library/refresh` — Admin-only endpoint for quarterly library update. Accepts new code dataset. Returns `LibraryRefreshResultDto`.
   - `POST /api/coding/icd10/library/revalidate` — Admin-only endpoint to trigger revalidation of pending codes. Returns revalidation summary.

5. **Apply cross-cutting concerns**:
   - Authorization: `[Authorize(Roles = "Staff,Admin")]` on all endpoints; library endpoints restricted to Admin.
   - Rate limiting: Per AIR-S08, enforce 100 requests/user/hour on generation endpoint.
   - Input validation: Validate `patientId` format, `diagnosisIds` non-empty (NFR-018).
   - Structured logging with correlation IDs (NFR-035).
   - Audit logging for all code generation and library changes (FR-066, AIR-S04).

6. **Register services** in `Program.cs` DI container.

## Current Project State

```text
[Placeholder — to be updated based on dependent task completion]
Server/
├── Controllers/
│   └── CodingController.cs       # New controller
├── Services/
│   ├── Icd10CodingService.cs     # New service
│   └── Icd10LibraryService.cs    # New service
├── DTOs/
│   ├── Icd10MappingRequestDto.cs # New DTO
│   ├── Icd10MappingResponseDto.cs# New DTO
│   ├── Icd10CodeDto.cs           # New DTO
│   └── LibraryRefreshResultDto.cs# New DTO
├── Interfaces/
│   ├── IIcd10CodingService.cs    # New interface
│   ├── IIcd10LibraryService.cs   # New interface
│   └── IAiCodingGateway.cs       # New interface (consumed by task_003)
└── Program.cs                     # Modify DI registration
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/DTOs/Icd10MappingRequestDto.cs | Request DTO for ICD-10 mapping trigger |
| CREATE | Server/DTOs/Icd10MappingResponseDto.cs | Response DTO with mapped codes and unmapped diagnoses |
| CREATE | Server/DTOs/Icd10CodeDto.cs | Individual code DTO with confidence, justification, rank |
| CREATE | Server/DTOs/LibraryRefreshResultDto.cs | Library refresh operation result DTO |
| CREATE | Server/Interfaces/IIcd10CodingService.cs | Interface for ICD-10 coding service |
| CREATE | Server/Interfaces/IIcd10LibraryService.cs | Interface for library management service |
| CREATE | Server/Interfaces/IAiCodingGateway.cs | Interface for AI Gateway integration (implemented in task_003) |
| CREATE | Server/Services/Icd10CodingService.cs | Service orchestrating AI mapping, validation, persistence |
| CREATE | Server/Services/Icd10LibraryService.cs | Service for library refresh and revalidation |
| CREATE | Server/Controllers/CodingController.cs | REST controller with 4 ICD-10 coding endpoints |
| MODIFY | Server/Program.cs | Register new services and interfaces in DI container |

## External References

- [ASP.NET Core 8 Web API documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [EF Core 8 Querying documentation](https://learn.microsoft.com/en-us/ef/core/querying/)
- [ASP.NET Core 8 Rate Limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0)
- [Polly 8 Resilience documentation](https://www.thepollyproject.org/docs/)

## Build Commands

- `dotnet build Server`
- `dotnet test Server.Tests`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] `POST /api/coding/icd10/generate` returns 202 with job ID for valid request
- [ ] `GET /api/coding/icd10/pending?patientId={id}` returns ranked codes with confidence and justification
- [ ] Uncodable diagnoses return with `code_value = "UNCODABLE"` and `confidence = 0.00`
- [ ] Library refresh correctly marks deprecated codes and inserts new entries
- [ ] Revalidation flags `MedicalCode` records referencing deprecated library entries
- [ ] Admin-only endpoints return 403 for non-Admin roles
- [ ] Rate limiting enforced at 100 requests/user/hour on generation endpoint

## Implementation Checklist

- [x] Create DTOs: `Icd10MappingRequestDto`, `Icd10MappingResponseDto`, `Icd10CodeDto`, `LibraryRefreshResultDto`
- [x] Create interfaces: `IIcd10CodingService`, `IIcd10LibraryService`, `IAiCodingGateway`
- [x] Implement `Icd10CodingService` with AI Gateway delegation, code validation, ranking, and uncodable handling
- [x] Implement `Icd10LibraryService` with library refresh (deprecation tracking) and pending code revalidation
- [x] Implement `CodingController` with 4 REST endpoints (generate, pending, refresh, revalidate)
- [x] Apply authorization, rate limiting, input validation, structured logging, and audit logging
- [x] Register services in `Program.cs` DI container
- [x] Verify idempotent behavior on `POST /api/coding/icd10/generate` (NFR-034)
