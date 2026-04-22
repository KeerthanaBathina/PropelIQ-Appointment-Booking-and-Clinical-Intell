# Task - task_004_db_document_parsing_status_and_retry_support

## Requirement Reference

- User Story: US_039
- Story Location: .propel/context/tasks/EP-006/us_039/us_039.md
- Acceptance Criteria:
    - AC-1: Given a document is uploaded successfully, When upload completes, Then queue state can be persisted as `queued`.
    - AC-2: Given parsing begins, When the worker starts processing, Then the status updates to `parsing`.
    - AC-3: Given results are ready, When parsing completes, Then the status updates to `parsed`.
    - AC-4: Given parsing fails, When a failure is detected, Then each retry attempt is logged.
    - AC-5: Given all retries fail, When the final retry fails, Then the status is set to `failed` and manual-review metadata is persisted.
- Edge Case:
    - EC-1: Retry records must remain durable across worker restarts so exponential backoff can resume correctly.
    - EC-2: Queue-flood handling must be queryable enough to support FIFO dispatch and active-job concurrency decisions without table scans.

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Language | C# | 12 / .NET 8 |
| Caching | Upstash Redis | 7.x |
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

Extend persistence so document parsing has a durable lifecycle and retry history. This database task updates `ClinicalDocument` status support from the upload-stage model into the parsing-stage lifecycle and adds failure-attempt tracking needed for exponential backoff, terminal-failure handling, and worker restarts. The schema must support FIFO queue dispatch queries, active-processing visibility, and manual-review follow-up without forcing later extracted-data stories into scope.

## Dependent Tasks

- US_038 task_003_db_clinical_document_metadata_and_upload_status_support (Upload metadata and `uploaded` baseline must exist)
- US_008 task_001_be_domain_entity_models (ClinicalDocument baseline entity must exist)
- US_008 task_002_be_efcore_configuration_migrations (ClinicalDocument configuration baseline must exist)

## Impacted Components

- **MODIFY** `ClinicalDocument` entity - Add parsing lifecycle statuses `queued`, `parsing`, `parsed`, and `failed` plus manual-review metadata (src/UPACIP.DataAccess/Entities/)
- **NEW** `DocumentParsingAttempt` entity - Persist retry attempts, failure reasons, next-attempt timestamps, and execution ordering (src/UPACIP.DataAccess/Entities/)
- **MODIFY** `ClinicalDocumentConfiguration` - Add indexes for patient plus status queries and recent parsing activity (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ApplicationDbContext` - Register parsing-attempt persistence and updated `ClinicalDocument` mapping (src/UPACIP.DataAccess/)
- **CREATE** EF Core migration - Add parsing-attempt table and status-support changes safely (src/UPACIP.DataAccess/Migrations/)

## Implementation Plan

1. **Extend the `ClinicalDocument` processing-status model** so upload-stage `uploaded` can advance into `queued`, `parsing`, `parsed`, and `failed` without conflicting with prior EP-006 work.
2. **Persist parsing retry attempts separately** through a `DocumentParsingAttempt` entity keyed to `ClinicalDocument`, capturing attempt number, failure reason, started-at, completed-at, and next-attempt timestamp.
3. **Add manual-review metadata** so terminal failures can record the fact that staff intervention is required without forcing downstream review workflow schema into this story.
4. **Support worker-resume behavior** by indexing retry and next-attempt fields so background processing can find due work efficiently after restart.
5. **Add indexes for operational status queries** across patient, status, created-at, and next-attempt timestamps to support SCR-012 refresh behavior and queue dispatch logic.
6. **Keep the migration backward-compatible** by preserving existing uploaded-document records and only requiring parsing fields once the orchestration path starts writing them.

## Current Project State

```text
src/
  UPACIP.DataAccess/
    Entities/
      ClinicalDocument.cs
    Configurations/
      ClinicalDocumentConfiguration.cs
    ApplicationDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Entities/ClinicalDocument.cs | Add parsing lifecycle status support and manual-review metadata |
| CREATE | src/UPACIP.DataAccess/Entities/DocumentParsingAttempt.cs | Persist per-attempt parsing failures and retry scheduling |
| MODIFY | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | Configure parsing-status fields and indexes |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register parsing-attempt entity and updated mappings |
| CREATE | src/UPACIP.DataAccess/Migrations/<timestamp>_AddDocumentParsingRetrySupport.cs | Migration adding parsing-attempt persistence and status updates |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core one-to-many relationships: https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Extend `ClinicalDocument` to support `queued`, `parsing`, `parsed`, and `failed` states alongside the upload baseline
- [x] Persist retry-attempt rows with failure reason and next-attempt scheduling data
- [x] Store manual-review-required metadata for terminal parsing failures
- [x] Add indexes supporting due-retry lookup, recent parsing activity, and patient-document status refresh
- [x] Keep uploaded-document history backward-compatible during migration rollout
- [x] Support durable retry resumption after worker restart or process interruption