# Task - task_003_be_verification_api

## Requirement Reference

- User Story: US_049
- Story Location: .propel/context/tasks/EP-008/us_049/us_049.md
- Acceptance Criteria:
  - AC-1: Given AI has generated ICD-10 and CPT codes, When the staff member views the medical coding screen, Then codes are presented in a verification queue with AI justification, confidence score, and approve/override actions.
  - AC-2: Given a staff member approves an AI-suggested code, When they click "Approve," Then the code status changes to "verified" with staff attribution and timestamp in the audit trail.
  - AC-3: Given a staff member disagrees with an AI suggestion, When they select "Override," Then the system presents a code search interface and requires the staff member to enter a justification for the override.
  - AC-4: Given a code change is made (approval or override), When the change is saved, Then an immutable audit log entry records the old code, new code, justification, user, and timestamp.
- Edge Case:
  - Deprecated code approval: System blocks approval and shows the deprecated notice with suggested replacement codes.
  - Partial verification: Patient coding status shows "partially verified" with a progress indicator (e.g., 3/5 codes verified).

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
| Documentation | Swagger (Swashbuckle) | 6.x |

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

Implement the RESTful API endpoints for the code verification workflow that serve the SCR-014 Medical Coding Review screen. Endpoints include the verification queue with filtering, single-code approval, code override with justification, code search for the override modal, per-code audit trail retrieval, verification progress tracking, and deprecated code checking. All endpoints require Staff or Admin role authorization and are documented via Swagger/OpenAPI per NFR-038.

## Dependent Tasks

- task_001_db_verification_audit_schema - Requires CodingAuditLog entity and MedicalCode verification fields
- task_002_be_verification_service - Requires ICodeVerificationService

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `CodeVerificationController` | Server (API Layer) |
| NEW | DTOs: `VerificationQueueDto`, `ApproveCodeResponseDto`, `OverrideCodeRequestDto`, `CodeSearchResultDto`, `CodingAuditEntryDto`, `VerificationProgressDto`, `DeprecationCheckDto` | Server (Models) |

## Implementation Plan

1. Define response and request DTOs:
   - `VerificationQueueDto`: code_id, code_type (icd10/cpt), code_value, description, justification, ai_confidence_score, status, suggested_by_ai, created_at
   - `ApproveCodeResponseDto`: code_id, status ("verified"), verified_by_user_name, verified_at, audit_log_id
   - `OverrideCodeRequestDto`: new_code_value (required), new_description (required), justification (required, min 10 chars)
   - `OverrideCodeResponseDto`: code_id, status ("overridden"), original_code_value, new_code_value, justification, audit_log_id
   - `CodeSearchResultDto`: code_value, description, is_deprecated, replacement_codes (list, nullable)
   - `CodingAuditEntryDto`: log_id, action, old_code_value, new_code_value, justification, user_name, timestamp
   - `VerificationProgressDto`: total_codes, verified_count, overridden_count, pending_count, status_label
   - `DeprecationCheckDto`: is_deprecated, deprecated_notice, replacement_codes (list)
2. Implement `CodeVerificationController` with endpoints:
   - `GET /api/patients/{patientId}/codes/verification-queue` - List pending codes with AI justification and confidence, supports ?code_type filter (AC-1)
   - `PUT /api/codes/{codeId}/approve` - Approve code; returns 200 with verified status or 409 if deprecated (AC-2, EC-1)
   - `PUT /api/codes/{codeId}/override` - Override code with OverrideCodeRequestDto body; returns 200 with override details (AC-3)
   - `GET /api/codes/{codeId}/audit-trail` - Immutable audit history for a code ordered by timestamp descending (AC-4)
   - `GET /api/codes/search?query={query}&type={icd10|cpt}&limit={20}` - Code search for override modal (AC-3)
   - `GET /api/patients/{patientId}/codes/verification-progress` - Verification progress with counts and status label (EC-2)
   - `GET /api/codes/{codeId}/deprecation-check` - Check if code is deprecated with replacements (EC-1)
3. Implement request validation using data annotations: OverrideCodeRequestDto with [Required] and [MinLength(10)] on justification
4. Map business exceptions to proper HTTP responses: DeprecatedCodeException → 409 Conflict with replacement details
5. Add Swagger XML documentation comments on all controller actions and DTO properties
6. Authorize all endpoints with `[Authorize(Roles = "Staff,Admin")]` attribute

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Controllers/
    PatientProfileController.cs
  Services/
    Interfaces/
      ICodeVerificationService.cs
    CodeVerificationService.cs
  Models/
    Verification/
  Data/
    Entities/
      MedicalCode.cs
      CodingAuditLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/CodeVerificationController.cs | REST endpoints for verification queue, approve, override, search, audit trail, progress, deprecation check |
| CREATE | Server/Models/Verification/VerificationQueueDto.cs | Response DTO for verification queue items with AI justification and confidence |
| CREATE | Server/Models/Verification/OverrideCodeRequestDto.cs | Request DTO for override with new code value, description, and justification validation |
| CREATE | Server/Models/Verification/CodeSearchResultDto.cs | Response DTO for code search results with deprecation status |
| CREATE | Server/Models/Verification/CodingAuditEntryDto.cs | Response DTO for immutable audit log entries |
| CREATE | Server/Models/Verification/DeprecationCheckDto.cs | Response DTO for deprecated code check with replacement suggestions |

## External References

- [ASP.NET Core Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/) - Controller and routing patterns
- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles) - Role-based authorization
- [ASP.NET Core Model Validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation) - Data annotation validation for request DTOs
- [Swashbuckle OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle) - Swagger documentation generation

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET verification-queue returns pending codes with AI justification and confidence
- [ ] PUT approve returns 200 with verified status and audit entry, or 409 if deprecated
- [ ] PUT override validates justification (min 10 chars), returns override details with audit entry
- [ ] GET audit-trail returns immutable history ordered by timestamp descending
- [ ] GET search returns relevant code results filtered by type
- [ ] Unauthorized requests return 403 for non-Staff/Admin roles
- [ ] Swagger UI displays all endpoints with correct request/response schemas

## Implementation Checklist

- [ ] Define request/response DTOs (VerificationQueueDto, OverrideCodeRequestDto, CodeSearchResultDto, CodingAuditEntryDto, VerificationProgressDto, DeprecationCheckDto) with JSON serialization and validation attributes
- [ ] Implement CodeVerificationController with 7 endpoints (queue, approve, override, audit-trail, search, progress, deprecation-check)
- [ ] Add request validation: OverrideCodeRequestDto with [Required] on new_code_value and [MinLength(10)] on justification
- [ ] Map business exceptions to HTTP responses (DeprecatedCodeException → 409 Conflict with replacement codes)
- [ ] Add role-based authorization (Staff, Admin) to all endpoints
- [ ] Add Swagger XML documentation comments on all controller actions and DTO properties per NFR-038
