# Task - task_001_be_concurrency_resilience_infrastructure

## Requirement Reference

- User Story: us_082
- Story Location: .propel/context/tasks/EP-015/us_082/us_082.md
- Acceptance Criteria:
  - AC-2: Given the database connection pool is configured, When concurrent requests arrive, Then connection pooling limits usage to max 100 concurrent connections with queuing for overflow.
  - AC-3: Given AI workloads are triggered, When multiple parsing/coding requests arrive simultaneously, Then they are processed asynchronously via Redis queue without blocking HTTP request threads.
- Edge Case:
  - What happens when connection pool is exhausted (100 connections all in use)? New requests wait in an in-memory queue with a 30-second timeout; if exceeded, HTTP 503 is returned.
  - How does the system handle sudden traffic spikes (e.g., 2000 concurrent users)? Circuit breakers activate for non-critical endpoints while critical paths (authentication, booking) remain available.

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
| Backend | Entity Framework Core | 8.x |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | Polly | 8.x |
| Library | System.Threading.Channels | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic infrastructure for connection pool hardening, async AI queue orchestration, and circuit breaker resilience. No LLM inference involved.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement three concurrency and resilience layers to support 1000+ concurrent users per NFR-003 and NFR-028: (1) **Connection pool hardening** — enhance the existing Npgsql connection pool configuration (established in US_003) with pool exhaustion monitoring, a 30-second queuing timeout for overflow requests, and HTTP 503 responses when the timeout expires (edge case 1); (2) **Async AI workload orchestration** — implement a `BackgroundAiQueueProcessor` that drains AI parsing and coding requests from the Redis queue without blocking HTTP request threads, with configurable concurrency limits and back-pressure signals to prevent queue overflow; (3) **Endpoint circuit breaker for traffic spikes** — implement Polly-based circuit breaker policies that activate for non-critical endpoints (dashboard views, history lookups, reporting) during traffic spikes while preserving critical paths (authentication, booking) (edge case 2). All components emit metrics to `IPerformanceTracker` (US_081 task_001) and log events via Serilog for observability.

## Dependent Tasks

- US_003 — Requires PostgreSQL with connection pooling (max 100 connections) and EF Core configured.
- US_004 — Requires Redis infrastructure for async queue processing.
- US_081 task_001_be_performance_instrumentation_alerting — Requires `IPerformanceTracker` for pool utilization and circuit breaker metrics.

## Impacted Components

- **NEW** `src/UPACIP.Service/Infrastructure/IConnectionPoolMonitor.cs` — Interface defining GetPoolUtilizationAsync, IsPoolExhaustedAsync methods
- **NEW** `src/UPACIP.Service/Infrastructure/ConnectionPoolMonitor.cs` — Npgsql pool statistics monitoring with exhaustion detection
- **NEW** `src/UPACIP.Service/Infrastructure/BackgroundAiQueueProcessor.cs` — BackgroundService: Redis queue consumer with configurable concurrency for AI workloads
- **NEW** `src/UPACIP.Service/Infrastructure/Models/ConcurrencyOptions.cs` — Configuration: MaxDbConnections, PoolQueueTimeoutSeconds, AiQueueConcurrency, CircuitBreakerThreshold
- **NEW** `src/UPACIP.Api/Middleware/ConnectionPoolGuardMiddleware.cs` — Middleware: detect pool exhaustion and return HTTP 503 with Retry-After
- **NEW** `src/UPACIP.Api/Middleware/EndpointCircuitBreakerMiddleware.cs` — Middleware: Polly circuit breaker for non-critical endpoints during traffic spikes
- **NEW** `src/UPACIP.Service/Infrastructure/Models/EndpointClassification.cs` — Enum: Critical, Standard, NonCritical with route-to-classification mapping
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register all infrastructure services, add middlewares, bind ConcurrencyOptions
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Concurrency configuration section with pool and circuit breaker settings

## Implementation Plan

1. **Configure connection pool hardening (AC-2, edge case 1)**: Enhance the existing Npgsql connection string (from US_003) with explicit pool parameters. Update the connection string in `appsettings.json` to include: `Maximum Pool Size=100;Minimum Pool Size=10;Connection Idle Lifetime=300;Connection Pruning Interval=10;Timeout=30`. The `Timeout=30` parameter implements the 30-second queue timeout — when all 100 connections are in use, new connection requests wait up to 30 seconds in Npgsql's internal queue before throwing `NpgsqlException` with "The connection pool has been exhausted". The `Minimum Pool Size=10` keeps warm connections ready for burst traffic. The `Connection Idle Lifetime=300` (5 minutes) prunes idle connections to free server resources during low-traffic periods.

2. **Implement `ConnectionPoolMonitor`**: Create `IConnectionPoolMonitor` / `ConnectionPoolMonitor` that queries Npgsql pool statistics via `NpgsqlConnection.GetDataSourceStatistics()` (available in Npgsql 8.x). Expose: `int ActiveConnections` (currently in-use), `int IdleConnections` (available in pool), `int TotalConnections` (active + idle), `float UtilizationPercentage` (active / max × 100). Add `IsPoolExhaustedAsync` returning true when utilization exceeds 90% (configurable threshold). Register as a singleton and expose metrics via `IPerformanceTracker.RecordLatency("db.pool_utilization", utilizationPercentage)` on each check. Log a warning when utilization exceeds 80%: "Connection pool utilization {Utilization}% — approaching limit of {MaxConnections}."

3. **Implement `ConnectionPoolGuardMiddleware` (edge case 1)**: Create ASP.NET Core middleware that checks pool utilization before processing requests. On each request: (a) call `IConnectionPoolMonitor.IsPoolExhaustedAsync`; (b) if pool is not exhausted (< 90%), pass through to `next(context)`; (c) if pool is exhausted, attempt to wait by allowing the request to proceed (Npgsql's internal 30s queue handles the actual waiting); (d) wrap the downstream call in a try-catch for `NpgsqlException` with "connection pool" in the message — if caught, return HTTP 503 Service Unavailable with body `{ "error": "Service temporarily unavailable — database capacity exceeded", "retryAfterSeconds": 5 }` and `Retry-After: 5` header; (e) log the 503 at `Warning` level with correlation ID and pool utilization snapshot. This ensures graceful degradation instead of unhandled exceptions propagating to the client.

4. **Implement async AI workload processing (AC-3)**: Create `BackgroundAiQueueProcessor` as a `BackgroundService` that continuously reads AI workload requests from the Redis queue (`ai:workload:queue`). The processor uses a `SemaphoreSlim` with configurable concurrency (default: `Environment.ProcessorCount * 2`, max: 20) to limit simultaneous AI processing. On each dequeue: (a) deserialize the job payload (document ID + job type); (b) acquire a semaphore slot (non-blocking — if full, the item stays in the queue for the next tick); (c) spawn a `Task.Run` to process the job (calling `DocumentParsingWorker` or medical coding service depending on job type); (d) on completion, release the semaphore and acknowledge the message. This ensures AI workloads never block HTTP request threads — the upload endpoint returns `202 Accepted` immediately after enqueuing, and the background processor handles actual AI inference asynchronously per Decision #2.

5. **Implement back-pressure signaling**: When the Redis queue depth exceeds a configurable threshold (default: 100 pending jobs), the `BackgroundAiQueueProcessor` emits a back-pressure signal. The upload/coding endpoints check this signal before enqueuing new work: if back-pressure is active, return HTTP 429 with `Retry-After: 30` and message "AI processing queue is full. Please retry shortly." This prevents unbounded queue growth under sustained load. Monitor queue depth via `IPerformanceTracker.RecordLatency("ai.queue_depth", depth)`.

6. **Implement endpoint circuit breaker (edge case 2)**: Create `EndpointCircuitBreakerMiddleware` using Polly's `CircuitBreakerPolicy`. Classify endpoints into three categories via `EndpointClassification`:
   - **Critical** (never circuit-broken): `/api/auth/*`, `/api/appointments/book*`, `/health`, `/ready`.
   - **Standard** (circuit-broken at high threshold): `/api/appointments/*` (non-booking), `/api/documents/*`, `/api/coding/*`.
   - **NonCritical** (circuit-broken at low threshold): `/api/admin/*`, `/api/dashboard/*`, `/api/reports/*`, `/api/history/*`.
   Configure two Polly `CircuitBreakerPolicy` instances:
   - **Standard**: Break after 10 consecutive failures or when average response time exceeds 5s over a 30-second sampling window. Half-open after 15 seconds.
   - **NonCritical**: Break after 5 consecutive failures or when average response time exceeds 3s over a 30-second sampling window. Half-open after 30 seconds.
   When a circuit is open, return HTTP 503 with `{ "error": "Service temporarily throttled to protect critical operations", "retryAfterSeconds": N }`. Log circuit state transitions (Closed → Open → HalfOpen → Closed) at `Warning` level.

7. **Configure endpoint classification mapping**: Store endpoint classification rules in `appsettings.json` under a `CircuitBreaker` section:
   ```json
   "CircuitBreaker": {
     "CriticalPaths": ["/api/auth", "/api/appointments/book", "/health", "/ready"],
     "StandardFailureThreshold": 10,
     "StandardResponseTimeThresholdMs": 5000,
     "NonCriticalFailureThreshold": 5,
     "NonCriticalResponseTimeThresholdMs": 3000,
     "HalfOpenRetrySeconds": 15
   }
   ```
   The middleware resolves endpoint classification by matching the request path against `CriticalPaths` — if matched, skip circuit breaker entirely. Otherwise, classify by route prefix convention: `/api/admin/*`, `/api/dashboard/*`, `/api/reports/*` → NonCritical; everything else → Standard. Bind via `IOptions<CircuitBreakerOptions>`.

8. **Register services and configure middleware ordering**: Add registrations in `Program.cs`: `services.AddSingleton<IConnectionPoolMonitor, ConnectionPoolMonitor>()`, `services.AddHostedService<BackgroundAiQueueProcessor>()`, bind `ConcurrencyOptions` and `CircuitBreakerOptions`. Add middlewares in order: `ConnectionPoolGuardMiddleware` → `EndpointCircuitBreakerMiddleware` → existing middlewares (authentication, rate limiting, routing). This ensures pool exhaustion is caught first (fast 503), then circuit breaker evaluation, before reaching business logic. Add `Concurrency` section to `appsettings.json`:
   ```json
   "Concurrency": {
     "MaxDbConnections": 100,
     "PoolExhaustionThresholdPercent": 90,
     "PoolQueueTimeoutSeconds": 30,
     "AiQueueConcurrency": 10,
     "AiQueueBackPressureThreshold": 100
   }
   ```

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AppointmentController.cs
│   │   │   ├── DocumentController.cs
│   │   │   └── Admin/
│   │   ├── Middleware/
│   │   │   ├── AiRateLimitingMiddleware.cs         ← from US_079
│   │   │   └── PerformanceInstrumentationMiddleware.cs ← from US_081
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Performance/
│   │   │   ├── IPerformanceTracker.cs              ← from US_081
│   │   │   ├── PerformanceTracker.cs               ← from US_081
│   │   │   ├── ISlaMonitorService.cs               ← from US_081
│   │   │   ├── SlaMonitorService.cs                ← from US_081
│   │   │   ├── IPriorityRequestQueue.cs            ← from US_081
│   │   │   ├── PriorityRequestQueue.cs             ← from US_081
│   │   │   └── Models/
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs                    ← from US_004
│   │   │   └── RedisCacheService.cs                ← from US_004
│   │   ├── AiSafety/
│   │   ├── AiTesting/
│   │   ├── AiAudit/
│   │   ├── VectorSearch/
│   │   └── Rag/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   ├── Services/
│   │   ├── IAppointmentBookingService.cs
│   │   ├── AppointmentBookingService.cs
│   │   └── AppointmentSlotCacheService.cs
│   └── AI/
│       ├── AiGatewayService.cs                     ← from US_067
│       └── DocumentParsing/
│           ├── DocumentParsingWorker.cs             ← from US_039
│           └── DocumentParsingDispatcher.cs         ← from US_039
├── app/
├── config/
└── scripts/
```

> Assumes US_003 (PostgreSQL + connection pooling), US_004 (Redis), US_067 (AI Gateway), US_039 (document parsing pipeline), US_081 (performance instrumentation + optimization) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Infrastructure/IConnectionPoolMonitor.cs | Interface: GetPoolUtilizationAsync, IsPoolExhaustedAsync |
| CREATE | src/UPACIP.Service/Infrastructure/ConnectionPoolMonitor.cs | Npgsql pool statistics, utilization tracking, exhaustion detection |
| CREATE | src/UPACIP.Service/Infrastructure/BackgroundAiQueueProcessor.cs | BackgroundService: Redis queue consumer with SemaphoreSlim concurrency |
| CREATE | src/UPACIP.Service/Infrastructure/Models/ConcurrencyOptions.cs | Config: MaxDbConnections, PoolQueueTimeoutSeconds, AiQueueConcurrency |
| CREATE | src/UPACIP.Service/Infrastructure/Models/EndpointClassification.cs | Enum: Critical, Standard, NonCritical |
| CREATE | src/UPACIP.Api/Middleware/ConnectionPoolGuardMiddleware.cs | Pool exhaustion detection → HTTP 503 with Retry-After |
| CREATE | src/UPACIP.Api/Middleware/EndpointCircuitBreakerMiddleware.cs | Polly circuit breaker for Standard/NonCritical endpoints |
| MODIFY | src/UPACIP.Api/Program.cs | Register infrastructure services, add middlewares, bind options |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Concurrency and CircuitBreaker configuration sections |

## External References

- [Npgsql Connection Pooling](https://www.npgsql.org/doc/connection-string-parameters.html#pooling)
- [Npgsql Pool Statistics — GetDataSourceStatistics](https://www.npgsql.org/doc/diagnostics/metrics.html)
- [Polly Circuit Breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [ASP.NET Core Middleware Pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [BackgroundService — IHostedService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [SemaphoreSlim — Concurrency Throttling](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [HTTP 503 Service Unavailable — RFC 9110](https://www.rfc-editor.org/rfc/rfc9110#status.503)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Connection pool monitor reports correct active/idle/total connection counts
- [ ] When 100+ concurrent DB requests arrive, overflow requests queue for up to 30 seconds
- [ ] After 30-second queue timeout, HTTP 503 is returned with Retry-After header
- [ ] AI workload enqueue returns 202 Accepted immediately without blocking HTTP thread
- [ ] BackgroundAiQueueProcessor respects concurrency limit (max 10 simultaneous AI jobs)
- [ ] Back-pressure signal activates when Redis queue depth exceeds 100 pending jobs
- [ ] Circuit breaker opens for NonCritical endpoints after 5 consecutive failures
- [ ] Critical endpoints (auth, booking) are never circuit-broken
- [ ] Circuit breaker transitions from Open to HalfOpen after configured seconds
- [ ] Pool utilization metrics are emitted to IPerformanceTracker

## Implementation Checklist

- [ ] Create `ConcurrencyOptions` and `EndpointClassification` models in `src/UPACIP.Service/Infrastructure/Models/`
- [ ] Implement `IConnectionPoolMonitor` / `ConnectionPoolMonitor` with Npgsql pool statistics and exhaustion detection
- [ ] Implement `ConnectionPoolGuardMiddleware` with 503 response on pool exhaustion timeout
- [ ] Implement `BackgroundAiQueueProcessor` with SemaphoreSlim-throttled Redis queue consumption
- [ ] Implement back-pressure signaling when AI queue depth exceeds threshold
- [ ] Implement `EndpointCircuitBreakerMiddleware` with Polly policies for Standard and NonCritical endpoints
- [ ] Add endpoint classification mapping in `appsettings.json` CircuitBreaker section
- [ ] Register all services in DI and configure middleware ordering in Program.cs
