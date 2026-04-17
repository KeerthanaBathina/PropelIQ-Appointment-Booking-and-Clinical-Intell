# Task - task_001_be_verification_enforcement

## Requirement Reference

- User Story: us_075
- Story Location: .propel/context/tasks/EP-013/us_075/us_075.md
- Acceptance Criteria:
  - AC-1: Given AI generates medical codes (ICD-10/CPT), When the codes are created, Then they remain in "pending-verification" status and cannot be finalized without staff approval.
  - AC-2: Given AI extracts clinical data, When confidence is below 0.80, Then the data is blocked from entering the consolidated profile until a staff member verifies it.
  - AC-3: Given a staff member verifies AI output, When they approve or modify the data, Then the verification is logged with staff ID, timestamp, original AI value, and final value.
  - AC-4: Given the human-in-the-loop rule is configured, When any attempt is made to bypass verification (e.g., via API), Then the system rejects the operation with "verification required" error.
- Edge Case:
  - What happens when no staff member is available to verify time-sensitive results? System queues results in "pending" status indefinitely; there is no auto-approval mechanism for clinical data.
  - How does the system handle verification for bulk AI outputs (50+ items)? System provides batch verification UI with "approve all" option that records individual audit entries for each item.

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
| Database | PostgreSQL | 16.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-S02, AIR-S03 |
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

Implement the backend enforcement layer that ensures no AI-generated medical output enters production records without human verification per AIR-S03. This includes: (1) a `VerificationStatus` enum and schema additions to `MedicalCode` and `ExtractedData` entities for tracking verification state; (2) `IVerificationEnforcementService` with methods for approving, modifying, and rejecting AI outputs plus audit logging of all verification actions (AC-3); (3) API action filters that intercept finalization attempts on unverified records and return "verification required" errors (AC-4); (4) domain rules that block low-confidence (<0.80) extracted data from the consolidated patient profile until verified (AC-2); (5) batch verification support for processing 50+ items efficiently with individual audit entries per edge case. No auto-approval mechanism exists — items remain pending indefinitely per edge case.

## Dependent Tasks

- US_049 — Requires code verification workflow foundation (approve/override actions on MedicalCode).
- US_041 — Requires confidence scoring infrastructure (ConfidenceScore on ExtractedData).
- US_008 task_001_be_domain_entity_models — Requires `MedicalCode`, `ExtractedData`, `AuditLog` entities.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Enums/VerificationStatus.cs` — Enum: PendingVerification, Verified, Modified, Rejected
- **NEW** `src/UPACIP.Service/Verification/IVerificationEnforcementService.cs` — Service interface for verification operations
- **NEW** `src/UPACIP.Service/Verification/VerificationEnforcementService.cs` — Implementation: approve, modify, reject, batch operations, audit logging
- **NEW** `src/UPACIP.Service/Verification/Dtos/VerificationRequestDto.cs` — DTO for single and batch verification requests
- **NEW** `src/UPACIP.Service/Verification/Dtos/VerificationAuditEntryDto.cs` — DTO for audit log entries with original/final values
- **NEW** `src/UPACIP.Api/Filters/VerificationRequiredFilter.cs` — API action filter rejecting finalization of unverified records
- **MODIFY** `src/UPACIP.DataAccess/Entities/MedicalCode.cs` — Add `VerificationStatus` property (default: PendingVerification)
- **MODIFY** `src/UPACIP.DataAccess/Entities/ExtractedData.cs` — Add `VerificationStatus` property (default: PendingVerification)
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Configure enum mapping and default values
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register service and action filter

## Implementation Plan

1. **Create `VerificationStatus` enum**: Define in `src/UPACIP.DataAccess/Enums/VerificationStatus.cs` with values: `PendingVerification` (initial state for all AI outputs), `Verified` (staff approved without changes), `Modified` (staff approved with corrections), `Rejected` (staff rejected the AI output). Default for all AI-generated records is `PendingVerification` per AC-1.

2. **Modify `MedicalCode` entity (AC-1)**: Add `VerificationStatus VerificationStatus` property with default value `PendingVerification`. Add `DateTime? VerifiedAt`, `Guid? VerifiedByUserId` (FK → ApplicationUser), `string? OriginalAiCodeValue` (stores the AI-suggested code before any modification for audit trail per AC-3). When AI generates a code, it is persisted with `VerificationStatus = PendingVerification` and cannot be used in downstream billing/finalization workflows.

3. **Modify `ExtractedData` entity (AC-2)**: Add `VerificationStatus VerificationStatus` property with default value `PendingVerification`. Records with `ConfidenceScore < 0.80` are automatically set to `PendingVerification` and excluded from the consolidated patient profile query via a global query filter or explicit predicate: `WHERE VerificationStatus != 'PendingVerification' OR ConfidenceScore >= 0.80`. This ensures low-confidence data is blocked from profile consolidation until verified.

4. **Implement `IVerificationEnforcementService` interface**: Methods: `Task<VerificationAuditEntryDto> ApproveAsync(Guid recordId, string recordType, Guid staffUserId)` — approves a single AI output; `Task<VerificationAuditEntryDto> ModifyAndApproveAsync(Guid recordId, string recordType, Guid staffUserId, string newValue, string justification)` — approves with modification; `Task<VerificationAuditEntryDto> RejectAsync(Guid recordId, string recordType, Guid staffUserId, string reason)` — rejects; `Task<List<VerificationAuditEntryDto>> BatchApproveAsync(List<Guid> recordIds, string recordType, Guid staffUserId)` — batch approval per edge case; `Task<bool> IsVerifiedAsync(Guid recordId, string recordType)` — check verification status.

5. **Implement verification service with audit logging (AC-3)**: In `VerificationEnforcementService.ApproveAsync`: (a) load the `MedicalCode` or `ExtractedData` record; (b) update `VerificationStatus` to `Verified`, set `VerifiedAt = DateTime.UtcNow`, `VerifiedByUserId = staffUserId`; (c) create an `AuditLog` entry with `Action = DataModify`, `ResourceType = "MedicalCode"/"ExtractedData"`, `ResourceId = recordId`, capturing original AI value and final approved value. In `ModifyAndApproveAsync`: (a) store original value in `OriginalAiCodeValue`; (b) update the code/data to the staff-provided value; (c) set `VerificationStatus = Modified`; (d) create audit entry with both old and new values plus justification text. `BatchApproveAsync` iterates and creates individual audit entries per item per edge case.

6. **Implement API bypass prevention filter (AC-4)**: Create `VerificationRequiredFilter` as an `IAsyncActionFilter`. Apply via `[ServiceFilter(typeof(VerificationRequiredFilter))]` attribute on API endpoints that finalize medical codes or include extracted data in patient profiles. The filter inspects the target record's `VerificationStatus`; if `PendingVerification`, return `400 Bad Request` with `{ "error": "verification_required", "message": "Human verification is required before this operation can be completed. No AI-generated clinical data may be finalized without staff approval." }`. This prevents any API-level bypass of the human-in-the-loop requirement.

7. **Implement no-auto-approval rule (edge case)**: The service explicitly does NOT include any timer-based or threshold-based auto-approval logic. `PendingVerification` status persists indefinitely until a staff member takes action. Document this design decision in code comments: "By design, no auto-approval mechanism exists for clinical data per AIR-S03 compliance."

8. **Register service and filter in DI**: Add `services.AddScoped<IVerificationEnforcementService, VerificationEnforcementService>()` and `services.AddScoped<VerificationRequiredFilter>()` in `Program.cs`. Configure `ApplicationDbContext` with enum-to-string conversion for `VerificationStatus` and default value `PendingVerification`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Filters/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── MedicalCode.cs
│       │   ├── ExtractedData.cs
│       │   └── AuditLog.cs
│       └── Enums/
├── app/
└── scripts/
```

> Assumes US_049 (code verification workflow), US_041 (confidence scoring), and US_008 (domain entities) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Enums/VerificationStatus.cs | Enum: PendingVerification, Verified, Modified, Rejected |
| CREATE | src/UPACIP.Service/Verification/IVerificationEnforcementService.cs | Interface: Approve, ModifyAndApprove, Reject, BatchApprove, IsVerified |
| CREATE | src/UPACIP.Service/Verification/VerificationEnforcementService.cs | Implementation: verification logic, AuditLog creation, batch operations |
| CREATE | src/UPACIP.Service/Verification/Dtos/VerificationRequestDto.cs | DTO for verification request with optional new value and justification |
| CREATE | src/UPACIP.Service/Verification/Dtos/VerificationAuditEntryDto.cs | DTO: staff ID, timestamp, original value, final value, action type |
| CREATE | src/UPACIP.Api/Filters/VerificationRequiredFilter.cs | Action filter returning 400 on unverified record finalization attempts |
| MODIFY | src/UPACIP.DataAccess/Entities/MedicalCode.cs | Add VerificationStatus, VerifiedAt, VerifiedByUserId, OriginalAiCodeValue |
| MODIFY | src/UPACIP.DataAccess/Entities/ExtractedData.cs | Add VerificationStatus property with PendingVerification default |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Configure VerificationStatus enum mapping and default values |
| MODIFY | src/UPACIP.Api/Program.cs | Register IVerificationEnforcementService and VerificationRequiredFilter |

## External References

- [ASP.NET Core Action Filters](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-8.0)
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [EF Core Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [Human-in-the-Loop AI Design Patterns](https://learn.microsoft.com/en-us/azure/architecture/guide/technology-choices/human-in-the-loop)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] `MedicalCode` records created by AI have `VerificationStatus = PendingVerification` by default
- [ ] `ExtractedData` with `ConfidenceScore < 0.80` are excluded from consolidated profile queries until verified
- [ ] `ApproveAsync` creates `AuditLog` entry with staff ID, timestamp, original and final values
- [ ] `ModifyAndApproveAsync` persists `OriginalAiCodeValue` and logs both old and new values
- [ ] `BatchApproveAsync` creates individual `AuditLog` entries per item (not a single bulk entry)
- [ ] `VerificationRequiredFilter` returns 400 with "verification_required" error on unverified finalization attempts
- [ ] No auto-approval mechanism exists — PendingVerification persists indefinitely
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [ ] Create `VerificationStatus` enum with `PendingVerification`, `Verified`, `Modified`, `Rejected` values
- [ ] Modify `MedicalCode` entity: add `VerificationStatus` (default PendingVerification), `VerifiedAt`, `VerifiedByUserId`, `OriginalAiCodeValue`
- [ ] Modify `ExtractedData` entity: add `VerificationStatus` (default PendingVerification), block low-confidence records from profile consolidation
- [ ] Implement `IVerificationEnforcementService` with `ApproveAsync`, `ModifyAndApproveAsync`, `RejectAsync`, `BatchApproveAsync`, `IsVerifiedAsync`
- [ ] Implement audit logging in all verification actions: staff ID, timestamp, original AI value, final value, justification
- [ ] Implement `VerificationRequiredFilter` action filter rejecting finalization of unverified records with 400 error
- [ ] Configure `ApplicationDbContext` with enum-to-string conversion and default values for VerificationStatus
- [ ] Register `IVerificationEnforcementService` and `VerificationRequiredFilter` in DI container
- **[AI Tasks - MANDATORY]** Verify AIR-S02 (code library validation) and AIR-S03 (human-in-the-loop) requirements are met
