# Task - task_001_be_uptime_monitoring_alerting

## Requirement Reference

- User Story: us_083
- Story Location: .propel/context/tasks/EP-015/us_083/us_083.md
- Acceptance Criteria:
  - AC-1: Given the system is running, When uptime is measured over a rolling 30-day period, Then the target is 99.9% (max 43 minutes downtime per month).
  - AC-3: Given uptime monitoring is active, When the system experiences an outage, Then an alert is generated within 1 minute with affected services, start time, and estimated impact.
  - AC-4: Given the error rate is monitored, When it exceeds 0.1% of total requests, Then an error rate alert is generated for investigation.
- Edge Case:
  - What happens during planned maintenance? Maintenance windows are pre-configured; health check returns "degraded" status and maintenance page is served.

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
| Backend | Serilog | 8.x |
| Backend | Seq (Community Edition) | 2024.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | AspNetCore.HealthChecks.NpgSql | 8.x |
| Library | AspNetCore.HealthChecks.Redis | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic uptime tracking, error rate monitoring, and alert generation. No LLM inference involved.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement an uptime monitoring and alerting framework that tracks system availability against the 99.9% SLA target (max 43 minutes downtime per rolling 30-day period) per NFR-019. The framework provides three capabilities: (1) **Uptime tracking** — a `BackgroundService` that polls the existing health check endpoints (`/health`, `/ready` from US_007) every 30 seconds, records availability snapshots to a rolling 30-day time-series in PostgreSQL, and computes current uptime percentage on demand; (2) **Outage detection and alerting** — detects when any dependency transitions from healthy to unhealthy, generates a structured alert within 1 minute containing affected services, outage start time, and estimated impact classification (Critical/Major/Minor), and emits alerts via Serilog structured logging to Seq for operator visibility; (3) **Error rate monitoring** — tracks HTTP error responses (4xx/5xx) per rolling 5-minute window, alerts when the error rate exceeds the 0.1% threshold (NFR-031), and classifies errors by workflow category (booking, intake, coding). Additionally, the framework supports pre-configured maintenance windows (edge case) during which the health check returns a "Degraded" status and outage alerts are suppressed.

## Dependent Tasks

- US_007 task_001_be_health_check_endpoints — Requires `/health` and `/ready` endpoints for availability probing.
- US_001 — Requires backend API scaffold with middleware pipeline.
- US_003 — Requires PostgreSQL for uptime snapshot persistence.
- US_081 task_001_be_performance_instrumentation_alerting — Requires `IPerformanceTracker` for metric emission.

## Impacted Components

- **NEW** `src/UPACIP.Service/Monitoring/IUptimeTracker.cs` — Interface: RecordSnapshotAsync, GetUptimePercentageAsync, GetCurrentStatusAsync
- **NEW** `src/UPACIP.Service/Monitoring/UptimeTracker.cs` — Rolling 30-day uptime computation from time-series snapshots
- **NEW** `src/UPACIP.Service/Monitoring/IOutageAlertService.cs` — Interface: EvaluateHealthTransitionAsync, GetActiveOutagesAsync, AcknowledgeOutageAsync
- **NEW** `src/UPACIP.Service/Monitoring/OutageAlertService.cs` — State-transition outage detection, alert generation with affected services and impact
- **NEW** `src/UPACIP.Service/Monitoring/IErrorRateMonitor.cs` — Interface: RecordRequestOutcome, GetCurrentErrorRate, IsThresholdExceeded
- **NEW** `src/UPACIP.Service/Monitoring/ErrorRateMonitor.cs` — Sliding window error rate computation with 0.1% threshold alerting
- **NEW** `src/UPACIP.Service/Monitoring/UptimeMonitoringService.cs` — BackgroundService: 30-second health probe, outage detection, error rate evaluation
- **NEW** `src/UPACIP.Service/Monitoring/Models/UptimeSnapshot.cs` — Entity: Id, Timestamp, IsHealthy, DependencyStatuses (JSON), MaintenanceWindow
- **NEW** `src/UPACIP.Service/Monitoring/Models/OutageRecord.cs` — Entity: Id, StartedAt, ResolvedAt, AffectedServices, ImpactLevel, AlertSentAt
- **NEW** `src/UPACIP.Service/Monitoring/Models/MonitoringOptions.cs` — Configuration: ProbeIntervalSeconds, UptimeWindowDays, ErrorRateThreshold, MaintenanceWindows
- **NEW** `src/UPACIP.Api/Middleware/ErrorRateTrackingMiddleware.cs` — Middleware: records request outcomes (success/error) per workflow category
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<UptimeSnapshot> and DbSet<OutageRecord>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register monitoring services, add ErrorRateTrackingMiddleware, bind MonitoringOptions
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Monitoring configuration section with maintenance windows

## Implementation Plan

1. **Create monitoring entities and configuration (AC-1)**: Create `UptimeSnapshot` entity with: `Guid Id` (PK), `DateTime Timestamp` (UTC, indexed), `bool IsHealthy`, `string DependencyStatusesJson` (serialized per-dependency status from `/ready` response), `bool IsMaintenanceWindow`. Create `OutageRecord` entity with: `Guid Id` (PK), `DateTime StartedAt` (indexed), `DateTime? ResolvedAt`, `string AffectedServices` (comma-separated), `string ImpactLevel` (Critical/Major/Minor), `DateTime? AlertSentAt`. Create `MonitoringOptions` with: `int ProbeIntervalSeconds` (default: 30), `int UptimeWindowDays` (default: 30), `double ErrorRateThresholdPercent` (default: 0.1), `int ErrorRateWindowSeconds` (default: 300), `List<MaintenanceWindow> MaintenanceWindows` where `MaintenanceWindow` has `DayOfWeek Day`, `TimeSpan StartUtc`, `TimeSpan EndUtc`. Add both `DbSet<UptimeSnapshot>` and `DbSet<OutageRecord>` to `ApplicationDbContext` and create a migration.

2. **Implement `UptimeTracker` (AC-1)**: Create `IUptimeTracker` / `UptimeTracker`. Method `RecordSnapshotAsync(bool isHealthy, Dictionary<string, string> dependencyStatuses)` persists a new `UptimeSnapshot` to PostgreSQL. Method `GetUptimePercentageAsync(int windowDays = 30)` queries all snapshots within the rolling window and computes `uptimePercent = (healthyCount / totalCount) * 100`. Method `GetCurrentStatusAsync()` returns the latest snapshot. Implement a data retention cleanup that deletes snapshots older than 90 days (3× the monitoring window) — run as part of each probe cycle after recording. The 30-day rolling window at 30-second intervals produces approximately 86,400 snapshots — index `Timestamp` for efficient range queries.

3. **Implement `OutageAlertService` (AC-3)**: Create `IOutageAlertService` / `OutageAlertService`. Maintain an in-memory `Dictionary<string, DateTime>` of currently-unhealthy dependencies (key = dependency name, value = first-detected-unhealthy timestamp). Method `EvaluateHealthTransitionAsync(Dictionary<string, string> currentStatuses)` compares current dependency statuses against the previous known state:
   - **Healthy → Unhealthy transition**: Create a new `OutageRecord` with `StartedAt = DateTime.UtcNow`, classify impact level based on the affected dependency (Database = Critical, Redis = Major, AI providers = Minor), and emit a structured Serilog alert: `Log.Warning("OUTAGE_DETECTED: Services={AffectedServices}, StartedAt={StartedAt}, Impact={ImpactLevel}")`. The alert includes affected services, start time, and estimated impact per AC-3 requirements. Persist the `OutageRecord` to PostgreSQL.
   - **Unhealthy → Healthy transition**: Update the existing `OutageRecord` with `ResolvedAt = DateTime.UtcNow` and emit a recovery log: `Log.Information("OUTAGE_RESOLVED: Services={AffectedServices}, Duration={Duration}")`.
   Since the probe runs every 30 seconds, the worst-case alert latency is 30 seconds (well within the 1-minute requirement of AC-3).

4. **Implement `ErrorRateMonitor` (AC-4)**: Create `IErrorRateMonitor` / `ErrorRateMonitor`. Use a thread-safe sliding window backed by `ConcurrentQueue<(DateTime Timestamp, bool IsError, string Category)>`. Method `RecordRequestOutcome(bool isError, string category)` enqueues a new entry and evicts entries older than `ErrorRateWindowSeconds` (default: 300 seconds / 5 minutes). Method `GetCurrentErrorRate()` computes `errorRate = errorCount / totalCount` over the current window. Method `IsThresholdExceeded()` returns `errorRate > ErrorRateThresholdPercent / 100`. Method `GetErrorRateByCategory()` groups errors by workflow category (booking, intake, coding, other) for diagnostic triage. When the threshold is exceeded, the monitoring service emits: `Log.Warning("ERROR_RATE_ALERT: Rate={ErrorRate}%, Threshold={Threshold}%, TopCategory={TopCategory}, Window={WindowSeconds}s")`. Register as singleton for cross-request state accumulation.

5. **Implement `ErrorRateTrackingMiddleware` (AC-4)**: Create middleware that runs after the response is generated. On each request completion: (a) determine workflow category from route — `/api/appointments/*` → "booking", `/api/intake/*` → "intake", `/api/coding/*` → "coding", everything else → "other"; (b) classify outcome — status codes 400-599 are errors, 200-399 are successes; (c) call `IErrorRateMonitor.RecordRequestOutcome(isError, category)`. Exclude health check endpoints (`/health`, `/ready`) from error rate tracking since they are infrastructure probes, not user workflows. Place this middleware early in the pipeline (after exception handling) to capture all response codes.

6. **Implement `UptimeMonitoringService` (AC-1, AC-3, AC-4, edge case)**: Create a `BackgroundService` that runs the monitoring loop on a configurable interval (default: 30 seconds via `MonitoringOptions.ProbeIntervalSeconds`). Each cycle:
   - (a) Check if the current time falls within a configured maintenance window — if yes, record snapshot with `IsMaintenanceWindow = true` and skip outage alerting (edge case: planned maintenance).
   - (b) Call the internal health check evaluation using `HealthCheckService.CheckHealthAsync()` (the ASP.NET Core built-in service, not an HTTP call to `/ready`) to get per-dependency health statuses.
   - (c) Call `IUptimeTracker.RecordSnapshotAsync()` with the aggregated health status.
   - (d) Call `IOutageAlertService.EvaluateHealthTransitionAsync()` to detect state transitions and generate outage alerts.
   - (e) Call `IErrorRateMonitor.IsThresholdExceeded()` — if exceeded, emit error rate alert via Serilog.
   - (f) Emit uptime metric: `IPerformanceTracker.RecordLatency("system.uptime_percent", uptimePercent)`.
   - (g) Prune old uptime snapshots (>90 days) every 100th cycle to avoid per-cycle query overhead.

7. **Implement maintenance window support (edge case)**: Add maintenance window configuration to `appsettings.json`:
   ```json
   "Monitoring": {
     "ProbeIntervalSeconds": 30,
     "UptimeWindowDays": 30,
     "ErrorRateThresholdPercent": 0.1,
     "ErrorRateWindowSeconds": 300,
     "MaintenanceWindows": [
       { "Day": "Sunday", "StartUtc": "02:00:00", "EndUtc": "04:00:00" }
     ]
   }
   ```
   During maintenance windows: (a) `UptimeSnapshot.IsMaintenanceWindow = true`; (b) maintenance snapshots are excluded from the uptime percentage computation (neither counted as healthy nor unhealthy); (c) outage alerts are suppressed (no `OUTAGE_DETECTED` log emitted); (d) the `/ready` endpoint from US_007 continues to report per-dependency status but the `UptimeMonitoringService` annotates the probe result as maintenance-excluded. This ensures planned maintenance does not count against the 99.9% SLA target.

8. **Register services and configure middleware ordering**: In `Program.cs`: register `services.AddSingleton<IErrorRateMonitor, ErrorRateMonitor>()`, `services.AddScoped<IUptimeTracker, UptimeTracker>()`, `services.AddScoped<IOutageAlertService, OutageAlertService>()`, `services.AddHostedService<UptimeMonitoringService>()`, and bind `MonitoringOptions` from configuration. Add `ErrorRateTrackingMiddleware` after `GlobalExceptionHandlerMiddleware` but before routing to capture all response codes. Inject `HealthCheckService` (built-in ASP.NET Core service registered by `AddHealthChecks()` in US_007) into `UptimeMonitoringService` for direct health evaluation without HTTP overhead.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs  ← from US_001
│   │   │   ├── CorrelationIdMiddleware.cs           ← from US_001
│   │   │   ├── AiRateLimitingMiddleware.cs          ← from US_079
│   │   │   ├── PerformanceInstrumentationMiddleware.cs ← from US_081
│   │   │   ├── ConnectionPoolGuardMiddleware.cs     ← from US_082
│   │   │   └── EndpointCircuitBreakerMiddleware.cs  ← from US_082
│   │   ├── HealthChecks/
│   │   │   ├── HealthCheckResponseWriter.cs         ← from US_007
│   │   │   └── StartupHealthCheck.cs                ← from US_007
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Performance/
│   │   │   ├── IPerformanceTracker.cs               ← from US_081
│   │   │   ├── PerformanceTracker.cs                ← from US_081
│   │   │   ├── ISlaMonitorService.cs                ← from US_081
│   │   │   ├── SlaMonitorService.cs                 ← from US_081
│   │   │   └── PerformanceMonitoringService.cs      ← from US_081
│   │   ├── Infrastructure/
│   │   │   ├── IConnectionPoolMonitor.cs            ← from US_082
│   │   │   ├── ConnectionPoolMonitor.cs             ← from US_082
│   │   │   └── BackgroundAiQueueProcessor.cs        ← from US_082
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs                     ← from US_004
│   │   │   └── RedisCacheService.cs                 ← from US_004
│   │   └── AiSafety/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   ├── Services/
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_001, US_003, US_004, US_007, US_081, and US_082 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Monitoring/IUptimeTracker.cs | Interface: RecordSnapshotAsync, GetUptimePercentageAsync, GetCurrentStatusAsync |
| CREATE | src/UPACIP.Service/Monitoring/UptimeTracker.cs | Rolling 30-day uptime computation from time-series snapshots |
| CREATE | src/UPACIP.Service/Monitoring/IOutageAlertService.cs | Interface: EvaluateHealthTransitionAsync, GetActiveOutagesAsync |
| CREATE | src/UPACIP.Service/Monitoring/OutageAlertService.cs | State-transition outage detection, impact classification, Serilog alert emission |
| CREATE | src/UPACIP.Service/Monitoring/IErrorRateMonitor.cs | Interface: RecordRequestOutcome, GetCurrentErrorRate, IsThresholdExceeded |
| CREATE | src/UPACIP.Service/Monitoring/ErrorRateMonitor.cs | Sliding window error rate with 0.1% threshold and per-category breakdown |
| CREATE | src/UPACIP.Service/Monitoring/UptimeMonitoringService.cs | BackgroundService: 30-second probe, outage detection, error rate evaluation |
| CREATE | src/UPACIP.Service/Monitoring/Models/UptimeSnapshot.cs | Entity: Id, Timestamp, IsHealthy, DependencyStatusesJson, IsMaintenanceWindow |
| CREATE | src/UPACIP.Service/Monitoring/Models/OutageRecord.cs | Entity: Id, StartedAt, ResolvedAt, AffectedServices, ImpactLevel |
| CREATE | src/UPACIP.Service/Monitoring/Models/MonitoringOptions.cs | Config: ProbeIntervalSeconds, UptimeWindowDays, ErrorRateThreshold, MaintenanceWindows |
| CREATE | src/UPACIP.Api/Middleware/ErrorRateTrackingMiddleware.cs | Middleware: records request outcomes per workflow category |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<UptimeSnapshot> and DbSet<OutageRecord> |
| MODIFY | src/UPACIP.Api/Program.cs | Register monitoring services, add middleware, bind MonitoringOptions |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Monitoring section with probe interval and maintenance windows |

## External References

- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0)
- [HealthCheckService — Programmatic Health Evaluation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice)
- [BackgroundService — IHostedService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [Serilog Structured Logging](https://serilog.net/)
- [Seq — Structured Log Server](https://datalust.co/seq)
- [ConcurrentQueue — Thread-Safe Collection](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentqueue-1)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Add EF Core migration for monitoring tables
dotnet ef migrations add AddMonitoringTables --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migration
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] EF Core migration creates `uptime_snapshots` and `outage_records` tables
- [ ] `UptimeMonitoringService` probes health every 30 seconds and persists snapshots
- [ ] `GetUptimePercentageAsync(30)` returns correct percentage over rolling 30-day window
- [ ] When a dependency transitions Healthy → Unhealthy, an `OUTAGE_DETECTED` log is emitted within 30 seconds
- [ ] Outage alert includes affected services, start time, and impact level (Critical/Major/Minor)
- [ ] When dependency recovers, `OUTAGE_RESOLVED` log is emitted with duration
- [ ] Error rate monitor tracks 5xx responses in a sliding 5-minute window
- [ ] When error rate exceeds 0.1%, `ERROR_RATE_ALERT` log is emitted with rate and top category
- [ ] During configured maintenance windows, snapshots are recorded but excluded from uptime calculation
- [ ] During maintenance windows, outage alerts are suppressed
- [ ] Snapshots older than 90 days are pruned automatically

## Implementation Checklist

- [ ] Create `UptimeSnapshot`, `OutageRecord`, and `MonitoringOptions` models in `src/UPACIP.Service/Monitoring/Models/`
- [ ] Add `DbSet<UptimeSnapshot>` and `DbSet<OutageRecord>` to `ApplicationDbContext` and create migration
- [ ] Implement `IUptimeTracker` / `UptimeTracker` with rolling 30-day uptime computation and 90-day retention pruning
- [ ] Implement `IOutageAlertService` / `OutageAlertService` with health transition detection and Serilog alert emission
- [ ] Implement `IErrorRateMonitor` / `ErrorRateMonitor` with sliding window and 0.1% threshold alerting
- [ ] Implement `ErrorRateTrackingMiddleware` with route-based workflow categorization
- [ ] Implement `UptimeMonitoringService` BackgroundService with 30-second probe cycle and maintenance window support
- [ ] Register all services in DI, configure middleware ordering, and add Monitoring configuration to appsettings.json
