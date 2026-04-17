# Task - task_002_be_resolution_service

## Requirement Reference

- User Story: US_045
- Story Location: .propel/context/tasks/EP-007/us_045/us_045.md
- Acceptance Criteria:
  - AC-2: Given the staff member reviews a conflict, When they select the correct data value, Then the chosen value is saved to the consolidated profile and the conflict is marked "resolved" with staff attribution.
  - AC-4: Given a staff member resolves all conflicts for a patient, When the last conflict is resolved, Then the profile status updates to "verified" and an audit log entry is created.
- Edge Cases:
  - EC-1: If the staff partially resolves conflicts and navigates away, the progress is saved. Unresolved conflicts remain flagged for the next session.
  - EC-2: Both values correct — staff can select "Both Valid — Different Dates" which preserves both entries with distinct date attribution.

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
| Cache | Upstash Redis | 7.x |
| Logging | Serilog + Seq | latest |

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

Implement the `IConflictResolutionService` responsible for the staff conflict resolution workflow. This service handles the "select correct value" flow (AC-2), saves the chosen data to the consolidated PatientProfileVersion, supports the "Both Valid — Different Dates" option (EC-2), checks for all-resolved completion to trigger profile verification (AC-4), and tracks partial resolution progress (EC-1). Operates atop the existing `IConflictManagementService` from US_044/task_002, extending resolve operations with value-selection semantics and profile consolidation.

## Dependent Tasks

- task_001_db_resolution_schema - Requires ConflictResolutionType enum, ProfileVerificationStatus enum, and new entity fields
- US_044/task_001_db_conflict_schema - Requires ClinicalConflict entity
- US_044/task_002_be_conflict_management - Requires IConflictManagementService for base conflict operations
- US_043/task_002_be_consolidation_service - Requires ConsolidationService for profile data update

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `IConflictResolutionService` interface | Server (Service Layer) |
| NEW | `ConflictResolutionService` implementation | Server (Service Layer) |
| NEW | DTOs: `SelectValueRequest`, `BothValidRequest`, `ResolutionProgressDto` | Server (Models) |
| MODIFY | `ConsolidationService` - Add method to update profile with selected value | Server (Service Layer) |
| MODIFY | DI registration in `Program.cs` | Server (Startup) |

## Implementation Plan

1. Define `IConflictResolutionService` interface:
   - `SelectConflictValueAsync(Guid conflictId, Guid selectedExtractedDataId, Guid userId, string resolutionNotes)` → Returns resolved conflict summary. Loads the ExtractedData by `selectedExtractedDataId`, writes the selected data value into the PatientProfileVersion consolidated record, marks the ClinicalConflict as resolved with `resolution_type = SelectedValue`, sets `selected_extracted_data_id`, records staff attribution (AC-2)
   - `ResolveBothValidAsync(Guid conflictId, Guid userId, string explanation)` → Marks conflict resolved with `resolution_type = BothValid`, stores `both_valid_explanation`, preserves both data entries in the PatientProfileVersion with their original date attributions (EC-2)
   - `CheckAndUpdateProfileVerificationAsync(Guid patientId, Guid userId)` → Queries all ClinicalConflict records for the patient; if zero open conflicts remain, updates PatientProfileVersion.verification_status to Verified, sets verified_by_user_id and verified_at, creates structured audit log entry (AC-4). If some remain, sets PartiallyVerified
   - `GetResolutionProgressAsync(Guid patientId)` → Returns ResolutionProgressDto with total conflicts, resolved count, remaining count, percentage complete, and current verification_status (EC-1)
2. Implement `SelectConflictValueAsync`:
   - Validate conflict exists and is in Detected or UnderReview status
   - Validate selectedExtractedDataId matches one of the conflict's source_extracted_data_ids
   - Load ExtractedData record, extract the data value
   - Call ConsolidationService to update the PatientProfileVersion consolidated data with the selected value
   - Update ClinicalConflict: status → Resolved, resolution_type → SelectedValue, selected_extracted_data_id, resolved_by, resolved_at, resolution_notes
   - Invalidate Redis-cached patient profile
   - Call CheckAndUpdateProfileVerificationAsync to check completion
   - Log structured audit event per NFR-035
3. Implement `ResolveBothValidAsync`:
   - Validate conflict exists and is resolvable
   - Validate explanation is not empty
   - Update ClinicalConflict: status → Resolved, resolution_type → BothValid, both_valid_explanation
   - Call ConsolidationService to ensure both ExtractedData values are preserved in the profile with their source date attribution
   - Invalidate Redis-cached patient profile
   - Call CheckAndUpdateProfileVerificationAsync
   - Log structured audit event
4. Implement `CheckAndUpdateProfileVerificationAsync`:
   - Query: `SELECT COUNT(*) FROM ClinicalConflict WHERE patient_id = @patientId AND status NOT IN (Resolved, Dismissed)`
   - If count == 0: Update PatientProfileVersion.verification_status → Verified, set verified_by_user_id and verified_at
   - Else if count < total: Update to PartiallyVerified
   - Create audit log entry when transitioning to Verified: `{event: "ProfileVerified", patientId, userId, conflictsResolvedCount, timestamp}` (AC-4)
5. Implement `GetResolutionProgressAsync`:
   - Count total, resolved, and remaining conflicts for the patient
   - Return ResolutionProgressDto with verification_status
6. Add concurrency protection: Use optimistic concurrency on ClinicalConflict (EF Core RowVersion) to prevent two staff members resolving the same conflict simultaneously
7. Wrap all resolve operations in a database transaction (IDbContextTransaction) to ensure atomicity: conflict update + profile consolidation + verification check
8. Register `IConflictResolutionService` → `ConflictResolutionService` as scoped in Program.cs DI container

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Services/
    Interfaces/
      IConsolidationService.cs
      IConflictDetectionService.cs
      IConflictManagementService.cs
    ConsolidationService.cs
    ConflictManagementService.cs
  Models/
    Consolidation/
    Conflict/
      ConflictEscalationResult.cs
      ConflictResolutionRequest.cs
      ReviewQueueEntry.cs
  Data/
    Entities/
      ClinicalConflict.cs
      PatientProfileVersion.cs
      ExtractedData.cs
    Enums/
      ConflictType.cs
      ConflictSeverity.cs
      ConflictStatus.cs
      ConflictResolutionType.cs
      ProfileVerificationStatus.cs
    PatientDbContext.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Interfaces/IConflictResolutionService.cs | Interface with SelectConflictValueAsync, ResolveBothValidAsync, CheckAndUpdateProfileVerificationAsync, GetResolutionProgressAsync |
| CREATE | Server/Services/ConflictResolutionService.cs | Full resolution workflow implementation with transaction management, profile consolidation, verification checks |
| CREATE | Server/Models/Conflict/SelectValueRequest.cs | DTO: conflictId, selectedExtractedDataId, resolutionNotes |
| CREATE | Server/Models/Conflict/BothValidRequest.cs | DTO: conflictId, explanation |
| CREATE | Server/Models/Conflict/ResolutionProgressDto.cs | DTO: totalConflicts, resolvedCount, remainingCount, percentComplete, verificationStatus |
| MODIFY | Server/Services/ConsolidationService.cs | Add UpdateProfileWithSelectedValueAsync and PreserveBothValuesAsync methods |
| MODIFY | Server/Program.cs | Register IConflictResolutionService in DI container |

## External References

- [EF Core Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) - Multi-operation transaction management
- [EF Core Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) - Optimistic concurrency with RowVersion
- [Serilog Structured Logging](https://github.com/serilog/serilog/wiki/Structured-Data) - Audit log correlation IDs

## Build Commands

- `dotnet build Server/`
- `dotnet test Server.Tests/ --filter "Category=ConflictResolution"`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Staff can select a value from conflict sources and it is saved to the consolidated profile
- [ ] Both Valid resolution preserves both data entries with date attribution
- [ ] Profile verification status transitions to Verified when all conflicts resolved
- [ ] Audit log entry created on profile verification completion
- [ ] Partial resolution progress correctly reports resolved/total counts
- [ ] Concurrent resolve attempts on same conflict handled via optimistic concurrency
- [ ] Redis-cached patient profile invalidated on each resolution

## Implementation Checklist

- [ ] Define IConflictResolutionService interface with SelectConflictValueAsync, ResolveBothValidAsync, CheckAndUpdateProfileVerificationAsync, and GetResolutionProgressAsync methods
- [ ] Implement SelectConflictValueAsync: validate conflict status, validate selectedExtractedDataId against source_extracted_data_ids, update consolidated profile, mark resolved with SelectedValue type (AC-2)
- [ ] Implement ResolveBothValidAsync: validate non-empty explanation, preserve both values in profile with date attribution, mark resolved with BothValid type (EC-2)
- [ ] Implement CheckAndUpdateProfileVerificationAsync: count open conflicts, transition PatientProfileVersion to Verified/PartiallyVerified, create audit log on full verification (AC-4)
- [ ] Implement GetResolutionProgressAsync: return total/resolved/remaining counts with current verification status (EC-1)
- [ ] Add optimistic concurrency protection (RowVersion) on ClinicalConflict to prevent duplicate resolution
- [ ] Wrap resolve operations in database transactions for atomicity (conflict update + profile consolidation + verification check)
- [ ] Register IConflictResolutionService as scoped service in Program.cs and invalidate Redis cache on each resolution
