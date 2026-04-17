# Task - task_002_be_patient_search_profile_api

## Requirement Reference

- User Story: US_062
- Story Location: .propel/context/tasks/EP-010/us_062/us_062.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the patient search, **When** they enter a name, DOB, or phone number, **Then** the system returns matching results within 1 second with relevance ranking.
    - AC-2: **Given** search results are displayed, **When** the staff member selects a patient, **Then** the system navigates to the complete patient profile view.
    - AC-3: **Given** the patient profile is displayed, **When** it loads, **Then** it shows sections for demographics, appointment history, intake data, uploaded documents, extracted data, and medical codes.
    - AC-4: **Given** the staff member views the patient profile, **When** they click on any section, **Then** the section expands to show detailed information with links to source screens.
- Edge Cases:
    - No results: System returns empty result set with HTTP 200 and empty array (frontend handles display).
    - Partial name search: Backend supports trigram-based partial matching with relevance scoring.

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
| Frontend | N/A | - |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | Polly (Resilience) | 8.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Implement the backend API endpoints for patient search and patient profile aggregation. The search endpoint (`GET /api/patients/search`) accepts name, DOB, and phone parameters, performs partial matching with relevance ranking, and returns paginated results within 1 second. The profile endpoint (`GET /api/patients/{id}/profile`) aggregates demographics, appointments, intake data, documents, extracted data, and medical codes into a unified response. Both endpoints enforce RBAC (Staff/Admin only), input validation, audit logging, and Redis caching per NFR-030 (5-minute TTL).

## Dependent Tasks

- US_008 - Foundational - Requires all domain entity models (Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode)
- US_004 - Foundational - Requires Redis caching infrastructure
- task_003_db_patient_search_indexes — Database indexes must exist for search performance

## Impacted Components

- **NEW** `PatientSearchController` — API controller with search and profile endpoints
- **NEW** `IPatientSearchService` / `PatientSearchService` — Business logic for patient search with relevance scoring
- **NEW** `IPatientProfileService` / `PatientProfileService` — Profile aggregation service with parallel data fetching
- **NEW** `PatientSearchRequest` DTO — Request model with validation attributes (name, dob, phone, page, pageSize)
- **NEW** `PatientSearchResponse` DTO — Response model with patient summary list, total count, pagination metadata
- **NEW** `PatientProfileResponse` DTO — Aggregated profile response with demographics, appointments, intake, documents, extracted data, codes
- **MODIFY** Service registration (DI) — Register new services in `Program.cs`

## Implementation Plan

1. **Request/Response DTOs**: Create `PatientSearchRequest` with `[FromQuery]` parameters: `Name` (string, optional), `DateOfBirth` (DateOnly, optional), `PhoneNumber` (string, optional), `Page` (int, default 1), `PageSize` (int, default 20, max 50). Create `PatientSearchResponse` with `Items` (list of `PatientSummaryDto`), `TotalCount`, `Page`, `PageSize`. Create `PatientProfileResponse` with sections for demographics, appointments, intake data, documents, extracted data, and medical codes. Add `[Required]` validation: at least one search parameter must be provided.

2. **PatientSearchController**: Create `[ApiController]` with `[Route("api/patients")]` and `[Authorize(Roles = "Staff,Admin")]`. Implement `GET /search` endpoint returning `PatientSearchResponse`. Implement `GET /{id:guid}/profile` endpoint returning `PatientProfileResponse`. Apply `[ProducesResponseType]` attributes for OpenAPI documentation (NFR-038). Apply rate limiting middleware (100 req/min/user per existing rate limiter).

3. **PatientSearchService**: Implement search logic using EF Core queries against the Patient table. Build relevance scoring:
   - Exact name match = weight 1.0
   - Starts-with match = weight 0.8
   - Contains/trigram match = weight 0.5
   - DOB exact match = weight 1.0
   - Phone exact match = weight 1.0
   - Combine scores and sort descending
   Use `pg_trgm` similarity function via raw SQL for partial name matching. Apply pagination with `Skip`/`Take`. Sanitize all inputs against SQL injection using parameterized queries (NFR-018).

4. **Redis Caching**: Cache search results with key pattern `patient:search:{hash(params)}` and 5-minute TTL (NFR-030). Cache individual patient profiles with key `patient:profile:{id}` and 5-minute TTL. Implement cache-aside pattern: check cache first, query DB on miss, populate cache. Use `IDistributedCache` interface for Redis interaction.

5. **PatientProfileService**: Implement profile aggregation by patient ID. Execute parallel queries using `Task.WhenAll` for:
   - Patient demographics (Patient table)
   - Appointment history (Appointment table, ordered by date desc)
   - Intake data (IntakeData table)
   - Clinical documents (ClinicalDocument table with processing status)
   - Extracted clinical data (ExtractedData table with confidence scores)
   - Medical codes (MedicalCode table with approval status)
   Return 404 if patient ID not found. Apply soft-delete filter (`deleted_at IS NULL`).

6. **Audit Logging**: Log all patient data access events to AuditLog table per FR-093 and NFR-012. Include `user_id`, `action: data_access`, `resource_type: Patient`, `resource_id`, `timestamp`, `ip_address`. Use the existing `AuditService` pattern (append-only, immutable).

7. **Input Validation and Sanitization**: Validate DOB format (ISO 8601 DateOnly). Validate phone number format (digits, dashes, parentheses). Trim and sanitize name input (strip HTML/script tags). Return 400 Bad Request with validation errors for invalid inputs. Ensure at least one search parameter is provided (NFR-018).

8. **OpenAPI Documentation**: Annotate controller with XML comments and `[SwaggerOperation]` for Swashbuckle auto-generated docs. Document all query parameters, response models, and error codes (200, 400, 401, 403, 404, 500) per NFR-038.

## Current Project State

- [Placeholder — to be updated based on completion of dependent tasks US_008 and US_004]

```text
Server/
├── Controllers/
├── Services/
├── Models/
│   ├── DTOs/
│   └── Entities/
├── Data/
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/PatientSearchController.cs | API controller with GET /search and GET /{id}/profile endpoints |
| CREATE | Server/Services/IPatientSearchService.cs | Interface for patient search service |
| CREATE | Server/Services/PatientSearchService.cs | Search implementation with relevance scoring and trigram matching |
| CREATE | Server/Services/IPatientProfileService.cs | Interface for patient profile aggregation service |
| CREATE | Server/Services/PatientProfileService.cs | Profile aggregation with parallel data fetching |
| CREATE | Server/Models/DTOs/PatientSearchRequest.cs | Search request DTO with validation attributes |
| CREATE | Server/Models/DTOs/PatientSearchResponse.cs | Search response DTO with pagination metadata |
| CREATE | Server/Models/DTOs/PatientProfileResponse.cs | Aggregated profile response DTO with all sections |
| MODIFY | Server/Program.cs | Register PatientSearchService and PatientProfileService in DI container |

## External References

- [ASP.NET Core 8 Web API Documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [Entity Framework Core 8 Querying](https://learn.microsoft.com/en-us/ef/core/querying/)
- [PostgreSQL pg_trgm Extension](https://www.postgresql.org/docs/16/pgtrgm.html)
- [ASP.NET Core Distributed Caching with Redis](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)
- [Swashbuckle OpenAPI Documentation](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-8.0)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Search endpoint returns results within 1 second at 95th percentile (NFR-004)
- [ ] Profile endpoint aggregates all 6 sections correctly
- [ ] Redis cache hit/miss works with 5-minute TTL (NFR-030)
- [ ] Audit log entries created for every patient data access
- [ ] Input validation rejects malformed DOB, phone, and empty queries
- [ ] OpenAPI docs generated with all endpoints documented
- [ ] RBAC enforces Staff/Admin-only access (401/403 for unauthorized)
- [ ] Soft-deleted patients excluded from search results

## Implementation Checklist

- [ ] Create `PatientSearchRequest`, `PatientSearchResponse`, and `PatientProfileResponse` DTOs with validation attributes
- [ ] Create `PatientSearchController` with `GET /api/patients/search` and `GET /api/patients/{id}/profile` endpoints, authorized for Staff/Admin roles
- [ ] Implement `PatientSearchService` with relevance-ranked search using `pg_trgm` similarity, parameterized queries, and pagination
- [ ] Implement `PatientProfileService` with parallel aggregation (`Task.WhenAll`) of demographics, appointments, intake, documents, extracted data, and medical codes
- [ ] Add Redis cache-aside pattern for search results and patient profiles with 5-minute TTL (NFR-030)
- [ ] Add audit logging for all patient data access events via AuditService (FR-093, NFR-012)
- [ ] Add input validation, sanitization (strip HTML/script tags), and at-least-one-parameter enforcement (NFR-018)
- [ ] Annotate controller with OpenAPI documentation attributes and XML comments (NFR-038)
