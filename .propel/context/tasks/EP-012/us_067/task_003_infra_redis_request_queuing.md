# Task - TASK_003

## Requirement Reference

- User Story: US_067
- Story Location: .propel/context/tasks/EP-012/us_067/us_067.md
- Acceptance Criteria:
    - AC-4: **Given** the AI Gateway processes document parsing requests, **When** multiple requests arrive simultaneously, **Then** they are queued via Redis and processed with configurable concurrency limits.
- Edge Case:
    - Both primary and fallback providers unavailable: Queued jobs remain in queue with retry; after max retries exceeded, moved to dead-letter queue with "AI service unavailable" status.

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
| Message Queue | Redis Queue (powered by Upstash Redis) | 7.x |
| Caching | Upstash Redis | 7.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

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

Implement Redis-based request queuing for document parsing requests flowing through the AI Gateway. When multiple document parsing requests arrive simultaneously, they are enqueued into Redis and processed by background workers with configurable concurrency limits. This prevents AI provider rate limit violations (AIR-O07) and enables asynchronous processing (TR-012, NFR-029) so that callers receive immediate 202 Accepted responses while parsing executes in the background.

The task implements the producer/consumer pattern using Upstash Redis: the AI Gateway enqueues parsing jobs, and a .NET `BackgroundService` worker dequeues and processes them with bounded concurrency. Dead-letter queue handling ensures failed jobs are captured for investigation. Queue depth monitoring provides operational visibility.

## Dependent Tasks

- task_001_be_ai_gateway_scaffold — Requires AI Gateway service infrastructure, AIRequest/AIResponse DTOs, and DI configuration.
- US_004 - Foundational - Requires Redis infrastructure (Upstash Redis connection).

## Impacted Components

- **NEW** `Features/AIGateway/Queue/DocumentParsingQueueProducer.cs` — Enqueue parsing requests to Redis
- **NEW** `Features/AIGateway/Queue/IDocumentParsingQueueProducer.cs` — Queue producer interface
- **NEW** `Features/AIGateway/Queue/DocumentParsingQueueConsumer.cs` — BackgroundService worker for dequeuing and processing
- **NEW** `Features/AIGateway/Queue/QueueMessage.cs` — Queue message schema DTO
- **NEW** `Features/AIGateway/Queue/DeadLetterHandler.cs` — Dead-letter queue handler for failed jobs
- **NEW** `Features/AIGateway/Configuration/QueueOptions.cs` — Queue configuration POCO
- **MODIFY** `Features/AIGateway/Services/AIGatewayService.cs` — Route DocumentParsing requests through queue producer
- **MODIFY** `Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register queue services and hosted worker
- **MODIFY** `Server/appsettings.json` — Add Queue configuration section

## Implementation Plan

1. **Define queue message schema** (`QueueMessage`):
   - `JobId` (Guid) — Unique job identifier for tracking
   - `DocumentId` (Guid) — Reference to ClinicalDocument entity
   - `Request` (AIRequest) — The AI Gateway request payload
   - `Priority` (enum: Normal, Urgent) — Processing priority
   - `EnqueuedAt` (DateTimeOffset) — Timestamp for latency tracking
   - `RetryCount` (int) — Current retry attempt (0-based)
   - `MaxRetries` (int) — Maximum retry attempts before dead-letter (default: 3)
   - `CorrelationId` (string) — Request tracing correlation

2. **Implement Redis queue producer** (`DocumentParsingQueueProducer : IDocumentParsingQueueProducer`):
   - Use `StackExchange.Redis` `IDatabase.ListRightPushAsync` to enqueue serialized `QueueMessage` to `ai-gateway:document-parsing:queue` key
   - Serialize messages using `System.Text.Json` with camelCase naming policy
   - Assign `JobId` and `EnqueuedAt` at enqueue time
   - Return `JobId` to caller for status tracking
   - Log enqueue event with CorrelationId, DocumentId, QueueDepth

3. **Implement Redis queue consumer** (`DocumentParsingQueueConsumer : BackgroundService`):
   - Override `ExecuteAsync` with `SemaphoreSlim` for configurable concurrency (default: 3 concurrent workers)
   - Use `IDatabase.ListLeftPopAsync` to dequeue from `ai-gateway:document-parsing:queue`
   - Deserialize `QueueMessage` and invoke `IAIGatewayService.SendCompletionAsync` for processing
   - On success: log completion with latency (now - EnqueuedAt)
   - On failure: increment `RetryCount`, re-enqueue if under `MaxRetries`, otherwise route to dead-letter queue
   - Implement polling with configurable interval (default: 500ms) to avoid busy-wait

4. **Implement dead-letter queue handling** (`DeadLetterHandler`):
   - Push failed messages to `ai-gateway:document-parsing:dead-letter` Redis key
   - Enrich with failure reason, last error message, failed timestamp
   - Log dead-letter event as Warning with full job context
   - Expose dead-letter queue depth in health monitoring metrics

5. **Add configurable concurrency and queue settings** via `appsettings.json`:
   ```json
   {
     "AIGateway": {
       "Queue": {
         "QueueKey": "ai-gateway:document-parsing:queue",
         "DeadLetterKey": "ai-gateway:document-parsing:dead-letter",
         "MaxConcurrentWorkers": 3,
         "MaxQueueDepth": 100,
         "PollingIntervalMs": 500,
         "MaxRetries": 3,
         "DefaultPriority": "Normal"
       }
     }
   }
   ```

6. **Add queue depth monitoring and structured logging**:
   - Periodically log queue depth (every 30s) using Serilog structured properties
   - Log consumer throughput: jobs processed/minute, average processing time
   - Log queue saturation warnings when depth exceeds 80% of `MaxQueueDepth`
   - Include CorrelationId in all queue-related log entries for tracing

7. **Implement graceful shutdown**:
   - Override `StopAsync` in `DocumentParsingQueueConsumer`
   - Use `CancellationToken` to signal workers to complete current job
   - Wait for all in-flight jobs to complete (bounded by timeout: 30s)
   - Log remaining queue depth and in-flight count at shutdown

## Current Project State

```
[Placeholder — update after task_001 scaffold is complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/
│       ├── Contracts/           ← from task_001
│       ├── Middleware/          ← from task_001
│       ├── Services/            ← from task_001
│       ├── Configuration/       ← from task_001 + task_002
│       ├── Extensions/          ← from task_001
│       ├── Providers/           ← from task_002
│       ├── Resilience/          ← from task_002
│       └── Queue/               ← NEW (this task)
│           ├── IDocumentParsingQueueProducer.cs
│           ├── DocumentParsingQueueProducer.cs
│           ├── DocumentParsingQueueConsumer.cs
│           ├── QueueMessage.cs
│           └── DeadLetterHandler.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Queue/IDocumentParsingQueueProducer.cs | Queue producer interface with EnqueueAsync method |
| CREATE | Server/Features/AIGateway/Queue/DocumentParsingQueueProducer.cs | Redis LIST-based producer using StackExchange.Redis RPUSH |
| CREATE | Server/Features/AIGateway/Queue/DocumentParsingQueueConsumer.cs | BackgroundService worker with SemaphoreSlim concurrency control |
| CREATE | Server/Features/AIGateway/Queue/QueueMessage.cs | Queue message schema (JobId, DocumentId, Request, Priority, RetryCount) |
| CREATE | Server/Features/AIGateway/Queue/DeadLetterHandler.cs | Dead-letter queue handler for failed jobs exceeding max retries |
| CREATE | Server/Features/AIGateway/Configuration/QueueOptions.cs | Config POCO (MaxConcurrentWorkers, MaxQueueDepth, PollingIntervalMs, MaxRetries) |
| MODIFY | Server/Features/AIGateway/Services/AIGatewayService.cs | Route DocumentParsing request type through queue producer instead of direct processing |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Register IDocumentParsingQueueProducer, DocumentParsingQueueConsumer as hosted service |
| MODIFY | Server/appsettings.json | Add Queue configuration section under AIGateway |

## External References

- [StackExchange.Redis — GitHub](https://github.com/StackExchange/StackExchange.Redis)
- [Redis Lists as Queues — Redis Docs](https://redis.io/docs/data-types/lists/)
- [BackgroundService in .NET — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [SemaphoreSlim for Concurrency — Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [Upstash Redis — Getting Started](https://upstash.com/docs/redis/overall/getstarted)

## Build Commands

- `dotnet add package StackExchange.Redis` — Add Redis client library
- `dotnet build` — Compile with queue infrastructure
- `dotnet run` — Start backend with queue consumer background service

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Queue producer enqueues messages to Redis LIST correctly
- [ ] Queue consumer dequeues and processes messages with bounded concurrency
- [ ] Concurrency limit respected (max N simultaneous jobs)
- [ ] Failed jobs re-enqueued up to MaxRetries, then routed to dead-letter queue
- [ ] Dead-letter queue captures failed jobs with failure context
- [ ] Queue depth monitoring logs at configured interval
- [ ] Graceful shutdown completes in-flight jobs within timeout
- [ ] Configuration changes (MaxConcurrentWorkers, PollingInterval) applied correctly

## Implementation Checklist

- [X] Implement Redis queue producer for enqueuing document parsing requests using StackExchange.Redis ListRightPushAsync with serialized QueueMessage
- [X] Define QueueMessage schema DTO (JobId, DocumentId, AIRequest payload, Priority enum, EnqueuedAt, RetryCount, MaxRetries, CorrelationId)
- [X] Implement DocumentParsingQueueConsumer BackgroundService with SemaphoreSlim-based configurable concurrency limits (default: 3 workers)
- [X] Implement dead-letter queue handling for failed jobs exceeding MaxRetries, pushing to separate Redis key with failure context
- [X] Add configurable queue settings via appsettings.json with IOptions<QueueOptions> binding (MaxConcurrentWorkers, MaxQueueDepth, PollingIntervalMs, MaxRetries)
- [X] Implement queue depth monitoring with periodic Serilog structured logging (depth, throughput, saturation warnings at 80%)
- [X] Implement graceful shutdown in StopAsync with CancellationToken signaling and bounded drain timeout (30s) for in-flight requests
