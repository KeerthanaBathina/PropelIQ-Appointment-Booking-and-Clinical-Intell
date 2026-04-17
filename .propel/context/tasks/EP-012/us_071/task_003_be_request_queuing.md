# Task - TASK_003_BE_REQUEST_QUEUING

## Requirement Reference

- User Story: us_071
- Story Location: .propel/context/tasks/EP-012/us_071/us_071.md
- Acceptance Criteria:
    - AC-3: **Given** document parsing requests arrive, **When** the queue concurrency limit is reached, **Then** new requests are queued in Redis FIFO and processed as capacity becomes available.
    - AC-4: **Given** the request queue is active, **When** a request has been queued for more than 5 minutes, **Then** the system logs a warning and escalates to admins if queue depth exceeds 50 items.
- Edge Case:
    - How does the system handle queue persistence across server restarts? Redis queues persist through restarts; on recovery, the system resumes processing from the last unprocessed item.

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
| Backend | .NET (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Caching/Queue | Upstash Redis | 7.x |
| Resilience | Polly | 8.x |
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

Implement the Redis-based FIFO request queue for document parsing requests with configurable concurrency limits, queue depth monitoring, stale request warnings, and admin escalation. This service prevents AI provider rate limit violations (AIR-O07) by controlling the flow of document parsing requests. The queue persists through server restarts via Redis, and on recovery the system resumes processing from the last unprocessed item. Includes retry with exponential backoff (AIR-O08) for failed AI requests before fallback to manual workflow.

## Dependent Tasks

- task_001_db_ai_cost_tracking_schema — Requires database schema for queue metadata logging
- US_067 (EP-012) — Requires AI Gateway service for document parsing dispatch
- US_004 (EP-TECH) — Requires Redis infrastructure for FIFO queue

## Impacted Components

- **NEW** - `IDocumentParsingQueue` interface (Server project, Services/Queue)
- **NEW** - `RedisDocumentParsingQueue` implementation (Server project, Services/Queue)
- **NEW** - `IQueueMonitorService` interface (Server project, Services/Queue)
- **NEW** - `QueueMonitorService` implementation (Server project, Services/Queue)
- **NEW** - `DocumentParsingQueueProcessor` background worker (Server project, BackgroundJobs)
- **NEW** - `QueueMonitorJob` background worker (Server project, BackgroundJobs)
- **MODIFY** - `Program.cs` / DI registration — Register queue services and background workers

## Implementation Plan

1. **Implement `IDocumentParsingQueue`** — Abstraction for the Redis FIFO queue with methods to enqueue parsing requests, dequeue for processing, peek at queue depth, and retrieve queue item metadata (enqueue timestamp, document ID, patient ID). Uses Redis Lists (`LPUSH` for enqueue, `RPOP` for dequeue) to guarantee FIFO ordering. Each queue entry is a serialized JSON payload containing document_id, patient_id, enqueue_timestamp, retry_count, and correlation_id.

2. **Implement `RedisDocumentParsingQueue`** — Concrete implementation using StackExchange.Redis (via the existing Upstash Redis connection from US_004). Queue key: `ai:doc_parsing:queue`. Processing set key: `ai:doc_parsing:processing` (tracks items currently being processed to prevent re-processing on restart). Uses Redis transactions (MULTI/EXEC) for atomic dequeue + add-to-processing-set operations.

3. **Implement `DocumentParsingQueueProcessor` as `BackgroundService`** — Continuously polls the queue with configurable concurrency limit (default: 3 concurrent document parsing requests). Uses `SemaphoreSlim` to enforce concurrency. For each dequeued item: (a) dispatch to AI Gateway for parsing, (b) on success remove from processing set, (c) on failure apply retry with exponential backoff (base: 2s, max 3 attempts per AIR-O08), (d) after max retries flag document as failed and fall back to manual workflow.

4. **Implement restart recovery** — On service startup, scan the `ai:doc_parsing:processing` set for items that were in-flight during shutdown. Re-enqueue these items at the front of the queue (using `RPUSH`) so they are processed next. Log recovery actions with Serilog.

5. **Implement `IQueueMonitorService`** — Service that checks queue health: (a) reads queue depth via `LLEN`, (b) inspects the oldest item's enqueue timestamp, (c) logs warning if any item has been queued >5 minutes, (d) triggers admin escalation alert if queue depth exceeds 50 items. Uses structured Serilog logging with queue metrics (depth, oldest_age_seconds, processing_count).

6. **Implement `QueueMonitorJob` as `BackgroundService`** — Runs the queue monitor on a configurable interval (default: every 60 seconds). Emits structured log events for monitoring dashboards and triggers escalation alerts via the existing notification infrastructure.

7. **Register services and configure settings** — Add all services and background workers to DI in `Program.cs`. Add queue configuration to `appsettings.json` under `AiQueue` section: `ConcurrencyLimit`, `MaxRetries`, `RetryBaseDelaySeconds`, `MonitorIntervalSeconds`, `StaleWarningMinutes`, `EscalationDepthThreshold`.

### Pseudocode: Queue Processing Loop

```pseudocode
function ProcessQueue():
    semaphore = new SemaphoreSlim(config.ConcurrencyLimit)  // default 3

    while not cancelled:
        await semaphore.WaitAsync()

        Task.Run(async () =>
            try:
                item = await queue.DequeueAsync()  // atomic RPOP + SADD to processing set
                if item is null:
                    await Task.Delay(1000)  // poll interval when empty
                    return

                result = await aiGateway.ParseDocumentAsync(item.DocumentId)

                if result.Success:
                    await queue.RemoveFromProcessingAsync(item)
                    log.Information("Parsed {DocumentId} successfully", item.DocumentId)
                else:
                    if item.RetryCount < config.MaxRetries:
                        item.RetryCount++
                        delay = config.RetryBaseDelay * Math.Pow(2, item.RetryCount)
                        await Task.Delay(delay)
                        await queue.ReenqueueAsync(item)
                    else:
                        await queue.RemoveFromProcessingAsync(item)
                        await flagManualWorkflow(item.DocumentId)
                        log.Warning("Max retries exceeded for {DocumentId}, fallback to manual", item.DocumentId)
            finally:
                semaphore.Release()
        )
```

### Pseudocode: Queue Monitor

```pseudocode
function MonitorQueue():
    depth = await queue.GetDepthAsync()      // LLEN
    oldestAge = await queue.GetOldestAgeAsync()  // peek + timestamp diff

    if oldestAge > config.StaleWarningMinutes * 60:
        log.Warning("Queue item stale: {AgeSeconds}s", oldestAge)

    if depth > config.EscalationDepthThreshold:
        log.Error("Queue depth {Depth} exceeds threshold {Threshold}",
                  depth, config.EscalationDepthThreshold)
        await alertService.EscalateQueueDepthAsync(depth)
```

## Current Project State

- [Placeholder — to be updated during execution based on dependent task completion]

```text
Server/
├── Services/
│   ├── Queue/
│   │   └── ... (new queue services)
│   └── ... (existing services)
├── BackgroundJobs/
│   └── ... (existing + new background workers)
├── Data/
│   └── ...
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Queue/IDocumentParsingQueue.cs | Interface for Redis FIFO queue operations (enqueue, dequeue, depth, metadata) |
| CREATE | Server/Services/Queue/RedisDocumentParsingQueue.cs | Redis Lists implementation with atomic dequeue and processing set tracking |
| CREATE | Server/Services/Queue/IQueueMonitorService.cs | Interface for queue health monitoring operations |
| CREATE | Server/Services/Queue/QueueMonitorService.cs | Queue depth, stale item detection, and admin escalation |
| CREATE | Server/BackgroundJobs/DocumentParsingQueueProcessor.cs | BackgroundService: concurrent queue processing with retry and fallback |
| CREATE | Server/BackgroundJobs/QueueMonitorJob.cs | BackgroundService: periodic queue health monitoring |
| MODIFY | Server/Program.cs | Register queue services, processors, and monitor in DI container |
| MODIFY | Server/appsettings.json | Add AiQueue configuration section (concurrency, retry, monitor settings) |

## External References

- [StackExchange.Redis — Lists Documentation](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [.NET 8 BackgroundService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [Polly Retry with Exponential Backoff](https://www.thepollyproject.org/2019/02/13/retry/)
- [Redis RPOPLPUSH for Reliable Queues](https://redis.io/docs/latest/commands/rpoplpush/)
- [SemaphoreSlim for Concurrency Limiting](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)

## Build Commands

- `dotnet build Server/`
- `dotnet test Server.Tests/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] FIFO ordering verified: items dequeued in enqueue order
- [ ] Concurrency limit enforced: no more than N concurrent parse operations
- [ ] Retry with exponential backoff: failed items retried up to 3 times
- [ ] Manual workflow fallback: triggered after max retries exceeded
- [ ] Restart recovery: in-flight items re-enqueued after service restart
- [ ] Stale warning: logged when item queued >5 minutes
- [ ] Escalation alert: triggered when queue depth >50 items

## Implementation Checklist

- [ ] Create `IDocumentParsingQueue` interface with `EnqueueAsync`, `DequeueAsync`, `GetDepthAsync`, `GetOldestAgeAsync`, `RemoveFromProcessingAsync`, `ReenqueueAsync` methods
- [ ] Implement `RedisDocumentParsingQueue` using Redis Lists (LPUSH/RPOP) with atomic dequeue via MULTI/EXEC and processing set tracking (SADD/SREM)
- [ ] Implement `DocumentParsingQueueProcessor` as BackgroundService with `SemaphoreSlim` concurrency control (configurable limit, default 3), retry with exponential backoff (base 2s, max 3 per AIR-O08), and manual workflow fallback after max retries
- [ ] Implement restart recovery logic — on startup, scan processing set and re-enqueue in-flight items to queue front
- [ ] Create `IQueueMonitorService` and implement `QueueMonitorService` — check queue depth, detect stale items (>5 min), escalate when depth >50 items
- [ ] Implement `QueueMonitorJob` as BackgroundService — periodic monitoring (default 60s interval) with structured Serilog logging of queue metrics
- [ ] Register all services and background workers in `Program.cs` DI; add `AiQueue` configuration section to `appsettings.json` with ConcurrencyLimit, MaxRetries, RetryBaseDelaySeconds, MonitorIntervalSeconds, StaleWarningMinutes, EscalationDepthThreshold
