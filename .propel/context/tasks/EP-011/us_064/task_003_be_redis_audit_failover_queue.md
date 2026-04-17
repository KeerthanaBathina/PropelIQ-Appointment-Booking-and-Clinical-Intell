# Task - task_003_be_redis_audit_failover_queue

## Requirement Reference

- User Story: US_064
- Story Location: .propel/context/tasks/EP-011/us_064/us_064.md
- Acceptance Criteria:
    - AC-1: **Given** any data is accessed, created, modified, or deleted, **When** the operation completes, **Then** an immutable audit log entry is created with user ID, action type, entity type, entity ID, timestamp, and IP address.
- Edge Cases:
    - How does the system handle audit logging when the database is temporarily unavailable? System queues log entries in Redis and flushes them to PostgreSQL when connectivity restores.

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
| Frontend | N/A | - |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | Polly (Resilience) | 8.x |
| Library | Serilog (Structured Logging) | 8.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Implement a Redis-backed failover queue for audit log entries to guarantee zero audit data loss during PostgreSQL outages. When `AuditService` (task_001) fails to write an audit entry to the database, it delegates to `IAuditQueueService` which serializes the audit entry and pushes it to a Redis list (`audit:failover:queue`). A .NET `BackgroundService` (`AuditQueueFlushWorker`) continuously monitors the queue and flushes entries back to PostgreSQL once connectivity restores. The worker implements a circuit breaker pattern using Polly to detect DB unavailability and exponential backoff for retry logic (NFR-032). Duplicate detection is handled via the `LogId` UUID — entries with an existing `LogId` in the database are silently skipped during flush. This ensures HIPAA audit trail completeness (FR-093, NFR-012) even during infrastructure failures, and aligns with Architecture Decision #2 (Asynchronous Processing) and Decision #7 (Redis for distributed caching and queuing).

## Dependent Tasks

- task_001_be_audit_log_service_query_api — Requires `IAuditService` which delegates to `IAuditQueueService` on DB write failure
- US_008 (task_001_be_domain_entity_models) — Requires `AuditLog` entity and `AuditAction` enum for serialization
- US_004 — Requires Redis infrastructure (Upstash Redis 7.x) connectivity

## Impacted Components

- **NEW** `Server/Services/IAuditQueueService.cs` — Interface for Redis audit queue operations (enqueue, dequeue, count)
- **NEW** `Server/Services/AuditQueueService.cs` — Redis list-backed implementation using `IDistributedCache` or `IConnectionMultiplexer`
- **NEW** `Server/Workers/AuditQueueFlushWorker.cs` — `BackgroundService` that flushes queued entries to PostgreSQL
- **NEW** `Server/Models/DTOs/AuditLogQueueEntry.cs` — Serializable DTO for Redis queue entries
- **NEW** `Server/Models/Configuration/AuditQueueSettings.cs` — Configuration POCO for queue settings
- **MODIFY** `Server/Services/AuditService.cs` — Add `IAuditQueueService` dependency for DB failure fallback
- **MODIFY** `Server/Program.cs` — Register `IAuditQueueService`, `AuditQueueFlushWorker`, and `AuditQueueSettings`

## Implementation Plan

1. **Create AuditLogQueueEntry DTO** — Create a serializable DTO `AuditLogQueueEntry` containing all fields needed to reconstruct an `AuditLog` entity: `LogId` (Guid), `UserId` (Guid), `Action` (string — serialized enum name), `ResourceType` (string), `ResourceId` (Guid), `Timestamp` (DateTime), `IpAddress` (string), `UserAgent` (string), `EnqueuedAt` (DateTime — when the entry was queued for monitoring staleness). Use `System.Text.Json` serialization with `JsonSerializerOptions` configured for camelCase property naming. The DTO must be separate from the EF Core entity to avoid serialization issues with navigation properties and EF proxies.

2. **Create IAuditQueueService / AuditQueueService** — Implement the Redis queue interface with three methods:
   - `Task EnqueueAsync(AuditLogQueueEntry entry)` — Serializes the entry to JSON and pushes to Redis list key `audit:failover:queue` using `RPUSH`. Uses `IConnectionMultiplexer` from StackExchange.Redis (already part of Upstash Redis integration).
   - `Task<AuditLogQueueEntry?> DequeueAsync()` — Pops from the head of the list using `LPOP` and deserializes. Returns null if queue is empty.
   - `Task<long> GetQueueDepthAsync()` — Returns `LLEN audit:failover:queue` for monitoring and health checks.
   All operations include structured Serilog logging: `Log.Warning("Audit entry {LogId} queued to Redis failover", entry.LogId)`. Handle Redis connection failures gracefully — if Redis is also unavailable, log a critical error `Log.Fatal("CRITICAL: Both PostgreSQL and Redis unavailable for audit logging. Entry {LogId} at risk of loss.", entry.LogId)` and write the entry to a local file fallback at `logs/audit-failover-{date}.json` as a last resort (defense-in-depth).

3. **Integrate AuditQueueService into AuditService** — Modify `AuditService.LogAsync()` (from task_001) to inject `IAuditQueueService`. In the `catch` block where DB write failure is handled, instead of only logging the error, also call `await _auditQueueService.EnqueueAsync(new AuditLogQueueEntry { ... })` to persist the entry in Redis. The flow becomes: try DB write → on `DbUpdateException` or `NpgsqlException`, catch → enqueue to Redis → log warning → return without throwing (caller unaffected). This ensures the calling transaction is never disrupted by audit write failures while guaranteeing eventual persistence.

4. **Create AuditQueueFlushWorker (BackgroundService)** — Implement a .NET `BackgroundService` that runs continuously:
   - On each iteration, check queue depth via `IAuditQueueService.GetQueueDepthAsync()`.
   - If queue depth > 0, attempt to flush entries in batches of configurable size (default: 100).
   - For each dequeued `AuditLogQueueEntry`, map to `AuditLog` entity and call `_context.AuditLogs.Add(entry)`.
   - Call `SaveChangesAsync()` per batch within a transaction for atomicity.
   - If DB write succeeds, the entry is permanently persisted. If DB write fails, re-enqueue the entry back to Redis (RPUSH) and wait before retrying.
   - Between iterations, use `Task.Delay(flushIntervalMs)` (configurable, default: 5000ms / 5 seconds).
   - When queue is empty, use longer delay (default: 30000ms / 30 seconds) to reduce Redis polling.

5. **Implement Circuit Breaker with Polly** — Configure a Polly `CircuitBreakerAsync` policy for the DB write operations in `AuditQueueFlushWorker`:
   - Break after 5 consecutive `NpgsqlException` or `DbUpdateException` failures.
   - Circuit open duration: 30 seconds (aligned with AIR-O04 pattern).
   - When circuit is open, skip flush attempts and log `Log.Warning("Audit flush circuit breaker OPEN — DB unavailable, retrying in {Duration}s", 30)`.
   - When circuit transitions to half-open, attempt a single flush to probe DB availability.
   - When circuit closes, resume normal batch flushing.
   - Use Polly `RetryAsync` wrapped inside the circuit breaker: 3 retries with exponential backoff (1s, 2s, 4s) per NFR-032.

6. **Implement Idempotent Flush (Duplicate Detection)** — Before inserting a dequeued entry into the database, check if `LogId` already exists: `bool exists = await _context.AuditLogs.AnyAsync(a => a.LogId == entry.LogId)`. If it exists, skip the insert silently (idempotent — the entry was already persisted, possibly by a retry). Log: `Log.Information("Audit entry {LogId} already exists in DB, skipping duplicate", entry.LogId)`. This ensures that re-enqueued entries (from failed batch flushes) are not duplicated in the audit trail. The `LogId` UUID primary key enforces uniqueness at the database level as a safety net (NFR-034 idempotent operations).

7. **Health Check Integration** — Create `AuditQueueHealthCheck` implementing `IHealthCheck` (ASP.NET Core health checks). The health check queries `IAuditQueueService.GetQueueDepthAsync()`:
   - Queue depth = 0: return `HealthCheckResult.Healthy("Audit queue empty — all entries persisted")`
   - Queue depth 1-100: return `HealthCheckResult.Degraded($"Audit queue has {depth} pending entries — DB may be recovering")`
   - Queue depth > 100: return `HealthCheckResult.Unhealthy($"Audit queue has {depth} pending entries — DB outage suspected")`
   Register the health check in `Program.cs` with tag `"audit"` so it appears in `/health` endpoint (NFR-020). This provides operational visibility into audit system health.

8. **Configuration and Service Registration** — Create `AuditQueueSettings` POCO with: `FlushBatchSize` (int, default 100), `FlushIntervalMs` (int, default 5000), `IdleIntervalMs` (int, default 30000), `CircuitBreakerFailureThreshold` (int, default 5), `CircuitBreakerDurationSeconds` (int, default 30), `MaxRetryAttempts` (int, default 3), `RedisQueueKey` (string, default "audit:failover:queue"), `LocalFallbackPath` (string, default "logs/audit-failover-{date}.json"). Bind from `appsettings.json` section `AuditQueue` using `IOptions<AuditQueueSettings>`. Register in `Program.cs`: `IAuditQueueService` / `AuditQueueService` as Singleton, `AuditQueueFlushWorker` as `AddHostedService<>`, `AuditQueueHealthCheck` with `.AddCheck<>`.

## Current Project State

- [Placeholder — to be updated based on completion of dependent tasks task_001 and US_004]

```text
Server/
├── Controllers/
│   └── AuditLogController.cs (from task_001)
├── Services/
│   ├── IAuditService.cs (from task_001)
│   ├── AuditService.cs (from task_001 — will be modified)
│   ├── IAuditLogQueryService.cs (from task_001)
│   └── AuditLogQueryService.cs (from task_001)
├── Workers/
├── Models/
│   ├── DTOs/
│   ├── Entities/
│   │   └── AuditLog.cs
│   └── Configuration/
│       └── AuditSettings.cs (from task_001)
├── Data/
│   └── ApplicationDbContext.cs
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IAuditQueueService.cs | Interface with EnqueueAsync, DequeueAsync, GetQueueDepthAsync methods |
| CREATE | Server/Services/AuditQueueService.cs | Redis list-backed implementation with local file fallback |
| CREATE | Server/Workers/AuditQueueFlushWorker.cs | BackgroundService flushing Redis queue to PostgreSQL with Polly circuit breaker |
| CREATE | Server/Models/DTOs/AuditLogQueueEntry.cs | Serializable DTO for Redis queue entries with System.Text.Json |
| CREATE | Server/Models/Configuration/AuditQueueSettings.cs | Configuration POCO for queue batch size, intervals, circuit breaker thresholds |
| CREATE | Server/HealthChecks/AuditQueueHealthCheck.cs | IHealthCheck reporting queue depth as Healthy/Degraded/Unhealthy |
| MODIFY | Server/Services/AuditService.cs | Add IAuditQueueService injection, enqueue on DB failure in catch block |
| MODIFY | Server/Program.cs | Register IAuditQueueService, AuditQueueFlushWorker, AuditQueueHealthCheck, AuditQueueSettings |
| MODIFY | Server/appsettings.json | Add AuditQueue configuration section |

## External References

- [StackExchange.Redis — Lists (RPUSH/LPOP)](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [ASP.NET Core BackgroundService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [Polly Circuit Breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [Polly Retry with Exponential Backoff](https://github.com/App-vNext/Polly/wiki/Retry)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0)
- [Upstash Redis .NET Integration](https://upstash.com/docs/redis/sdks/dotnet)
- [System.Text.Json Serialization (.NET 8)](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] `AuditQueueService.EnqueueAsync` successfully pushes entries to Redis list
- [ ] `AuditQueueService.DequeueAsync` correctly deserializes entries from Redis
- [ ] `AuditService.LogAsync` enqueues to Redis when DB write throws NpgsqlException
- [ ] `AuditQueueFlushWorker` flushes queued entries to PostgreSQL on DB recovery
- [ ] Polly circuit breaker opens after 5 consecutive DB failures and retries after 30s
- [ ] Duplicate entries (same LogId) are silently skipped during flush (NFR-034)
- [ ] Health check reports Degraded when queue depth 1-100, Unhealthy when >100
- [ ] Local file fallback activates when both PostgreSQL and Redis are unavailable
- [ ] Calling transaction is never disrupted by audit write failures

## Implementation Checklist

- [ ] Create `AuditLogQueueEntry` DTO with all audit log fields plus `EnqueuedAt` timestamp, using `System.Text.Json` serialization
- [ ] Create `IAuditQueueService` / `AuditQueueService` with Redis `RPUSH`/`LPOP`/`LLEN` operations and local file fallback on Redis failure
- [ ] Modify `AuditService.LogAsync()` to inject `IAuditQueueService` and enqueue entries on `NpgsqlException`/`DbUpdateException` (edge case: DB unavailable)
- [ ] Create `AuditQueueFlushWorker` as `BackgroundService` with configurable batch size, flush interval, and idle interval
- [ ] Implement Polly `CircuitBreakerAsync` (5 failures, 30s open) wrapping `RetryAsync` (3 retries, exponential backoff) for DB writes in worker (NFR-032)
- [ ] Implement idempotent flush with `LogId` existence check before insert to prevent duplicate audit entries (NFR-034)
- [ ] Create `AuditQueueHealthCheck` reporting queue depth via `/health` endpoint with Healthy/Degraded/Unhealthy thresholds (NFR-020)
- [ ] Register all services, worker, health check, and `AuditQueueSettings` configuration in `Program.cs` with `IOptions<AuditQueueSettings>` binding
