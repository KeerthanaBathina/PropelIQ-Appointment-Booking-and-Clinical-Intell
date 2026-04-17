# Task - task_002_be_consolidation_service

## Requirement Reference

- User Story: US_043
- Story Location: .propel/context/tasks/EP-007/us_043/us_043.md
- Acceptance Criteria:
  - AC-1: Given multiple documents have been parsed for a patient, When consolidation runs, Then the system merges extracted medications, diagnoses, procedures, and allergies into a unified patient profile.
  - AC-2: Given the consolidated profile is created or updated, When the update completes, Then a new profile version is recorded with timestamp, user attribution, and list of source documents.
  - AC-4: Given new documents are uploaded for an existing patient, When consolidation runs, Then the profile incrementally updates with new data without losing previously verified entries.
- Edge Case:
  - Identical data points: System deduplicates by matching drug name + dosage or diagnosis code + date and retains the higher-confidence entry.
  - 50+ documents: Consolidation processes documents in chronological order in batches of 10 to prevent memory and timeout issues.

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
| Caching | Upstash Redis | 7.x |
| Message Queue | Redis Queue (Upstash) | 7.x |

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

Implement the backend consolidation service that merges extracted clinical data (medications, diagnoses, procedures, allergies) from multiple parsed documents into a unified patient profile. The service handles deduplication, incremental updates, version tracking, batch processing for large document sets, and source document attribution. This is the core business logic layer for FR-052 and FR-056.

## Dependent Tasks

- task_001_db_profile_versioning_schema - Requires PatientProfileVersion table
- US_040 (EP-006) - Requires extracted data from document parsing pipeline
- US_008 (EP-DATA) - Requires ExtractedData and Patient entity persistence

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `IConsolidationService` interface | Server (Service Layer) |
| NEW | `ConsolidationService` implementation | Server (Service Layer) |
| NEW | `ConsolidationWorker` background service | Server (Background Workers) |
| NEW | DTOs: `ConsolidationResult`, `MergedDataPoint`, `DeduplicationResult` | Server (Models) |
| MODIFY | DI registration in `Program.cs` | Server (Startup) |

## Implementation Plan

1. Define `IConsolidationService` interface with methods: `ConsolidatePatientProfileAsync(Guid patientId, Guid triggeredByUserId)` and `IncrementalConsolidateAsync(Guid patientId, List<Guid> newDocumentIds, Guid triggeredByUserId)`
2. Implement deduplication logic per data type:
   - Medications: match by `drug_name + dosage` (case-insensitive), retain higher `confidence_score` entry
   - Diagnoses: match by `diagnosis_code + date`, retain higher `confidence_score` entry
   - Procedures: match by `procedure_code + date`, retain higher `confidence_score` entry
   - Allergies: match by `allergen_name` (case-insensitive), retain higher `confidence_score` entry
3. Implement batch processing: sort documents by `upload_date` ascending, process in batches of 10 using `IAsyncEnumerable` to prevent memory pressure
4. Implement incremental update logic: load existing verified entries (where `verified_by_user_id IS NOT NULL`), merge new data without overwriting verified entries, append unverified new entries
5. Create `PatientProfileVersion` record on each consolidation with: version number (auto-increment per patient), user attribution, source document ID list, data delta snapshot
6. Implement `ConsolidationWorker` as `BackgroundService` that dequeues consolidation jobs from Redis Queue and invokes `IConsolidationService`
7. Invoke AI conflict detection service (from task_004) after merge to flag contradictions and medication contraindications
8. Log all consolidation operations to audit trail with correlation ID per NFR-035

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Services/
  Workers/
  Models/
  Data/
    Entities/
      PatientProfileVersion.cs
    PatientDbContext.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Interfaces/IConsolidationService.cs | Interface with ConsolidatePatientProfileAsync and IncrementalConsolidateAsync methods |
| CREATE | Server/Services/ConsolidationService.cs | Core merge, deduplication, batch processing, version creation logic |
| CREATE | Server/Workers/ConsolidationWorker.cs | BackgroundService consuming Redis Queue consolidation jobs |
| CREATE | Server/Models/Consolidation/ConsolidationResult.cs | DTO: merged count, conflict count, version number, duration |
| CREATE | Server/Models/Consolidation/MergedDataPoint.cs | DTO: data type, content, confidence, source doc ID, is_duplicate flag |
| CREATE | Server/Models/Consolidation/DeduplicationResult.cs | DTO: retained entry, discarded entries, match criteria |
| MODIFY | Server/Program.cs | Register IConsolidationService, ConsolidationWorker in DI container |

## External References

- [ASP.NET Core BackgroundService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) - Background worker pattern for queue processing
- [EF Core 8 Bulk Operations](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating) - Efficient batch insert/update for consolidation
- [Polly Retry Policies](https://github.com/App-vNext/Polly) - Resilience for Redis Queue operations

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Consolidation merges data from 3+ documents into single profile
- [ ] Deduplication retains higher-confidence entry for matching data points
- [ ] Verified entries preserved during incremental consolidation
- [ ] Batch processing handles 50+ documents without timeout
- [ ] Profile version created with correct metadata on each consolidation run

## Implementation Checklist

- [ ] Define `IConsolidationService` with full and incremental consolidation methods
- [ ] Implement data-type-specific deduplication (medications by drug+dosage, diagnoses by code+date, procedures by code+date, allergies by allergen name)
- [ ] Implement batch document processing in groups of 10, sorted chronologically, using async enumeration
- [ ] Implement incremental update logic preserving verified entries and appending new unverified data
- [ ] Create `PatientProfileVersion` record with auto-incremented version number, user attribution, source document IDs, and data delta snapshot
- [ ] Implement `ConsolidationWorker` as BackgroundService consuming Redis Queue jobs
- [ ] Integrate with AI conflict detection service interface (IConflictDetectionService from task_004)
- [ ] Add structured audit logging with correlation IDs for all consolidation operations
