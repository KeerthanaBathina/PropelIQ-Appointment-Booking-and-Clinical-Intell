# Task - task_003_be_resolution_api

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
| API Documentation | Swagger/OpenAPI | 3.x |

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

Expose the conflict resolution workflow as REST API endpoints extending the existing `ConflictController` (from US_044/task_003). Adds value selection, "Both Valid" resolution, progress tracking, and profile verification status endpoints. These endpoints serve the frontend resolution UI (task_004) and ensure all resolution operations are accessible through a consistent, authorized API.

## Dependent Tasks

- task_002_be_resolution_service - Requires IConflictResolutionService for all resolution operations
- US_044/task_003_be_conflict_api - Requires existing ConflictController with base conflict endpoints

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| MODIFY | `ConflictController` - Add resolution workflow endpoints | Server (API Layer) |
| NEW | DTOs: `SelectValueRequestDto`, `BothValidRequestDto`, `ResolutionProgressResponseDto`, `ProfileVerificationResponseDto` | Server (Models) |
| NEW | `ProfileController` or extend patient endpoints - verification status | Server (API Layer) |

## Implementation Plan

1. Add value selection endpoint to `ConflictController`:
   - `PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value`
   - Request body: `SelectValueRequestDto { Guid SelectedExtractedDataId, string ResolutionNotes }`
   - Response: 200 OK with resolved conflict summary, 404 if conflict not found, 409 if already resolved, 422 if selectedExtractedDataId not in conflict sources
   - Authorization: `[Authorize(Roles = "Staff,Admin")]`
   - Calls `IConflictResolutionService.SelectConflictValueAsync`
2. Add "Both Valid" resolution endpoint:
   - `PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid`
   - Request body: `BothValidRequestDto { string Explanation }`
   - Response: 200 OK with resolved conflict summary, 404/409 error handling
   - Authorization: `[Authorize(Roles = "Staff,Admin")]`
   - Calls `IConflictResolutionService.ResolveBothValidAsync`
3. Add resolution progress endpoint:
   - `GET /api/patients/{patientId}/conflicts/resolution-progress`
   - Response: `ResolutionProgressResponseDto { int TotalConflicts, int ResolvedCount, int RemainingCount, decimal PercentComplete, string VerificationStatus }`
   - Authorization: `[Authorize(Roles = "Staff,Admin")]`
   - Calls `IConflictResolutionService.GetResolutionProgressAsync`
4. Add profile verification status endpoint:
   - `GET /api/patients/{patientId}/profile/verification-status`
   - Response: `ProfileVerificationResponseDto { string Status, Guid? VerifiedByUserId, string VerifiedByUserName, DateTimeOffset? VerifiedAt }`
   - Authorization: `[Authorize(Roles = "Staff,Admin")]`
   - Calls service to retrieve PatientProfileVersion verification fields
5. Add FluentValidation validators:
   - `SelectValueRequestDtoValidator`: SelectedExtractedDataId required (non-empty GUID), ResolutionNotes max 2000 chars
   - `BothValidRequestDtoValidator`: Explanation required, min 10 chars, max 2000 chars
6. Add Swagger/OpenAPI annotations:
   - `[ProducesResponseType]` for 200, 404, 409, 422 on each endpoint
   - `[SwaggerOperation]` summaries describing the resolution workflow
   - Tag grouping under "ConflictResolution"

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Controllers/
    ConflictController.cs  (from US_044/task_003: GET list, GET detail, PUT resolve, PUT dismiss, GET review-queue, GET summary)
  Services/
    Interfaces/
      IConflictManagementService.cs
      IConflictResolutionService.cs
    ConflictManagementService.cs
    ConflictResolutionService.cs
  Models/
    Conflict/
      ConflictListDto.cs
      ConflictDetailDto.cs
      ResolveConflictDto.cs
      SelectValueRequest.cs
      BothValidRequest.cs
      ResolutionProgressDto.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/Conflict/SelectValueRequestDto.cs | DTO: SelectedExtractedDataId (Guid), ResolutionNotes (string) |
| CREATE | Server/Models/Conflict/BothValidRequestDto.cs | DTO: Explanation (string, required) |
| CREATE | Server/Models/Conflict/ResolutionProgressResponseDto.cs | DTO: TotalConflicts, ResolvedCount, RemainingCount, PercentComplete, VerificationStatus |
| CREATE | Server/Models/Conflict/ProfileVerificationResponseDto.cs | DTO: Status, VerifiedByUserId, VerifiedByUserName, VerifiedAt |
| CREATE | Server/Validators/SelectValueRequestDtoValidator.cs | FluentValidation: SelectedExtractedDataId required, ResolutionNotes max length |
| CREATE | Server/Validators/BothValidRequestDtoValidator.cs | FluentValidation: Explanation required, min/max length |
| MODIFY | Server/Controllers/ConflictController.cs | Add PUT select-value, PUT both-valid, GET resolution-progress endpoints |
| MODIFY | Server/Controllers/ConflictController.cs | Add GET verification-status endpoint under patient conflict routes |

## External References

- [ASP.NET Core Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/) - Controller action patterns
- [FluentValidation in ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) - Request validation
- [Swagger Annotations](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle) - OpenAPI documentation

## Build Commands

- `dotnet build Server/`
- `dotnet test Server.Tests/ --filter "Category=ConflictResolutionApi"`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] PUT select-value returns 200 with resolved conflict on valid request
- [ ] PUT select-value returns 409 when conflict is already resolved
- [ ] PUT select-value returns 422 when selectedExtractedDataId is not in conflict sources
- [ ] PUT both-valid returns 200 on valid explanation, 400 on empty explanation
- [ ] GET resolution-progress returns correct resolved/total counts
- [ ] GET verification-status returns Verified when all conflicts resolved
- [ ] All endpoints enforce Staff/Admin role authorization
- [ ] Swagger documentation renders correctly with all response types

## Implementation Checklist

- [X] Create SelectValueRequestDto and BothValidRequestDto with FluentValidation validators enforcing required fields and max lengths
- [X] Add PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value endpoint calling IConflictResolutionService.SelectConflictValueAsync with 200/404/409/422 response handling (AC-2)
- [X] Add PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid endpoint calling IConflictResolutionService.ResolveBothValidAsync with 200/404/409 response handling (EC-2)
- [X] Add GET /api/patients/{patientId}/conflicts/resolution-progress endpoint returning total/resolved/remaining counts (EC-1, AC-4)
- [X] Add GET /api/patients/{patientId}/profile/verification-status endpoint returning current verification state with staff attribution (AC-4)
- [X] Add Swagger/OpenAPI annotations with ProducesResponseType, SwaggerOperation, and ConflictResolution tag grouping
