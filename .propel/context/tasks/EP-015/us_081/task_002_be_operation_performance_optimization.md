# Task - task_002_be_operation_performance_optimization

## Requirement Reference

- User Story: us_081
- Story Location: .propel/context/tasks/EP-015/us_081/us_081.md
- Acceptance Criteria:
  - AC-1: Given a patient submits a booking request, When the request is processed, Then the end-to-end response completes within 2 seconds at P95.
  - AC-2: Given a clinical document is uploaded, When AI parsing runs, Then extraction completes within 30 seconds at P95.
  - AC-3: Given AI medical coding is triggered, When codes are generated, Then the coding response completes within 5 seconds per diagnosis/procedure at P95.
- Edge Case:
  - What happens when performance degrades under peak load? System prioritizes booking requests over parsing and coding via request priority queues.

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
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | System.Threading.Channels | 8.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic performance optimizations for booking, parsing, and coding operations. It tunes existing services (caching, query optimization, priority queuing) without LLM inference.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement targeted performance optimizations for the three SLA-governed operations: booking (<2s P95), document parsing (<30s P95), and medical coding (<5s P95), plus a request priority queuing mechanism to maintain booking responsiveness under peak load (edge case). The optimizations focus on four layers: (1) **Booking path optimization** — add Redis-cached slot availability lookups, optimize the EF Core booking query with compiled queries, and reduce round-trips by batching validation + insert in a single transaction; (2) **Document parsing optimization** — add per-document timeout enforcement (25s hard limit with graceful abort), implement streaming chunk processing (process chunks as OCR completes rather than waiting for full document), and tune Redis queue concurrency; (3) **Medical coding optimization** — add Redis-cached code library lookups (hot ICD-10/CPT codes), implement parallel diagnosis/procedure coding, and set AI Gateway timeout of 4s for coding requests; (4) **Priority queuing under peak load (edge case)** — implement a `PriorityRequestQueue` that categorizes incoming requests into Critical (booking), Normal (coding), and Background (parsing) priorities, applying concurrency limits per category to ensure booking requests are never starved by long-running AI operations. All optimizations are instrumented via `IPerformanceTracker` (task_001) for validation.

## Dependent Tasks

- US_081 task_001_be_performance_instrumentation_alerting — Requires `IPerformanceTracker` for measuring optimization impact.
- US_001 — Requires backend API scaffold.
- US_004 — Requires Redis caching infrastructure.
- US_067 — Requires AI Gateway for timeout configuration.

## Impacted Components

- **NEW** `src/UPACIP.Service/Performance/IPriorityRequestQueue.cs` — Interface defining EnqueueAsync, DequeueAsync with priority categories
- **NEW** `src/UPACIP.Service/Performance/PriorityRequestQueue.cs` — Channel-based multi-priority queue with per-category concurrency limits
- **NEW** `src/UPACIP.Service/Performance/Models/RequestPriority.cs` — Enum: Critical, Normal, Background
- **NEW** `src/UPACIP.Service/Performance/Models/PriorityQueueOptions.cs` — Configuration: CriticalConcurrency, NormalConcurrency, BackgroundConcurrency
- **MODIFY** `Server/Services/AppointmentBookingService.cs` — Add compiled EF Core query for slot availability, Redis-cached slot check, batched validation + insert
- **MODIFY** `Server/Services/AppointmentSlotCacheService.cs` — Add pre-warming for high-demand time slots, reduce cache miss penalty
- **MODIFY** `Server/AI/DocumentParsing/DocumentParsingWorker.cs` — Add 25s per-document timeout with CancellationToken, streaming chunk processing
- **MODIFY** `Server/AI/AiGatewayService.cs` — Add per-operation timeout configuration (4s coding, 25s parsing), priority-aware request routing
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IPriorityRequestQueue, bind PriorityQueueOptions
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add PriorityQueue and per-operation timeout configuration sections

## Implementation Plan

1. **Optimize booking path for <2s P95 (AC-1)**: Apply three optimizations to the appointment booking flow:
   - **Compiled EF Core query**: Create a compiled query using `EF.CompileAsyncQuery` for the slot availability check: `SELECT 1 FROM appointments WHERE provider_id = @providerId AND appointment_time = @time AND status != 'Cancelled'`. Compiled queries eliminate expression tree compilation overhead (~5-10ms per query). Use for the hot-path uniqueness check before booking insert.
   - **Redis-cached slot availability**: Before hitting the database, check Redis cache key `slot:{providerId}:{date}` for pre-computed available slots. On cache hit (expected >80% per NFR-004), skip the DB query entirely. On cache miss, query DB, populate cache with 5-minute TTL (per NFR-030), and proceed. Invalidate specific slot keys on booking/cancellation.
   - **Batched transaction**: Combine slot validation, appointment insert, and notification queue enqueue in a single EF Core `SaveChangesAsync` call with `BeginTransactionAsync`. Reduces DB round-trips from 3 (check, insert, audit) to 1 transactional batch. Use `ExecutionStrategy` with retry for transient failures.
   Target: eliminate ~500ms from the current path by removing redundant DB round-trips and leveraging cache.

2. **Optimize document parsing path for <30s P95 (AC-2)**: Apply three optimizations to the document parsing pipeline:
   - **Per-document timeout enforcement**: Wrap the entire parsing pipeline in a `CancellationTokenSource` with a 25s timeout (5s buffer below the 30s SLA). If OCR + extraction exceeds 25s, cancel the in-flight AI request, mark the document as `status: timeout`, and queue for retry with a simplified extraction prompt (fewer fields). Log timeout events for capacity planning.
   - **Streaming chunk processing**: Instead of waiting for the full document OCR to complete before starting extraction, process chunks as they become available. Use `Channel<string>` to pipeline OCR output to the AI extraction stage. The OCR producer writes text chunks to the channel as each page completes; the extraction consumer processes chunks in parallel (max 3 concurrent AI calls). This overlaps OCR and AI latency.
   - **Redis queue concurrency tuning**: Configure `DocumentParsingDispatcher` to limit concurrent parsing workers to `Environment.ProcessorCount` (avoid thread starvation from I/O-bound AI calls). Use `SemaphoreSlim` with configurable max concurrency from `appsettings.json`.

3. **Optimize medical coding path for <5s P95 (AC-3)**: Apply three optimizations to the medical coding pipeline:
   - **Redis-cached code library**: Cache frequently used ICD-10/CPT code descriptions in Redis with key pattern `code:{codeSystem}:{codeValue}` and 24-hour TTL. Pre-warm the cache on application startup with the top 500 most-used codes. On coding requests, first check cache for code validation/description lookup before querying pgvector or the code library table. Expected cache hit ratio >90% for common diagnoses.
   - **Parallel diagnosis/procedure coding**: When a document contains multiple diagnoses/procedures, code them in parallel using `Task.WhenAll` instead of sequentially. Each coding request is independent (separate prompt to the AI Gateway). With 3 diagnoses, parallel coding reduces wall-clock time from 3 × 4s = 12s to max(4s, 4s, 4s) = 4s.
   - **AI Gateway coding timeout**: Configure a per-operation timeout of 4s for medical coding requests in the AI Gateway (distinct from the 25s parsing timeout). If the primary model (GPT-4o-mini) doesn't respond within 3.5s, trigger immediate fallback to Claude (500ms budget for fallback). This ensures the 5s SLA is met even when the primary model is slow.

4. **Implement priority request queue (edge case)**: Create `IPriorityRequestQueue` / `PriorityRequestQueue` using three bounded `Channel<Func<Task>>` instances (one per priority level). Configure per-category concurrency limits via `PriorityQueueOptions` from `appsettings.json`:
   ```json
   "PriorityQueue": {
     "CriticalConcurrency": 20,
     "NormalConcurrency": 10,
     "BackgroundConcurrency": 5
   }
   ```
   Each priority level has a dedicated consumer pool (`Task.Run` loop draining the channel up to the concurrency limit via `SemaphoreSlim`). Under peak load, booking requests (Critical) get 20 concurrent slots, medical coding (Normal) gets 10, and document parsing (Background) gets 5. When the system is under low load, unused slots from higher priorities are not shared downward (prevents priority inversion). The queue maintains strict ordering within each priority level (FIFO).

5. **Integrate priority classification into request pipeline**: Modify the AI Gateway service to route requests through the priority queue based on request type metadata:
   - `RequestType == "booking"` → `Critical` priority (bypasses queue entirely — direct execution to ensure <2s SLA).
   - `RequestType == "medical-coding"` → `Normal` priority.
   - `RequestType == "document-parsing"` → `Background` priority.
   When the Background queue reaches capacity (channel full), new parsing requests are held in the Redis queue (back-pressure to the dispatcher level) rather than rejected. This ensures parsing requests are never lost, just delayed under peak load.

6. **Add slot cache pre-warming**: Modify `AppointmentSlotCacheService` to pre-warm Redis cache on application startup and on a periodic schedule (every 5 minutes). Pre-warming queries all available slots for the next 7 days and populates Redis keys `slot:{providerId}:{date}`. This ensures the first booking request after a cache expiry doesn't incur a cache-miss penalty. Use `IHostedService` for the periodic pre-warming job. Track cache hit ratio via `IPerformanceTracker` to validate the >80% target from NFR-004.

7. **Configure per-operation timeouts in AI Gateway**: Add an `AiOperationTimeouts` configuration section to `appsettings.json`:
   ```json
   "AiOperationTimeouts": {
     "DocumentParsing": 25000,
     "MedicalCoding": 4000,
     "ConversationalIntake": 3000,
     "Default": 10000
   }
   ```
   Modify `AiGatewayService` to read the timeout for the current operation type and apply it to the `HttpClient` request via `CancellationTokenSource.CreateLinkedTokenSource` with the operation-specific timeout. The circuit breaker (Polly) wraps inside the timeout — if the primary provider times out, the fallback provider gets the remaining time budget.

8. **Instrument all optimizations with PerformanceTracker**: Add `IPerformanceTracker.StartSpan` calls around each optimized code path:
   - `"booking.cache_check"` — Redis slot availability lookup.
   - `"booking.compiled_query"` — Compiled EF Core slot validation.
   - `"booking.transaction"` — Batched insert + audit.
   - `"parsing.ocr_chunk"` — Per-page OCR timing.
   - `"parsing.ai_extraction"` — AI extraction per chunk.
   - `"coding.cache_lookup"` — Redis code library lookup.
   - `"coding.parallel_inference"` — Parallel AI coding request.
   This enables span-level bottleneck identification per the edge case requirement. Log the pre-optimization and post-optimization P95 values during validation to confirm improvement.

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
│   │   │   └── PerformanceInstrumentationMiddleware.cs ← from task_001
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Performance/
│   │   │   ├── IPerformanceTracker.cs              ← from task_001
│   │   │   ├── PerformanceTracker.cs               ← from task_001
│   │   │   ├── ISlaMonitorService.cs               ← from task_001
│   │   │   ├── SlaMonitorService.cs                ← from task_001
│   │   │   ├── PerformanceMonitoringService.cs     ← from task_001
│   │   │   └── Models/
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs
│   │   │   └── RedisCacheService.cs
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

> Assumes task_001 (performance instrumentation), US_001 (backend scaffold), US_004 (Redis), US_067 (AI Gateway), US_039 (document parsing), and appointment booking services are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Performance/IPriorityRequestQueue.cs | Interface: EnqueueAsync, DequeueAsync with Critical/Normal/Background priorities |
| CREATE | src/UPACIP.Service/Performance/PriorityRequestQueue.cs | Multi-channel priority queue with per-category SemaphoreSlim concurrency limits |
| CREATE | src/UPACIP.Service/Performance/Models/RequestPriority.cs | Enum: Critical, Normal, Background |
| CREATE | src/UPACIP.Service/Performance/Models/PriorityQueueOptions.cs | Config: CriticalConcurrency, NormalConcurrency, BackgroundConcurrency |
| MODIFY | Server/Services/AppointmentBookingService.cs | Compiled EF Core query, Redis-cached slot check, batched transaction |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Pre-warming on startup and periodic schedule, cache hit ratio tracking |
| MODIFY | Server/AI/DocumentParsing/DocumentParsingWorker.cs | 25s timeout enforcement, streaming Channel-based chunk processing |
| MODIFY | Server/AI/AiGatewayService.cs | Per-operation timeout config, priority-aware routing, 4s coding timeout |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPriorityRequestQueue, bind PriorityQueueOptions and AiOperationTimeouts |
| MODIFY | src/UPACIP.Api/appsettings.json | Add PriorityQueue and AiOperationTimeouts configuration sections |

## External References

- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)
- [System.Threading.Channels — Bounded Channel](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [Redis Caching Patterns — Cache-Aside](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside)
- [Polly — Timeout Policy](https://github.com/App-vNext/Polly/wiki/Timeout)
- [CancellationTokenSource — Linked Tokens](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.createlinkedtokensource)
- [Task.WhenAll — Parallel Async](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenall)
- [SemaphoreSlim — Concurrency Throttling](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)

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
- [ ] Booking endpoint P95 latency <2000ms under simulated load (100 concurrent requests)
- [ ] Document parsing P95 latency <30000ms for 10-page PDF test documents
- [ ] Medical coding P95 latency <5000ms per diagnosis with parallel coding enabled
- [ ] Redis cache hit ratio for slot availability >80% after pre-warming
- [ ] Priority queue routes booking requests to Critical channel with 20 concurrent slots
- [ ] Under peak load, parsing requests (Background) are delayed but not rejected
- [ ] Per-document parsing timeout fires at 25s and marks document as timeout status
- [ ] AI Gateway coding timeout fires at 4s and triggers Claude fallback
- [ ] Compiled EF Core query eliminates expression tree recompilation overhead
- [ ] All optimization spans visible via IPerformanceTracker (booking.cache_check, coding.parallel_inference, etc.)

## Implementation Checklist

- [ ] Create `IPriorityRequestQueue` interface and `PriorityRequestQueue` with multi-channel priority queuing and SemaphoreSlim concurrency limits
- [ ] Optimize `AppointmentBookingService` with compiled query, Redis cache-aside slot check, and batched transaction
- [ ] Add slot cache pre-warming to `AppointmentSlotCacheService` with periodic 5-minute refresh
- [ ] Add 25s per-document timeout and streaming chunk processing to `DocumentParsingWorker`
- [ ] Add Redis-cached code library and parallel diagnosis/procedure coding to medical coding path
- [ ] Configure per-operation timeouts in `AiGatewayService` (4s coding, 25s parsing)
- [ ] Integrate priority classification into AI Gateway request routing
- [ ] Instrument all optimization paths with `IPerformanceTracker.StartSpan` for bottleneck visibility
