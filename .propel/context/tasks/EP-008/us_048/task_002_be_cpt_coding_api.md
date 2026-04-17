# Task - task_002_be_cpt_coding_api

## Requirement Reference

- User Story: US_048
- Story Location: .propel/context/tasks/EP-008/us_048/us_048.md
- Acceptance Criteria:
  - AC-1: Given clinical procedures have been extracted from patient documents, When AI coding runs, Then the system maps each procedure to the most appropriate CPT code with justification text.
  - AC-3: Given multiple CPT codes apply to a single procedure, When the AI identifies this, Then the system presents all applicable codes ranked by relevance with multi-code assignment support.
  - AC-4: Given the CPT code library is maintained, When a quarterly update is applied, Then the system refreshes the code library and revalidates pending codes.
- Edge Case:
  - Ambiguous procedure description: System assigns the closest match with reduced confidence and flags for staff verification.
  - Bundled procedures: System identifies bundling opportunities and presents the bundled code option alongside individual codes.

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
| AI Gateway | Custom .NET Service with Polly | 8.x |

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

Implement the backend API layer for CPT procedure code mapping. This includes RESTful endpoints for retrieving pending CPT codes, approving AI-suggested codes, overriding codes with justification, and refreshing the CPT code library. The service layer orchestrates the AI Gateway call for CPT mapping, validates generated codes against the CPT code library, checks payer rule combinations, supports multi-code assignment with relevance ranking, and records all changes in the audit trail. Implements circuit breaker pattern for AI service unavailability with fallback to manual coding workflow.

## Dependent Tasks

- US_008 tasks (EP-DATA) — MedicalCode entity and EF Core configuration must exist
- US_040 tasks (EP-006) — ExtractedData with procedure data_type must be available
- task_003_db_cpt_code_library — CPT code library table must exist
- task_004_ai_cpt_prompt_rag — AI prompt templates and RAG pipeline must be configured

## Impacted Components

- **NEW** `CptCodingController` — ASP.NET Core API controller with CPT coding endpoints
- **NEW** `ICptCodingService` / `CptCodingService` — Service interface and implementation for CPT mapping orchestration
- **NEW** `CptCodeLibraryService` — Service for CPT code validation and library refresh
- **NEW** `CptCodingRequestDto` / `CptCodingResponseDto` — DTOs for API request/response models
- **NEW** `CptApproveRequest` / `CptOverrideRequest` — DTOs for approve and override operations
- **MODIFY** `MedicalCode` entity — Ensure CPT-specific fields are utilized (code_type = cpt)
- **MODIFY** AI Gateway service — Add CPT-specific prompt routing alongside existing ICD-10 routing

## Implementation Plan

1. **Create `CptCodingController`** with three endpoints following RESTful conventions: `GET /api/coding/cpt/pending/{patientId}` returns pending CPT codes for a patient, `PUT /api/coding/cpt/approve` accepts array of code_ids to approve, `PUT /api/coding/cpt/override` accepts new code + justification. All endpoints require Staff role authorization via `[Authorize(Roles = "staff")]`. Implement idempotent endpoints per NFR-034.
2. **Implement `CptCodingService`** orchestrating the CPT mapping workflow per UC-007 sequence: read patient procedure data from ExtractedData (data_type = "procedure") → call AI Gateway for CPT code suggestions → validate suggestions against CPT code library → check payer rule combinations → save MedicalCode records (code_type = "cpt", suggested_by_ai = true) → return results with confidence scores.
3. **Add CPT code library validation** per AIR-S02: query `cpt_code_library` table to verify each AI-suggested CPT code exists and is active. Reject invalid codes and log validation failures. Return validation status per code in response.
4. **Implement multi-code assignment** per AC-3 and FR-068/FR-069: when AI returns multiple CPT codes for a single procedure, persist all codes with a shared `procedure_reference_id` and individual relevance rankings. Support bundled procedure detection where multiple procedures share a single CPT code — return bundled option alongside individual codes.
5. **Add payer rule validation** per FR-070: validate CPT code combinations against configurable payer-specific rules. Flag potential claim denial risks when invalid combinations detected. Store alternative code suggestions when denial risk identified.
6. **Implement quarterly code library refresh** per AC-4: create `PUT /api/coding/cpt/library/refresh` endpoint (Admin role) that imports updated CPT codes from CSV, deactivates expired codes, and triggers revalidation of pending MedicalCode records with code_type = "cpt".
7. **Add audit logging** per FR-066 and NFR-012: log all CPT code approvals, overrides, and library updates to AuditLog entity with action type, resource_type = "MedicalCode", user_id attribution, and timestamp. Include override justification text in audit details.
8. **Implement circuit breaker** via Polly per AIR-O04: wrap AI Gateway calls with circuit breaker policy (open after 5 consecutive failures, retry after 30s). When circuit open, return HTTP 503 with `ai_available: false` flag so frontend can show manual coding fallback. Implement retry with exponential backoff (max 3 retries per AIR-O08).

## Current Project State

- No backend codebase exists yet (green-field). Project structure to be established by foundational tasks (EP-TECH).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/CptCodingController.cs | API controller with GET pending, PUT approve, PUT override, PUT library refresh endpoints |
| CREATE | Server/Services/ICptCodingService.cs | Service interface for CPT coding orchestration |
| CREATE | Server/Services/CptCodingService.cs | Service implementation: AI Gateway call → validation → persistence → audit |
| CREATE | Server/Services/CptCodeLibraryService.cs | CPT code library validation and quarterly refresh logic |
| CREATE | Server/DTOs/CptCodingRequestDto.cs | Request DTOs: CptApproveRequest, CptOverrideRequest, CptLibraryRefreshRequest |
| CREATE | Server/DTOs/CptCodingResponseDto.cs | Response DTOs: CptCodeSuggestion, CptCodingResult, CptValidationResult |
| MODIFY | Server/Services/AiGatewayService.cs | Add CPT-specific prompt routing method alongside ICD-10 |
| MODIFY | Server/Data/AppDbContext.cs | Register CptCodeLibrary DbSet if not already present |

## External References

- [ASP.NET Core Web API Controllers (v8)](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [Polly Circuit Breaker Policy (.NET)](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [Entity Framework Core 8 — Querying](https://learn.microsoft.com/en-us/ef/core/querying/)
- [CPT Code Structure — AMA](https://www.ama-assn.org/practice-management/cpt)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET /api/coding/cpt/pending/{patientId} returns CPT codes with confidence scores and justifications
- [ ] PUT /api/coding/cpt/approve updates MedicalCode.approved_by_user_id and logs audit event
- [ ] PUT /api/coding/cpt/override persists new code + justification and logs audit event with override details
- [ ] Invalid CPT codes rejected by library validation (AIR-S02)
- [ ] Multi-code assignment stores all applicable codes with relevance ranking
- [ ] Payer rule validation flags claim denial risks for invalid combinations (FR-070)
- [ ] Circuit breaker opens after 5 failures and returns ai_available: false
- [ ] Quarterly refresh endpoint imports codes and revalidates pending entries

## Implementation Checklist

- [ ] Create `CptCodingController` with GET pending, PUT approve, PUT override endpoints secured with Staff role authorization
- [ ] Implement `CptCodingService` orchestrating AI Gateway call → CPT code library validation → payer rule check → MedicalCode persistence
- [ ] Add CPT code library validation against `cpt_code_library` table per AIR-S02 (reject invalid/inactive codes)
- [ ] Implement multi-code assignment with relevance ranking and bundled procedure grouping (AC-3, FR-068, FR-069)
- [ ] Add payer rule validation with claim denial risk flagging and alternative code suggestions (FR-070)
- [ ] Create quarterly CPT code library refresh endpoint with CSV import, code deactivation, and pending code revalidation (AC-4)
- [ ] Add audit logging for all CPT code changes (approve, override, library refresh) with user attribution (FR-066, NFR-012)
- [ ] Implement Polly circuit breaker for AI Gateway calls (5 failures → open, 30s retry) with exponential backoff (AIR-O04, AIR-O08)
