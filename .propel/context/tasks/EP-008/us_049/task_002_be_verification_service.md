# Task - task_002_be_verification_service

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
| ORM | Entity Framework Core | 8.x |
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

Implement the backend code verification service that orchestrates the full verification lifecycle: retrieving pending AI-generated codes for staff review, approving codes with staff attribution, overriding codes with new values and mandatory justification, blocking deprecated code approvals with replacement suggestions, tracking verification progress per patient, and creating immutable audit log entries for every code state transition. This service consumes AI-generated MedicalCode records from US_047/US_048 and applies the human-in-the-loop verification required by FR-064 and AIR-009.

## Dependent Tasks

- task_001_db_verification_audit_schema - Requires CodeVerificationStatus/CodingAuditAction enums, CodingAuditLog entity, and MedicalCode verification fields

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `ICodeVerificationService` interface | Server (Service Layer) |
| NEW | `CodeVerificationService` implementation | Server (Service Layer) |
| NEW | DTOs: `VerificationQueueItem`, `VerificationProgress`, `DeprecationCheckResult` | Server (Models) |
| MODIFY | DI registration in `Program.cs` | Server (Startup) |

## Implementation Plan

1. Define `ICodeVerificationService` interface with methods:
   - `GetVerificationQueueAsync(Guid patientId)` → Returns list of pending MedicalCode records with AI justification, confidence score, and code type (AC-1)
   - `ApproveCodeAsync(Guid codeId, Guid userId)` → Validates code is not deprecated, sets status=Verified, records verified_by_user_id and verified_at, creates CodingAuditLog entry with action=Approved (AC-2, AC-4)
   - `OverrideCodeAsync(Guid codeId, Guid userId, string newCodeValue, string newDescription, string justification)` → Stores original_code_value, updates code_value and description, sets status=Overridden, records override_justification, creates CodingAuditLog with action=Overridden and old/new values (AC-3, AC-4)
   - `CheckDeprecatedAsync(string codeValue, string codeType)` → Queries code library for deprecation status, returns deprecated notice and replacement code suggestions (EC-1)
   - `GetVerificationProgressAsync(Guid patientId)` → Returns total, verified, overridden, and pending code counts with a status label: "fully verified", "partially verified", or "pending review" (EC-2)
   - `SearchCodesAsync(string query, string codeType, int limit)` → Searches ICD-10 or CPT code library by code value or description for the override workflow (AC-3)
   - `GetAuditTrailAsync(Guid codeId)` → Returns immutable audit log entries for a specific code ordered by timestamp descending (AC-4)
2. Implement deprecated code blocking in `ApproveCodeAsync`: before approval, call CheckDeprecatedAsync; if deprecated, throw a business exception with the deprecated notice and replacement codes (EC-1)
3. Implement audit trail creation: wrap all state transitions in a unit of work that creates the CodingAuditLog entry atomically with the MedicalCode status change (AC-4)
4. Implement verification progress calculation: count codes by status per patient, derive status label based on counts (EC-2)
5. Implement code search using EF Core ILIKE query on code_value and description columns with limit parameter for performance
6. Add structured audit logging (Serilog) for all verification operations with correlation IDs per NFR-035
7. Register ICodeVerificationService in DI container (Program.cs)
8. Add input validation: override justification minimum 10 characters, code value format validation (ICD-10: letter + digits pattern, CPT: 5-digit pattern)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Data/
    Entities/
      MedicalCode.cs
      CodingAuditLog.cs
      User.cs
    Enums/
      CodeVerificationStatus.cs
      CodingAuditAction.cs
    PatientDbContext.cs
  Services/
    Interfaces/
    ConsolidationService.cs
  Models/
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Interfaces/ICodeVerificationService.cs | Interface with approve, override, queue, search, audit, progress, and deprecation check methods |
| CREATE | Server/Services/CodeVerificationService.cs | Full verification lifecycle: approval with deprecated blocking, override with justification, audit trail creation, progress tracking |
| CREATE | Server/Models/Verification/VerificationQueueItem.cs | DTO: code summary with AI justification, confidence, type, status |
| CREATE | Server/Models/Verification/VerificationProgress.cs | DTO: total, verified, overridden, pending counts with status label |
| CREATE | Server/Models/Verification/DeprecationCheckResult.cs | DTO: is_deprecated, notice text, replacement code list |
| MODIFY | Server/Program.cs | Register ICodeVerificationService in DI container |

## External References

- [ASP.NET Core Dependency Injection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection) - Service registration patterns
- [EF Core 8 Change Tracking](https://learn.microsoft.com/en-us/ef/core/change-tracking/) - Atomic state transitions for code + audit log
- [EF Core ILIKE Queries](https://learn.microsoft.com/en-us/ef/core/providers/npgsql/functions) - PostgreSQL case-insensitive search for code lookup
- [Serilog Structured Logging](https://github.com/serilog/serilog/wiki/Structured-Data) - Correlation ID patterns for audit trail

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Approve action sets status=Verified with staff attribution and timestamp
- [ ] Override action saves original code, updates to new code, records justification
- [ ] Deprecated code approval is blocked with replacement suggestions
- [ ] All state transitions create immutable CodingAuditLog entries atomically
- [ ] Verification progress returns accurate counts and correct status label
- [ ] Code search returns relevant results for both ICD-10 and CPT queries

## Implementation Checklist

- [x] Define ICodeVerificationService interface with approve, override, queue, search, audit trail, progress, and deprecation check methods
- [x] Implement ApproveCodeAsync with deprecated code blocking (EC-1), status=Verified with staff attribution (AC-2), and atomic CodingAuditLog creation (AC-4)
- [x] Implement OverrideCodeAsync storing original_code_value, applying new code, recording justification, and creating audit entry with old/new values (AC-3, AC-4)
- [x] Implement GetVerificationQueueAsync returning pending codes with AI justification and confidence scores (AC-1)
- [x] Implement GetVerificationProgressAsync with total/verified/overridden/pending counts and status label derivation (EC-2)
- [x] Implement SearchCodesAsync with EF Core ILIKE query on code_value and description for override code search (AC-3)
- [x] Implement GetAuditTrailAsync returning immutable audit history ordered by timestamp descending (AC-4)
- [x] Add input validation (justification min 10 chars, code format validation) and structured Serilog logging with correlation IDs (NFR-035)
