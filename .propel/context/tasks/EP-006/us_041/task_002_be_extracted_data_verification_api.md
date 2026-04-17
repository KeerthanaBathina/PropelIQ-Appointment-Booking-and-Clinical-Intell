# Task - task_002_be_extracted_data_verification_api

## Requirement Reference

- User Story: US_041
- Story Location: .propel/context/tasks/EP-006/us_041/us_041.md
- Acceptance Criteria:
    - AC-2: Given an extracted data point has confidence below 0.80, When the results are displayed, Then the data point is visually flagged with an amber/red indicator and marked for mandatory manual verification.
    - AC-4: Given a data point is flagged for verification, When the staff member confirms or corrects it, Then the verification status updates to "verified" with staff attribution and timestamp.
- Edge Case:
    - EC-1: If the AI cannot assign a confidence score, verification endpoints must still treat the row as review-required using the persisted `confidence-unavailable` reason rather than silently accepting it as normal.
    - EC-2: If staff select multiple flagged items, the API must support bulk verification with one request while still recording per-row attribution and timestamps.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Logging | Serilog | 8.x |
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

Implement the staff-only verification API for extracted clinical data. This backend task exposes endpoints for verifying a single flagged item, correcting and verifying an extracted row in one operation, and bulk-verifying multiple flagged rows with a single confirmation request. The workflow must stamp each verified row with verifier identity and UTC timestamp, enforce that only eligible review rows can be bulk-verified, and return enough updated state for SCR-012 and SCR-013 to refresh review counts and row statuses without reloading the full document-processing pipeline.

## Dependent Tasks

- task_003_ai_confidence_score_assignment_orchestration (Rows must already carry confidence and review-reason metadata)
- task_004_db_extracted_data_confidence_and_verification_support (Verification status, reason, and timestamp fields must exist)
- US_040 task_002_be_extracted_data_persistence_and_document_outcome_orchestration (Extracted rows must already be persisted and retrievable)
- US_013 task_002_be_rbac_authorization (Staff-only authorization policies must exist)

## Impacted Components

- **NEW** `ExtractedDataController` verification endpoints - Staff-authorized endpoints for single-row verify, corrected verify, and bulk verification (Server/Controllers/)
- **NEW** `IExtractedDataVerificationService` / `ExtractedDataVerificationService` - Applies verification rules, row corrections, timestamps, and per-row attribution (Server/Services/Documents/)
- **NEW** verification DTOs - Request and response contracts for single and bulk review operations (Server/Models/DTOs/)
- **MODIFY** extracted-data query path - Include review reason, verification status, verifier attribution, and remaining flagged counts in API responses consumed by SCR-012 and SCR-013 (Server/Services/Documents/ or Server/Models/DTOs/)
- **MODIFY** audit integration path - Record extracted-data verification actions without logging raw PHI payloads (Server/Services/)
- **MODIFY** `Program.cs` - Register verification services and route dependencies

## Implementation Plan

1. **Add staff-authorized verification endpoints** for single-item verify, single-item correct-and-verify, and bulk verify operations scoped to extracted clinical data.
2. **Enforce review eligibility deterministically** so only rows flagged for review or marked `confidence-unavailable` can flow through the mandatory-review endpoints, while already verified rows are treated as idempotent or rejected consistently.
3. **Stamp every successful verification with attribution** by setting `VerificationStatus = verified`, `VerifiedByUserId`, and `VerifiedAtUtc` for each row touched, including each row in bulk operations.
4. **Support safe correction-before-verify flows** by allowing a validated replacement `DataContent` payload on single-row verification without mixing edit semantics into bulk approval.
5. **Return lightweight refreshed review state** including updated row status, remaining flagged counts, and any per-document totals needed by the frontend after each verification action.
6. **Audit verification activity safely** by logging who verified which extracted row and when, while redacting or excluding PHI-rich `DataContent` from application logs.
7. **Keep API scope limited to review actions** by avoiding extraction reprocessing, document preview, or profile-consolidation behavior owned by later stories.

## Current Project State

```text
Server/
  Controllers/
  Services/
    Documents/
  Models/
    DTOs/
    Entities/
      ExtractedData.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/ExtractedDataController.cs | Staff-only endpoints for single and bulk verification of extracted data |
| CREATE | Server/Services/Documents/IExtractedDataVerificationService.cs | Interface for verification and correction orchestration |
| CREATE | Server/Services/Documents/ExtractedDataVerificationService.cs | Apply verification rules, updates, and bulk attribution stamping |
| CREATE | Server/Models/DTOs/VerifyExtractedDataRequest.cs | Request contract for single-row verify or correct-and-verify actions |
| CREATE | Server/Models/DTOs/BulkVerifyExtractedDataRequest.cs | Request contract for bulk verification of flagged rows |
| CREATE | Server/Models/DTOs/ExtractedDataVerificationResponse.cs | Response contract with updated status and remaining review counts |
| MODIFY | Server/Services/AuditService.cs | Record verification actions with row identifiers and verifier attribution |
| MODIFY | Server/Program.cs | Register extracted-data verification services |

## External References

- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- ASP.NET Core Web API controller actions: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core concurrency handling: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Expose staff-only endpoints for verify, correct-and-verify, and bulk verify operations on extracted rows
- [ ] Enforce that mandatory-review rows remain review-gated until explicit verification occurs
- [ ] Stamp each verified row with verifier identity and UTC timestamp, including bulk operations
- [ ] Support single-row correction payloads without allowing bulk payload edits
- [ ] Return updated status and remaining flagged counts needed by SCR-012 and SCR-013
- [ ] Record verification audit events without logging raw PHI payloads
- [ ] Keep extraction reruns, profile consolidation, and preview workflows out of this API scope