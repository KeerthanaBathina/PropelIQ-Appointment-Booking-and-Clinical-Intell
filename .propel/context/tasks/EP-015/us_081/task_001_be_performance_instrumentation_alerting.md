# Task - task_001_be_performance_instrumentation_alerting

## Requirement Reference

- User Story: us_081
- Story Location: .propel/context/tasks/EP-015/us_081/us_081.md
- Acceptance Criteria:
  - AC-4: Given performance is monitored, When any metric exceeds its SLA threshold, Then the system generates a performance alert with the affected operation, P95 value, and trend.
- Edge Case:
  - How does the system identify performance bottlenecks? Application Performance Monitoring (APM) traces all operations with span-level timing for database, cache, and external API calls.

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
| Library | System.Diagnostics.DiagnosticSource | 8.x |
| Library | Serilog | 4.x |
| Monitoring | Seq (Community Edition) | 2024.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic performance monitoring infrastructure. No LLM inference — it collects timing metrics and generates alerts when SLA thresholds are breached.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a performance instrumentation and alerting framework using .NET 8's built-in `System.Diagnostics` (`Activity`, `ActivitySource`, `Meter`) combined with Serilog structured logging. The framework provides three capabilities: (1) **APM instrumentation** with span-level tracing that decorates every operation (booking, document parsing, medical coding) with hierarchical spans capturing database, cache, and external API call timings (edge case); (2) **P95 metric collection** via a sliding window histogram that continuously tracks response latency for each operation category and computes P95 values over configurable windows; (3) **SLA threshold alerting** that compares computed P95 values against configured thresholds (booking <2s, parsing <30s, coding <5s) and generates structured alerts via Serilog with the affected operation, current P95 value, threshold, and trend direction (improving/degrading). The instrumentation uses `ActivitySource` for distributed tracing (compatible with OpenTelemetry if adopted later) and `System.Diagnostics.Metrics.Meter` for metrics, avoiding external APM dependencies in Phase 1 per the free-infrastructure constraint. A background `PerformanceMonitoringService` runs on a configurable interval (default: 60 seconds) to evaluate SLA compliance and emit alerts.

## Dependent Tasks

- US_001 — Requires backend API scaffold with middleware pipeline.
- US_004 — Requires Redis caching infrastructure (for cached metric state).

## Impacted Components

- **NEW** `src/UPACIP.Service/Performance/IPerformanceTracker.cs` — Interface defining StartOperation, RecordSpan, CompleteOperation methods
- **NEW** `src/UPACIP.Service/Performance/PerformanceTracker.cs` — ActivitySource-based APM instrumentation with span-level tracing
- **NEW** `src/UPACIP.Service/Performance/ISlaMonitorService.cs` — Interface defining EvaluateSlaComplianceAsync, GetCurrentMetricsAsync methods
- **NEW** `src/UPACIP.Service/Performance/SlaMonitorService.cs` — P95 computation with sliding window histogram, threshold evaluation, alert emission
- **NEW** `src/UPACIP.Service/Performance/Models/OperationMetric.cs` — Metric DTO: OperationType, P50Ms, P95Ms, P99Ms, SampleCount, WindowStart, WindowEnd
- **NEW** `src/UPACIP.Service/Performance/Models/SlaAlert.cs` — Alert DTO: OperationType, CurrentP95Ms, ThresholdMs, TrendDirection, Timestamp
- **NEW** `src/UPACIP.Service/Performance/Models/PerformanceOptions.cs` — Configuration: SLA thresholds, evaluation interval, window size, alert cooldown
- **NEW** `src/UPACIP.Service/Performance/PerformanceMonitoringService.cs` — BackgroundService: periodic SLA evaluation and alert emission
- **NEW** `src/UPACIP.Api/Middleware/PerformanceInstrumentationMiddleware.cs` — ASP.NET Core middleware: auto-instrument all incoming requests with Activity spans
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IPerformanceTracker, ISlaMonitorService, PerformanceMonitoringService, add middleware
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add PerformanceMonitoring configuration section

## Implementation Plan

1. **Define performance configuration model**: Create `PerformanceOptions` bound from `appsettings.json` section `PerformanceMonitoring` with: `Dictionary<string, int> SlaThresholdsMs` (keyed by operation type: `{ "Booking": 2000, "DocumentParsing": 30000, "MedicalCoding": 5000 }`), `int EvaluationIntervalSeconds` (default: 60), `int SlidingWindowMinutes` (default: 15 — P95 computed over last 15 minutes of data), `int AlertCooldownMinutes` (default: 5 — suppress duplicate alerts for the same operation within this window), `bool EnableSpanTracing` (default: true). Add configuration to `appsettings.json`:
   ```json
   "PerformanceMonitoring": {
     "SlaThresholdsMs": {
       "Booking": 2000,
       "DocumentParsing": 30000,
       "MedicalCoding": 5000
     },
     "EvaluationIntervalSeconds": 60,
     "SlidingWindowMinutes": 15,
     "AlertCooldownMinutes": 5,
     "EnableSpanTracing": true
   }
   ```

2. **Define metric and alert models**: Create `OperationMetric` with: `string OperationType`, `double P50Ms`, `double P95Ms`, `double P99Ms`, `int SampleCount`, `DateTime WindowStart`, `DateTime WindowEnd`, `bool IsWithinSla` (computed: P95Ms <= threshold). Create `SlaAlert` with: `string OperationType`, `double CurrentP95Ms`, `int ThresholdMs`, `TrendDirection Trend` (enum: Improving, Stable, Degrading — computed by comparing current P95 to previous evaluation), `DateTime Timestamp`, `string Message` (human-readable: "Booking P95 latency 2450ms exceeds 2000ms threshold (Degrading)").

3. **Define `IPerformanceTracker` interface**: Methods:
   - `Activity? StartOperation(string operationType, Dictionary<string, string>? tags = null)` — Creates and starts a new `Activity` span for the operation.
   - `Activity? StartSpan(string spanName, Activity? parentActivity = null)` — Creates a child span under an operation (e.g., "db.query", "cache.get", "ai.inference").
   - `void RecordLatency(string operationType, long latencyMs)` — Records a completed operation's latency into the histogram.
   - `void CompleteOperation(Activity? activity, bool success = true)` — Stops the activity span and records final status.

4. **Implement `PerformanceTracker` with APM spans (edge case)**: Create a static `ActivitySource` named `"UPACIP.Performance"`. In `StartOperation`, create an `Activity` with the operation type as the name, tagged with `operation.type`, `user.id` (from HttpContext if available), and `request.id` (correlation ID). In `StartSpan`, create child activities for sub-operations: `"db.query"` (EF Core queries), `"cache.get"` / `"cache.set"` (Redis operations), `"ai.inference"` (AI Gateway calls), `"http.external"` (external API calls). Each span records `span.duration_ms` as a tag. In `RecordLatency`, store the latency sample in a thread-safe in-memory circular buffer (capacity: 10,000 samples per operation type) implemented with `ConcurrentQueue<(DateTime Timestamp, long LatencyMs)>`. Expired samples (older than `SlidingWindowMinutes`) are lazily evicted during reads. Use `Meter` from `System.Diagnostics.Metrics` to expose `upacip.operation.duration` histogram for each operation type (compatible with OpenTelemetry exporters if added later).

5. **Define `ISlaMonitorService` interface**: Methods:
   - `Task<IReadOnlyList<OperationMetric>> GetCurrentMetricsAsync(CancellationToken ct)` — Computes current P50/P95/P99 for all tracked operation types.
   - `Task<IReadOnlyList<SlaAlert>> EvaluateSlaComplianceAsync(CancellationToken ct)` — Evaluates all operations against SLA thresholds and returns any breach alerts.

6. **Implement `SlaMonitorService` with P95 computation**: In `GetCurrentMetricsAsync`, for each operation type in the circular buffer: (a) filter samples within the sliding window; (b) sort by latency ascending; (c) compute P50 (index = count × 0.50), P95 (index = count × 0.95), P99 (index = count × 0.99) using nearest-rank interpolation; (d) construct `OperationMetric` with computed percentiles and sample count. Require minimum 10 samples to produce valid percentiles; if fewer, skip the operation. In `EvaluateSlaComplianceAsync`, call `GetCurrentMetricsAsync`, then for each metric where `P95Ms > SlaThresholdsMs[OperationType]`: (a) compute trend by comparing to the previous evaluation's P95 (stored in-memory); (b) check alert cooldown — skip if an alert for the same operation was emitted within `AlertCooldownMinutes`; (c) construct `SlaAlert` and emit via Serilog at `Warning` level: `_logger.LogWarning("SLA breach: {OperationType} P95={P95Ms}ms exceeds {ThresholdMs}ms ({Trend})", ...)`. Store current P95 per operation for next evaluation's trend calculation.

7. **Implement `PerformanceMonitoringService`**: Create a `BackgroundService` that runs `EvaluateSlaComplianceAsync` every `EvaluationIntervalSeconds`. On each tick: (a) call `ISlaMonitorService.EvaluateSlaComplianceAsync`; (b) for each alert, log structured alert via Serilog; (c) also write a summary metric log at `Information` level: "Performance summary: Booking P95={BookingP95}ms, Parsing P95={ParsingP95}ms, Coding P95={CodingP95}ms". Handle exceptions gracefully — if evaluation fails, log the error and retry on the next tick. Use `PeriodicTimer` for accurate interval scheduling.

8. **Implement `PerformanceInstrumentationMiddleware`**: Create ASP.NET Core middleware that auto-instruments incoming requests. Route-based operation type classification: `/api/appointments/book*` → "Booking", `/api/documents/parse*` → "DocumentParsing", `/api/coding/*` → "MedicalCoding", all others → "General" (not SLA-tracked). On each request: (a) call `IPerformanceTracker.StartOperation` with the classified operation type; (b) set the Activity as the current Activity (for child spans created by downstream services); (c) call `next(context)`; (d) on completion, compute latency from the activity's duration; (e) call `RecordLatency` with the operation type and latency; (f) call `CompleteOperation` with success = (status code < 500). Add response headers `X-Request-Duration-Ms: {duration}` and `X-Correlation-Id: {activityId}` for observability. Register the middleware early in the pipeline (after authentication, before controllers).

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
│   │   │   └── AiRateLimitingMiddleware.cs         ← from US_079
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
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
│           └── DocumentParsingWorker.cs             ← from US_039
├── app/
├── config/
└── scripts/
```

> Assumes US_001 (backend scaffold), US_004 (Redis), US_067 (AI Gateway), US_039 (document parsing pipeline), and appointment booking services are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Performance/IPerformanceTracker.cs | Interface: StartOperation, StartSpan, RecordLatency, CompleteOperation |
| CREATE | src/UPACIP.Service/Performance/PerformanceTracker.cs | ActivitySource-based APM, circular buffer histogram, Meter metrics |
| CREATE | src/UPACIP.Service/Performance/ISlaMonitorService.cs | Interface: EvaluateSlaComplianceAsync, GetCurrentMetricsAsync |
| CREATE | src/UPACIP.Service/Performance/SlaMonitorService.cs | P95 nearest-rank computation, threshold evaluation, trend analysis, alert emission |
| CREATE | src/UPACIP.Service/Performance/Models/OperationMetric.cs | DTO: OperationType, P50Ms, P95Ms, P99Ms, SampleCount, IsWithinSla |
| CREATE | src/UPACIP.Service/Performance/Models/SlaAlert.cs | DTO: OperationType, CurrentP95Ms, ThresholdMs, TrendDirection, Message |
| CREATE | src/UPACIP.Service/Performance/Models/PerformanceOptions.cs | Config: SlaThresholdsMs, EvaluationIntervalSeconds, SlidingWindowMinutes |
| CREATE | src/UPACIP.Service/Performance/PerformanceMonitoringService.cs | BackgroundService: periodic SLA evaluation with Serilog alerts |
| CREATE | src/UPACIP.Api/Middleware/PerformanceInstrumentationMiddleware.cs | Auto-instrument requests with Activity spans, route classification, latency recording |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPerformanceTracker, ISlaMonitorService, PerformanceMonitoringService, add middleware |
| MODIFY | src/UPACIP.Api/appsettings.json | Add PerformanceMonitoring configuration section with SLA thresholds |

## External References

- [System.Diagnostics.ActivitySource — Distributed Tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
- [System.Diagnostics.Metrics — .NET Metrics API](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
- [OpenTelemetry .NET Compatibility](https://opentelemetry.io/docs/languages/net/)
- [ASP.NET Core Middleware Pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [BackgroundService — Hosted Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [Serilog Structured Logging — Warning Level](https://serilog.net/)
- [Percentile Calculation — Nearest-Rank Method](https://en.wikipedia.org/wiki/Percentile#The_nearest-rank_method)

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

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] ActivitySource creates spans for booking, document parsing, and medical coding operations
- [ ] Child spans correctly capture database, cache, and external API call durations
- [ ] P95 computation produces correct values for known test datasets (e.g., 100 samples with known distribution)
- [ ] SLA alert is generated when P95 exceeds configured threshold (e.g., booking >2000ms)
- [ ] Alert cooldown prevents duplicate alerts within the configured window
- [ ] Trend direction correctly identifies improving (P95 decreased) and degrading (P95 increased) patterns
- [ ] PerformanceMonitoringService runs on configured interval and survives evaluation errors
- [ ] X-Request-Duration-Ms and X-Correlation-Id response headers are present on all responses
- [ ] Circular buffer evicts expired samples and maintains bounded memory usage

## Implementation Checklist

- [ ] Create `PerformanceOptions`, `OperationMetric`, and `SlaAlert` models in `src/UPACIP.Service/Performance/Models/`
- [ ] Define `IPerformanceTracker` interface with operation and span lifecycle methods
- [ ] Implement `PerformanceTracker` with `ActivitySource`, `Meter`, and thread-safe circular buffer histogram
- [ ] Define `ISlaMonitorService` interface with evaluation and query methods
- [ ] Implement `SlaMonitorService` with P95 nearest-rank computation, threshold evaluation, and trend tracking
- [ ] Implement `PerformanceMonitoringService` BackgroundService with periodic SLA evaluation
- [ ] Implement `PerformanceInstrumentationMiddleware` with route-based operation classification
- [ ] Add `PerformanceMonitoring` configuration section to `appsettings.json` and register all services in DI
