# Task - TASK_002

## Requirement Reference

- User Story: US_046
- Story Location: .propel/context/tasks/EP-007/us_046/us_046.md
- Acceptance Criteria:
  - AC-1: Given AI confidence for a consolidation operation is below 80%, When the result is generated, Then the system presents the data in a manual review form pre-filled with AI suggestions marked as "low-confidence."
  - AC-2: Given the system detects clinical event dates that violate chronological plausibility (e.g., procedure date before diagnosis date), When the validation runs, Then the conflicting dates are flagged with an explanation of the inconsistency.
  - AC-3: Given a staff member is in manual fallback mode, When they edit and confirm data entries, Then each confirmed entry is saved with "manual-verified" status and staff attribution.
  - AC-4: Given the AI service is completely unavailable, When a document is uploaded, Then the system displays a banner "AI unavailable — switch to manual" and provides the manual data entry form.
- Edge Case:
  - Partial date parsing: System saves partial date, flags as "incomplete-date," and presents for staff completion.
  - Timezone: Phase 1 assumes all dates in clinic's local timezone; timezone metadata is ignored.

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
| Frontend | N/A | N/A |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Implement the backend API layer for manual fallback workflow and clinical event date validation. This task delivers the confidence threshold evaluation service that detects consolidation results below 80%, the chronological date plausibility validation service, the manual-verified save endpoint with staff attribution, AI health check and fallback routing, partial date handling, and timezone normalization. The APIs serve the frontend manual review form (TASK_001) and are consumed by the AI layer (TASK_003).

## Dependent Tasks

- US_043 - Requires consolidation pipeline backend services and data models
- US_008 - Requires ExtractedData entity and repository in Entity Framework Core

## Impacted Components

- `ConsolidationConfidenceService` — NEW service evaluating overall AI confidence for consolidation results (Server/Services/)
- `DateValidationService` — NEW service for chronological plausibility validation of clinical events (Server/Services/)
- `ManualFallbackController` — NEW API controller for manual review and verification endpoints (Server/Controllers/)
- `AiHealthCheckService` — NEW service for AI provider availability monitoring (Server/Services/)
- `ExtractedData` — UPDATE entity to add `verification_status` and `verified_by_user_id` fields (Server/Models/)
- `ConsolidationReviewDto` — NEW DTO for low-confidence results formatted for manual review (Server/DTOs/)

## Implementation Plan

1. **Create ConsolidationConfidenceService** — Implement a service that evaluates the aggregate confidence score of consolidation results. Query `ExtractedData` records for a given patient/document, calculate mean confidence, and flag individual entries below the 0.80 threshold. Return a structured result indicating whether manual fallback is required and which entries need review. Use `IQueryable` with EF Core for efficient database queries.

2. **Implement DateValidationService** — Build a validation service with chronological plausibility rules for clinical events. Rules include: procedure dates must not precede diagnosis dates for the same condition, discharge dates must follow admission dates, follow-up appointments must follow initial visits. Accept a collection of `ExtractedData` entries, cross-reference date fields by `data_type` and clinical context, and return a list of violations with human-readable explanations (e.g., "Procedure 'Appendectomy' dated 2023-01-10 precedes diagnosis 'Appendicitis' dated 2023-06-15"). Handle partial dates by flagging as "incomplete-date" without blocking validation.

3. **Create ManualFallbackController with review endpoints** — Implement `GET /api/consolidation/{patientId}/manual-review` endpoint that returns low-confidence consolidation results pre-formatted for the manual review form (AC-1). Include all `ExtractedData` entries with `confidence_score < 0.80`, grouped by `data_type` (medication, diagnosis, procedure, allergy), with source document attribution. Implement `POST /api/consolidation/manual-verify` endpoint accepting a list of verified entries, each with edited data content, "manual-verified" status, and staff `user_id` attribution (AC-3). Use `[Authorize(Roles = "Staff")]` for RBAC.

4. **Implement AI health check and fallback routing** — Create `AiHealthCheckService` that monitors AI provider availability using Redis-cached health status (5-minute TTL per NFR-030). Expose `GET /api/ai/health` endpoint returning availability status. When the AI Gateway reports failure (circuit breaker open per AIR-O04), set cached status to unavailable. The `POST /api/documents/{documentId}/upload` pipeline checks AI health before queuing for AI processing; if unavailable, return response with `aiAvailable: false` flag so the frontend renders the manual entry form (AC-4).

5. **Add partial date handling and timezone normalization** — Extend the `DateValidationService` to detect partial dates (month/year only, missing day) using regex pattern matching on extracted date strings. When a partial date is detected, save with an `"incomplete-date"` flag on the `ExtractedData` record and include in the manual review response. Normalize all document dates to the clinic's configured local timezone (from `appsettings.json`) by stripping timezone metadata from parsed dates and treating them as local (Phase 1 timezone strategy per edge case).

6. **Implement manual-verified save with audit logging** — In the `POST /api/consolidation/manual-verify` endpoint, update each confirmed `ExtractedData` record: set `verification_status = "manual-verified"`, `verified_by_user_id = current staff userId`, and `updated_at = DateTime.UtcNow`. Create an immutable `AuditLog` entry for each verification action with `action = "data_modify"`, `resource_type = "ExtractedData"`, `resource_id = extracted_id`, and staff `user_id` attribution (FR-093, NFR-012). Use a database transaction to ensure atomicity.

7. **Add idempotent endpoint design and input validation** — Implement idempotency for `POST /api/consolidation/manual-verify` using an `Idempotency-Key` header with Redis-cached request deduplication (NFR-034). Validate all inputs: sanitize free-text fields to prevent injection (NFR-018), validate date formats using `DateTime.TryParseExact`, validate `data_type` against enum values, and reject payloads exceeding size limits. Return `400 Bad Request` with structured error details for invalid inputs.

8. **Create integration with consolidation pipeline** — Register `ConsolidationConfidenceService` and `DateValidationService` in the dependency injection container. Wire them into the existing consolidation pipeline (from US_043) so that after AI extraction completes, the confidence evaluation runs automatically, date validation executes on all date-bearing entries, and results are stored with `confidence_score`, `incomplete-date`, and `verification_status` flags before notifying staff.

## Current Project State

- Project structure is placeholder; to be updated based on completion of dependent tasks (US_043, US_008).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/ConsolidationConfidenceService.cs | Service evaluating AI confidence thresholds for consolidation results |
| CREATE | Server/Services/IConsolidationConfidenceService.cs | Interface for consolidation confidence evaluation |
| CREATE | Server/Services/DateValidationService.cs | Chronological plausibility validation for clinical event dates |
| CREATE | Server/Services/IDateValidationService.cs | Interface for date validation service |
| CREATE | Server/Services/AiHealthCheckService.cs | AI provider availability monitoring with Redis cache |
| CREATE | Server/Services/IAiHealthCheckService.cs | Interface for AI health check service |
| CREATE | Server/Controllers/ManualFallbackController.cs | API controller for manual review GET and manual-verify POST endpoints |
| CREATE | Server/DTOs/ConsolidationReviewDto.cs | DTO for low-confidence results formatted for manual review |
| CREATE | Server/DTOs/ManualVerifyRequestDto.cs | DTO for manual verification submission payload |
| CREATE | Server/DTOs/DateViolationDto.cs | DTO for chronological plausibility violations |
| MODIFY | Server/Models/ExtractedData.cs | Add verification_status enum and verified_by_user_id FK fields |
| MODIFY | Server/Data/AppDbContext.cs | Add configuration for new ExtractedData fields and indexes |
| CREATE | Server/Migrations/AddManualVerificationFields.cs | EF Core migration for verification_status and verified_by_user_id columns |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- Entity Framework Core 8 documentation: https://learn.microsoft.com/en-us/ef/core/
- ASP.NET Core Authorization (RBAC): https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0
- Polly resilience library (circuit breaker): https://www.thepollyproject.org/
- StackExchange.Redis documentation: https://stackexchange.github.io/StackExchange.Redis/
- PostgreSQL 16 documentation: https://www.postgresql.org/docs/16/

## Build Commands

- Refer to applicable technology stack specific build commands in .propel/build/

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] ConsolidationConfidenceService correctly identifies entries below 80% threshold
- [ ] DateValidationService detects procedure-before-diagnosis and other chronological violations
- [ ] GET /api/consolidation/{patientId}/manual-review returns pre-filled low-confidence results
- [ ] POST /api/consolidation/manual-verify saves with "manual-verified" status and staff attribution
- [ ] AiHealthCheckService correctly reports AI availability from Redis cache
- [ ] Partial dates flagged as "incomplete-date" without blocking validation
- [ ] Timezone metadata stripped; dates normalized to clinic local timezone
- [ ] Audit logs created for all manual verification actions (NFR-012, FR-093)
- [ ] Idempotency-Key prevents duplicate submissions (NFR-034)
- [ ] Input validation rejects injection attempts and malformed data (NFR-018)

## Implementation Checklist

- [X] Create ConsolidationConfidenceService with aggregate confidence evaluation and per-entry threshold flagging
- [X] Implement DateValidationService with chronological plausibility rules and human-readable violation explanations
- [X] Create ManualFallbackController with GET manual-review and POST manual-verify endpoints (RBAC: Staff role)
- [X] Implement AiHealthCheckService with Redis-cached health status and circuit breaker integration
- [X] Add partial date detection, "incomplete-date" flagging, and timezone normalization to clinic local time
- [X] Implement manual-verified save with staff attribution and immutable audit logging in a DB transaction
- [X] Add idempotent endpoint design with Idempotency-Key header and input validation/sanitization
- [X] Register services in DI container and wire into consolidation pipeline (US_043 integration)
