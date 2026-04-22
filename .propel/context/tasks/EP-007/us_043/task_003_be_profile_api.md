# Task - task_003_be_profile_api

## Requirement Reference

- User Story: US_043
- Story Location: .propel/context/tasks/EP-007/us_043/us_043.md
- Acceptance Criteria:
  - AC-3: Given a staff member views the patient profile, When they select any data point, Then the source document citation is displayed linking the data to the original document section.
  - AC-1: Given multiple documents have been parsed for a patient, When consolidation runs, Then the system merges extracted medications, diagnoses, procedures, and allergies into a unified patient profile. (Profile retrieval)
- Edge Case:
  - N/A (API layer delegates edge case handling to consolidation service)

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

Implement the RESTful API endpoints that serve the consolidated 360° patient profile to the frontend. This includes retrieving the unified profile with all data categories, version history, source document citations per data point, and triggering manual consolidation. Endpoints support Redis caching (5-min TTL per NFR-030) and are documented via Swagger/OpenAPI per NFR-038. All endpoints require Staff or Admin role authorization per FR-002.

## Dependent Tasks

- task_001_db_profile_versioning_schema - Requires PatientProfileVersion entity
- task_002_be_consolidation_service - Requires IConsolidationService for trigger endpoint
- US_008 (EP-DATA) - Requires Patient, ExtractedData entities

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `PatientProfileController` | Server (API Layer) |
| NEW | DTOs: `PatientProfile360Dto`, `ProfileDataPointDto`, `SourceCitationDto`, `VersionHistoryDto` | Server (Models) |
| NEW | `IPatientProfileService` interface | Server (Service Layer) |
| NEW | `PatientProfileService` implementation | Server (Service Layer) |
| MODIFY | DI registration in `Program.cs` | Server (Startup) |

## Implementation Plan

1. Define response DTOs:
   - `PatientProfile360Dto`: patient summary, medications list, diagnoses list, procedures list, allergies list, conflict count, pending review count, current version number
   - `ProfileDataPointDto`: data content, data type, confidence score, source document ID, source attribution text, flagged for review, verified status
   - `SourceCitationDto`: document ID, document name, document category, upload date, extraction section reference
   - `VersionHistoryDto`: version number, created at, consolidated by user name, source document count, consolidation type
2. Implement `PatientProfileController` with endpoints:
   - `GET /api/patients/{patientId}/profile` - Full 360° profile with all data categories
   - `GET /api/patients/{patientId}/profile/versions` - Version history list
   - `GET /api/patients/{patientId}/profile/versions/{versionNumber}` - Specific version snapshot
   - `GET /api/patients/{patientId}/profile/data-points/{extractedDataId}/citation` - Source document citation for a data point
   - `POST /api/patients/{patientId}/profile/consolidate` - Trigger manual consolidation
3. Implement `PatientProfileService` aggregating data from ExtractedData, PatientProfileVersion, and ClinicalDocument tables using EF Core projections
4. Add Redis caching on profile GET endpoint with 5-minute TTL, invalidated on consolidation
5. Add Swagger XML documentation for all endpoints and DTOs
6. Authorize all endpoints with `[Authorize(Roles = "Staff,Admin")]` attribute

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Controllers/
  Services/
    Interfaces/
      IConsolidationService.cs
    ConsolidationService.cs
  Models/
    Consolidation/
  Data/
    Entities/
      PatientProfileVersion.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/PatientProfileController.cs | REST endpoints for profile retrieval, version history, citation, manual consolidation trigger |
| CREATE | Server/Services/Interfaces/IPatientProfileService.cs | Interface for profile aggregation and citation retrieval |
| CREATE | Server/Services/PatientProfileService.cs | Profile aggregation logic with EF Core queries, Redis caching |
| CREATE | Server/Models/Profile/PatientProfile360Dto.cs | Full profile response DTO with all data categories |
| CREATE | Server/Models/Profile/ProfileDataPointDto.cs | Individual data point with confidence, source, review status |
| CREATE | Server/Models/Profile/SourceCitationDto.cs | Document citation details for a data point |
| CREATE | Server/Models/Profile/VersionHistoryDto.cs | Version history entry DTO |
| MODIFY | Server/Program.cs | Register IPatientProfileService in DI container |

## External References

- [ASP.NET Core Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/) - Controller and routing patterns
- [ASP.NET Core Response Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed) - Distributed Redis caching with IDistributedCache
- [Swashbuckle OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle) - Swagger documentation generation
- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles) - Role-based authorization attributes

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET profile endpoint returns all 4 data categories with correct structure
- [ ] Source citation endpoint returns document details linked to data point
- [ ] Version history endpoint returns ordered version list
- [ ] Redis cache hit returns profile within sub-second response time
- [ ] Unauthorized requests return 403 for non-Staff/Admin roles
- [ ] Swagger UI displays all endpoints with correct schemas

## Implementation Checklist

- [x] Define response DTOs (PatientProfile360Dto, ProfileDataPointDto, SourceCitationDto, VersionHistoryDto) with proper JSON serialization attributes
- [x] Implement PatientProfileController with 5 endpoints (profile, versions, version detail, citation, consolidation trigger)
- [x] Implement PatientProfileService with EF Core queries aggregating ExtractedData grouped by data_type with source document joins
- [x] Add Redis distributed caching on GET profile endpoint with 5-minute TTL and cache invalidation on consolidation
- [x] Add role-based authorization (Staff, Admin) to all endpoints
- [x] Add Swagger XML documentation comments on all controller actions and DTO properties
