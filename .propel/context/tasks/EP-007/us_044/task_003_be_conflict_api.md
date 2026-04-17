# Task - task_003_be_conflict_api

## Requirement Reference

- User Story: US_044
- Story Location: .propel/context/tasks/EP-007/us_044/us_044.md
- Acceptance Criteria:
  - AC-2: Given duplicate diagnoses with different dates are found, When the conflict is detected, Then the system flags the conflict with source citations from both documents.
  - AC-3: Given a medication contraindication is detected, When the conflict is flagged, Then the system escalates it with an "URGENT" indicator and moves it to the top of the review queue.
- Edge Case:
  - 3+ document conflicts: System shows all conflicting sources in the comparison view, not just pairs.

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

Implement the RESTful API endpoints for conflict management that serve the conflict resolution UI. Endpoints include listing active conflicts for a patient, retrieving conflict details with source citations from all involved documents, resolving/dismissing conflicts with staff attribution, and accessing the staff review queue sorted by urgency. All endpoints require Staff or Admin role authorization and are documented via Swagger/OpenAPI per NFR-038.

## Dependent Tasks

- task_001_db_conflict_schema - Requires ClinicalConflict entity
- task_002_be_conflict_management - Requires IConflictManagementService

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `ConflictController` | Server (API Layer) |
| NEW | DTOs: `ConflictListDto`, `ConflictDetailDto`, `ConflictSourceCitationDto`, `ResolveConflictDto` | Server (Models) |
| MODIFY | DI registration in `Program.cs` (if controller requires new service bindings) | Server (Startup) |

## Implementation Plan

1. Define response DTOs:
   - `ConflictListDto`: conflict_id, conflict_type, severity, status, is_urgent, patient_name, conflict_description, source_document_count, ai_confidence_score, created_at
   - `ConflictDetailDto`: all ConflictListDto fields plus ai_explanation, source_citations (list of ConflictSourceCitationDto), resolution_notes, resolved_by_user_name, resolved_at
   - `ConflictSourceCitationDto`: document_id, document_name, document_category, upload_date, extracted_data_id, data_type, data_content, confidence_score, source_attribution_text
   - `ResolveConflictDto`: resolution_notes (required), action (enum: resolve, dismiss)
2. Implement `ConflictController` with endpoints:
   - `GET /api/patients/{patientId}/conflicts` - List conflicts filtered by status, severity, type with pagination
   - `GET /api/patients/{patientId}/conflicts/{conflictId}` - Conflict detail with source citations from all involved documents (supports 3+ sources)
   - `PUT /api/patients/{patientId}/conflicts/{conflictId}/resolve` - Resolve conflict with notes and staff attribution
   - `PUT /api/patients/{patientId}/conflicts/{conflictId}/dismiss` - Dismiss false-positive with reason
   - `GET /api/conflicts/review-queue` - Staff review queue: urgent first, then by created_at descending, with pagination
   - `GET /api/patients/{patientId}/conflicts/summary` - Conflict count summary by type and severity for dashboard badges
3. Implement source citation resolution: for each conflict, join source_extracted_data_ids with ExtractedData table, then join with ClinicalDocument to build full citation chain (AIR-007)
4. Add query string filters: `?status=detected&severity=critical&type=medication_contraindication`
5. Add Swagger XML documentation for all endpoints and DTOs
6. Authorize all endpoints with `[Authorize(Roles = "Staff,Admin")]` attribute

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Controllers/
    PatientProfileController.cs
  Services/
    Interfaces/
      IConflictManagementService.cs
    ConflictManagementService.cs
  Models/
    Conflict/
  Data/
    Entities/
      ClinicalConflict.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/ConflictController.cs | REST endpoints for conflict listing, detail with citations, resolution, dismissal, review queue |
| CREATE | Server/Models/Conflict/ConflictListDto.cs | List view DTO with summary fields |
| CREATE | Server/Models/Conflict/ConflictDetailDto.cs | Detail view DTO with source citations and resolution info |
| CREATE | Server/Models/Conflict/ConflictSourceCitationDto.cs | Source document citation with extracted data context |
| CREATE | Server/Models/Conflict/ResolveConflictDto.cs | Request DTO for resolve/dismiss actions |
| CREATE | Server/Models/Conflict/ConflictSummaryDto.cs | Count aggregation by type and severity |

## External References

- [ASP.NET Core Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/) - Controller and routing patterns
- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles) - Role-based authorization
- [Swashbuckle OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle) - Swagger documentation generation
- [EF Core Projections](https://learn.microsoft.com/en-us/ef/core/querying/projections) - Efficient DTO projection from entity queries

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET conflicts endpoint returns filtered list with correct pagination
- [ ] GET conflict detail returns source citations from all involved documents (2+ sources)
- [ ] PUT resolve/dismiss updates conflict status and records user attribution
- [ ] Review queue returns urgent conflicts first, then by date descending
- [ ] Unauthorized requests return 403 for non-Staff/Admin roles
- [ ] Swagger UI displays all endpoints with correct schemas

## Implementation Checklist

- [ ] Define response DTOs (ConflictListDto, ConflictDetailDto, ConflictSourceCitationDto, ResolveConflictDto, ConflictSummaryDto) with JSON serialization attributes
- [ ] Implement ConflictController with 6 endpoints (list, detail, resolve, dismiss, review-queue, summary)
- [ ] Implement source citation resolution joining ClinicalConflict → ExtractedData → ClinicalDocument for full citation chain (AIR-007)
- [ ] Add query string filtering (status, severity, type) with pagination support
- [ ] Add role-based authorization (Staff, Admin) to all endpoints
- [ ] Add Swagger XML documentation comments on all controller actions and DTO properties
