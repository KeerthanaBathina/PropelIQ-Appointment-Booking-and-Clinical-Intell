# Task - task_002_be_document_parsing_queue_orchestration

## Requirement Reference

- User Story: US_039
- Story Location: .propel/context/tasks/EP-006/us_039/us_039.md
- Acceptance Criteria:
    - AC-1: Given a document is uploaded successfully, When the upload completes, Then the system enqueues a parsing job in the Redis queue and updates `ClinicalDocument` status to `queued`.
    - AC-2: Given a parsing job is picked up from the queue, When processing begins, Then the system can advance the document into the worker flow and status `parsing`.
    - AC-4: Given parsing fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff and logs each failure.
    - AC-5: Given all retry attempts fail, When the final retry fails, Then the status is set to `failed` and staff-facing fallback actions can be triggered.
- Edge Case:
    - EC-1: If Redis is unavailable, fall back to synchronous processing with an explicit warning path rather than dropping the parsing request.
    - EC-2: If 100 or more documents are queued, preserve FIFO ordering while limiting active worker execution to the configurable default of 5 concurrent jobs.

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
| Caching | Upstash Redis | 7.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Logging | Serilog | 8.x |
| Resilience | Polly | 8.x |
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

Implement the application-side orchestration that hands uploaded documents into the asynchronous parsing pipeline. This backend task converts the `uploaded` document state from US_038 into `queued`, writes a Redis queue job, coordinates FIFO dequeue behavior with a configurable concurrency ceiling, and falls back to synchronous processing when Redis is unavailable. It also owns the retry scheduling envelope around parsing jobs so transient worker failures are resubmitted with exponential backoff and terminal failures can move documents into a consistent `failed` state.

## Dependent Tasks

- US_038 task_002_be_secure_clinical_document_upload_api (Uploaded documents and post-upload orchestration hook must exist)
- US_038 task_003_db_clinical_document_metadata_and_upload_status_support (`uploaded` status persistence must exist)
- US_004 - Foundational - Redis queue infrastructure must exist
- task_004_db_document_parsing_status_and_retry_support (Queue and retry persistence support must exist)
- task_003_ai_document_parser_worker_and_retry_orchestration (Worker-side parsing execution contract must exist)

## Impacted Components

- **NEW** `IDocumentParsingQueueService` / `DocumentParsingQueueService` - Transitions uploaded documents to `queued`, enqueues Redis jobs, and coordinates fallback processing (Server/Services/Documents/)
- **NEW** `DocumentParsingQueueJob` - Queue payload describing document ID, attempt count, enqueue time, and processing mode (Server/Models/DTOs/)
- **NEW** `DocumentParsingDispatcher` - Applies FIFO dequeue coordination and concurrency limits before invoking the parser worker (Server/BackgroundServices/)
- **MODIFY** `ClinicalDocumentUploadService` - Trigger queue handoff after successful document persistence (Server/Services/Documents/)
- **MODIFY** `Program.cs` - Register queue orchestration, dispatcher, and concurrency settings

## Implementation Plan

1. **Trigger queue orchestration immediately after a successful upload** by moving the document from `uploaded` to `queued` only after the Redis enqueue operation or fallback path is confirmed.
2. **Define a stable queue payload** containing document identifier, current attempt number, and timing metadata so retries and worker executions are deterministic.
3. **Implement FIFO queue processing with configurable concurrency** using Redis-backed dispatch and a default active-job ceiling of 5, while allowing later configuration tuning without code changes.
4. **Add Redis-unavailable fallback behavior** that invokes synchronous parsing with a degraded-mode flag and logs the warning path so staff messaging can reflect the slower experience.
5. **Schedule retry attempts with exponential backoff** for transient parser failures, keeping attempt counts and timestamps synchronized with durable retry metadata.
6. **Set terminal failure state consistently** when the last retry fails, ensuring downstream UI and manual-review actions can rely on the persisted status.
7. **Keep queue orchestration separate from model-specific parsing logic** so the same dispatch path can later support different parser implementations or AI gateway updates.

## Current Project State

```text
Server/
  Controllers/
    ClinicalDocumentsController.cs
  Services/
    Documents/
      ClinicalDocumentUploadService.cs
  BackgroundServices/
  Models/
    DTOs/
      ClinicalDocumentUploadResponse.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Documents/IDocumentParsingQueueService.cs | Interface for enqueueing and fallback-dispatching document parsing jobs |
| CREATE | Server/Services/Documents/DocumentParsingQueueService.cs | Transition documents to queued, write Redis jobs, and invoke fallback parsing when needed |
| CREATE | Server/Models/DTOs/DocumentParsingQueueJob.cs | Queue payload for FIFO parsing dispatch and retry attempts |
| CREATE | Server/BackgroundServices/DocumentParsingDispatcher.cs | Redis-backed FIFO dispatcher with configurable concurrency control |
| MODIFY | Server/Services/Documents/ClinicalDocumentUploadService.cs | Invoke queue orchestration after document upload succeeds |
| MODIFY | Server/Program.cs | Register queue orchestration services, dispatcher, and concurrency configuration |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Redis lists and blocking operations: https://redis.io/docs/latest/develop/data-types/lists/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Enqueue successful uploads into Redis and transition `ClinicalDocument` status from `uploaded` to `queued`
- [ ] Preserve FIFO dispatch behavior under high queue depth
- [ ] Enforce the default concurrency cap of 5 active parsing jobs through configuration
- [ ] Fall back to synchronous parsing with warning telemetry when Redis is unavailable
- [ ] Retry transient parsing failures up to 3 times with exponential backoff
- [ ] Mark terminal failures consistently after the final retry attempt
- [ ] Keep queue orchestration reusable and separate from parser-model specifics