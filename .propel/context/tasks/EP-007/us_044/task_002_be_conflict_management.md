# Task - task_002_be_conflict_management

## Requirement Reference

- User Story: US_044
- Story Location: .propel/context/tasks/EP-007/us_044/us_044.md
- Acceptance Criteria:
  - AC-1: Given multiple documents are consolidated for a patient, When consolidation runs, Then the AI identifies medication discrepancies (different dosages, conflicting prescriptions) and flags them.
  - AC-3: Given a medication contraindication is detected, When the conflict is flagged, Then the system escalates it with an "URGENT" indicator and moves it to the top of the review queue.
  - AC-4: Given AI confidence for conflict detection drops below 80%, When the result is generated, Then the system automatically flags the entire review for manual verification.
- Edge Case:
  - 3+ document conflicts: System shows all conflicting sources in the comparison view, not just pairs.
  - Resolved conflict preservation: Previously resolved conflicts are not reopened unless the new document introduces contradictory data.

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

Implement the backend conflict management service that orchestrates the full conflict lifecycle: persisting detected conflicts, escalating critical conflicts (medication contraindications) to urgent status at the top of the review queue, handling low-confidence fallback to manual verification, resolving/dismissing conflicts with staff attribution, and re-evaluating conflicts when new documents are uploaded without reopening previously resolved conflicts. This service consumes results from the IConflictDetectionService (US_043/task_004) and persists them using the ClinicalConflict entity.

## Dependent Tasks

- task_001_db_conflict_schema - Requires ClinicalConflict entity and enums
- US_043/task_004_ai_conflict_detection - Requires IConflictDetectionService for AI analysis results
- US_043/task_002_be_consolidation_service - Requires ConsolidationService integration point

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `IConflictManagementService` interface | Server (Service Layer) |
| NEW | `ConflictManagementService` implementation | Server (Service Layer) |
| NEW | DTOs: `ConflictEscalationResult`, `ConflictResolutionRequest`, `ReviewQueueEntry` | Server (Models) |
| MODIFY | `ConsolidationService` - Integrate conflict persistence after detection | Server (Service Layer) |
| MODIFY | DI registration in `Program.cs` | Server (Startup) |

## Implementation Plan

1. Define `IConflictManagementService` interface with methods:
   - `PersistDetectedConflictsAsync(ConflictAnalysisResult aiResult, Guid patientId, Guid profileVersionId)` - Save AI-detected conflicts to database
   - `EscalateUrgentConflictsAsync(Guid patientId)` - Set is_urgent=true for medication_contraindication conflicts, create urgent staff notification
   - `ResolveConflictAsync(Guid conflictId, Guid userId, string resolutionNotes)` - Mark conflict resolved with attribution
   - `DismissConflictAsync(Guid conflictId, Guid userId, string reason)` - Mark false-positive conflict dismissed
   - `ReEvaluateOnNewDocumentAsync(Guid patientId, List<Guid> newDocumentIds)` - Re-run detection only against new data, skip resolved conflicts unless new contradiction found
   - `GetReviewQueueAsync(int page, int pageSize)` - Return conflicts sorted by urgency then creation date
2. Implement urgent escalation workflow: when `ConflictDetectionService` returns a conflict with severity=Critical and type=MedicationContraindication, automatically set `is_urgent=true` and create a staff notification record (AC-3)
3. Implement low-confidence fallback: when AI confidence score on any conflict in the batch is <0.80, set all conflicts in the batch to status=UnderReview and flag the entire consolidation review for manual verification (AC-4, AIR-010)
4. Implement resolved conflict preservation: on new document upload, query existing resolved conflicts for the patient, compare new data against resolved conflicts, only reopen if new document data directly contradicts the resolution (Edge Case)
5. Implement multi-document conflict aggregation: when conflict detection identifies the same conflict across 3+ source documents, merge into a single ClinicalConflict record with all source_document_ids and source_extracted_data_ids in JSONB arrays (Edge Case)
6. Modify `ConsolidationService` (from US_043/task_002) to call `PersistDetectedConflictsAsync` after conflict detection completes
7. Add structured audit logging for all conflict state transitions (detected → under_review → resolved/dismissed) with correlation IDs per NFR-035
8. Invalidate Redis-cached patient profile on conflict status changes to ensure UI reflects current state

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Services/
    Interfaces/
      IConsolidationService.cs
      IConflictDetectionService.cs
    ConsolidationService.cs
  Models/
    Consolidation/
    AI/
      ConflictAnalysisResult.cs
      DetectedConflict.cs
  Data/
    Entities/
      ClinicalConflict.cs
    Enums/
      ConflictType.cs
      ConflictSeverity.cs
      ConflictStatus.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Interfaces/IConflictManagementService.cs | Interface with persist, escalate, resolve, dismiss, re-evaluate, and queue methods |
| CREATE | Server/Services/ConflictManagementService.cs | Conflict lifecycle orchestration: persistence, escalation, resolution, re-evaluation |
| CREATE | Server/Models/Conflict/ConflictEscalationResult.cs | DTO: escalated count, urgent conflicts, notification IDs |
| CREATE | Server/Models/Conflict/ConflictResolutionRequest.cs | DTO: conflict ID, resolution notes, action (resolve/dismiss) |
| CREATE | Server/Models/Conflict/ReviewQueueEntry.cs | DTO: conflict summary, urgency, patient name, creation date |
| MODIFY | Server/Services/ConsolidationService.cs | Add call to PersistDetectedConflictsAsync after IConflictDetectionService returns results |
| MODIFY | Server/Program.cs | Register IConflictManagementService in DI container |

## External References

- [ASP.NET Core Dependency Injection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection) - Service registration patterns
- [EF Core 8 Change Tracking](https://learn.microsoft.com/en-us/ef/core/change-tracking/) - Efficient entity updates for conflict resolution
- [Serilog Structured Logging](https://github.com/serilog/serilog/wiki/Structured-Data) - Correlation ID patterns for audit trail

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Medication contraindication conflict is automatically escalated to urgent status
- [ ] Urgent conflicts appear at top of review queue sorted by creation date
- [ ] Low-confidence batch (<80%) flags entire review for manual verification
- [ ] Resolved conflicts are not reopened unless new contradictory data detected
- [ ] Multi-document conflicts aggregate all source IDs into single record
- [ ] Audit log records all conflict state transitions

## Implementation Checklist

- [ ] Define `IConflictManagementService` interface with persist, escalate, resolve, dismiss, re-evaluate, and queue retrieval methods
- [ ] Implement urgent escalation: auto-set is_urgent=true for MedicationContraindication conflicts and create staff notification (AC-3)
- [ ] Implement low-confidence fallback: flag entire batch for manual review when any conflict confidence < 0.80 (AC-4, AIR-010)
- [ ] Implement resolved conflict preservation: re-evaluate only new data against existing resolved conflicts, reopen only on direct contradiction (Edge Case)
- [ ] Implement multi-document conflict aggregation: merge conflicts spanning 3+ documents into single record with all source references (Edge Case)
- [ ] Modify ConsolidationService to call PersistDetectedConflictsAsync after AI conflict detection
- [ ] Add structured audit logging for all conflict state transitions with correlation IDs (NFR-035)
- [ ] Invalidate Redis-cached patient profile on conflict status changes
