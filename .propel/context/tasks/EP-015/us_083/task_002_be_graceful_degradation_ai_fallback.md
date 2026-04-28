# Task - task_002_be_graceful_degradation_ai_fallback

## Requirement Reference

- User Story: us_083
- Story Location: .propel/context/tasks/EP-015/us_083/us_083.md
- Acceptance Criteria:
  - AC-2: Given an AI service becomes unavailable, When the failure is detected, Then the system activates graceful degradation mode with manual workflow fallbacks and displays a notification to staff.
- Edge Case:
  - How does the system handle partial outages (e.g., database up, Redis down)? Health check reports per-dependency status; Redis-dependent features degrade while core CRUD operations continue.

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
| Backend | Polly | 8.x |
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

> This task implements deterministic degradation mode switching and fallback routing. It does not invoke LLMs â€” it detects AI service unavailability and routes to manual workflows.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a graceful degradation framework that maintains system operability when non-critical dependencies (AI providers, Redis) become unavailable, per NFR-022. The framework provides three capabilities: (1) **Degradation mode manager** â€” a centralized `IDegradationModeManager` that tracks per-dependency health status and activates/deactivates degradation mode for specific feature areas (AI services, caching, notifications), exposing the current system operating mode (Normal, Degraded, MaintenanceMode) via a queryable API; (2) **Manual workflow fallback routing** â€” when AI services are unavailable, AI-powered endpoints (conversational intake, document parsing, medical coding) return structured fallback responses redirecting users to manual workflows instead of failing with 500 errors, and the existing circuit breaker (AIR-O04) integrates with the degradation manager to propagate state; (3) **Staff notification pipeline** â€” when degradation mode activates, a staff notification is dispatched via the existing notification infrastructure and a persistent banner flag is set that frontend clients can poll to display degradation status. Additionally, the framework handles partial outages (edge case) by tracking per-dependency status independently â€” Redis unavailability degrades cache-dependent features (slot caching, session) while core CRUD operations continue via database fallback.

## Dependent Tasks

- US_083 task_001_be_uptime_monitoring_alerting â€” Requires `IOutageAlertService` for dependency health transition detection.
- US_007 task_001_be_health_check_endpoints â€” Requires health check infrastructure for per-dependency status.
- US_082 task_001_be_concurrency_resilience_infrastructure â€” Requires endpoint circuit breaker patterns (Polly) for integration.
- US_004 â€” Requires Redis for cache-dependent feature degradation handling.

## Impacted Components

- **NEW** `src/UPACIP.Service/Monitoring/IDegradationModeManager.cs` â€” Interface: GetCurrentMode, GetDependencyStatus, ActivateDegradation, DeactivateDegradation, IsFeatureAvailable
- **NEW** `src/UPACIP.Service/Monitoring/DegradationModeManager.cs` â€” Per-dependency degradation state tracking, feature availability computation
- **NEW** `src/UPACIP.Service/Monitoring/Models/DegradationState.cs` â€” Model: SystemMode (Normal/Degraded/MaintenanceMode), per-dependency FeatureAvailability map
- **NEW** `src/UPACIP.Service/Monitoring/Models/DependencyCategory.cs` â€” Enum: AiProviders, Redis, Database, ExternalServices
- **NEW** `src/UPACIP.Api/Middleware/GracefulDegradationMiddleware.cs` â€” Middleware: intercepts AI-dependent requests when degraded and returns fallback responses
- **NEW** `src/UPACIP.Api/Controllers/Admin/SystemStatusController.cs` â€” Admin API: GET system mode, dependency statuses, active degradations, manual override
- **MODIFY** `src/UPACIP.Service/Monitoring/UptimeMonitoringService.cs` â€” Integrate degradation mode activation on health transition events
- **MODIFY** `src/UPACIP.Api/Program.cs` â€” Register degradation services, add GracefulDegradationMiddleware
- **MODIFY** `src/UPACIP.Api/appsettings.json` â€” Add Degradation configuration section with fallback messages and feature-dependency mapping

## Implementation Plan

1. **Create degradation models and configuration**: Create `DependencyCategory` enum with: `AiProviders`, `Redis`, `Database`, `ExternalServices` (SMS/email gateways). Create `DegradationState` with: `SystemMode Mode` (enum: Normal, Degraded, MaintenanceMode), `Dictionary<DependencyCategory, bool> DependencyHealth` (true = healthy), `Dictionary<string, bool> FeatureAvailability` (feature name â†’ available), `DateTime? DegradedSince`. Create `DegradationOptions` with: `Dictionary<string, DependencyCategory[]> FeatureDependencyMap` mapping feature areas to their required dependencies â€” e.g., `"ai_intake": [AiProviders]`, `"ai_coding": [AiProviders]`, `"ai_parsing": [AiProviders]`, `"slot_caching": [Redis]`, `"session_management": [Redis]`, `"core_crud": [Database]`. Add to `appsettings.json`:
   ```json
   "Degradation": {
     "FeatureDependencyMap": {
       "ai_intake": ["AiProviders"],
       "ai_coding": ["AiProviders"],
       "ai_parsing": ["AiProviders"],
       "slot_caching": ["Redis"],
       "session_management": ["Redis"],
       "core_crud": ["Database"]
     },
     "StaffNotificationEnabled": true,
     "FallbackMessage": "AI services are temporarily unavailable. Please use manual workflows."
   }
   ```

2. **Implement `DegradationModeManager` (AC-2, edge case)**: Create `IDegradationModeManager` / `DegradationModeManager` registered as a singleton. Maintain a thread-safe `ConcurrentDictionary<DependencyCategory, bool>` for per-dependency health. Method `ActivateDegradation(DependencyCategory category)` marks the dependency as unhealthy, recomputes feature availability from the `FeatureDependencyMap`, sets `Mode = Degraded` if any dependency is unhealthy, and emits a Serilog log: `Log.Warning("DEGRADATION_ACTIVATED: Dependency={Category}, AffectedFeatures={Features}")`. Method `DeactivateDegradation(DependencyCategory category)` marks the dependency as healthy, recomputes mode (back to `Normal` if all healthy), and logs recovery. Method `IsFeatureAvailable(string featureName)` checks whether all dependencies required by the feature are currently healthy. Method `GetCurrentMode()` returns the current `DegradationState`. This design handles the partial outage edge case â€” when Redis is down but Database is up, `IsFeatureAvailable("slot_caching")` returns false while `IsFeatureAvailable("core_crud")` returns true, allowing core CRUD to continue uninterrupted.

3. **Implement `GracefulDegradationMiddleware` (AC-2)**: Create ASP.NET Core middleware that intercepts requests to AI-dependent endpoints when degradation is active. Route-to-feature mapping:
   - `/api/intake/conversational*` â†’ feature `ai_intake`
   - `/api/documents/parse*`, `/api/documents/upload*` â†’ feature `ai_parsing`
   - `/api/coding/suggest*`, `/api/coding/auto*` â†’ feature `ai_coding`
   On each matching request: (a) call `IDegradationModeManager.IsFeatureAvailable(feature)`; (b) if available, pass through to `next(context)`; (c) if unavailable, return HTTP 503 with a structured fallback response:
   ```json
   {
     "error": "service_degraded",
     "message": "AI services are temporarily unavailable. Please use manual workflows.",
     "fallbackAction": "manual_intake",
     "degradedSince": "2026-04-17T10:00:00Z",
     "affectedFeature": "ai_intake"
   }
   ```
   The `fallbackAction` field maps to: `ai_intake` â†’ `"manual_intake"` (redirect to manual intake form), `ai_parsing` â†’ `"manual_upload"` (accept upload but skip AI parsing), `ai_coding` â†’ `"manual_coding"` (redirect to manual code entry). This gives frontend clients actionable fallback routing information. Non-AI routes pass through without evaluation.

4. **Implement staff notification on degradation (AC-2)**: When `DegradationModeManager.ActivateDegradation()` is called, trigger a staff notification via two channels:
   - **Serilog alert** (immediate): `Log.Warning("STAFF_NOTIFICATION: AI services unavailable. Manual workflows required. Degraded features: {Features}")` â€” this appears in Seq for operators monitoring the dashboard.
   - **Persistent degradation flag** (for frontend polling): Set a Redis key `system:degradation:status` with the current `DegradationState` serialized as JSON, with a 5-minute TTL (auto-clears if the monitoring service stops updating). If Redis itself is unavailable, fall back to an in-memory volatile flag queryable via the `SystemStatusController`. Frontend clients poll `GET /api/admin/system-status` to check degradation state and display a banner notification to staff (the banner UI itself is outside this task's scope â€” this task provides the API endpoint).
   On recovery (`DeactivateDegradation`): clear the Redis key and log: `Log.Information("STAFF_NOTIFICATION: AI services restored. Normal operations resumed.")`.

5. **Integrate with `UptimeMonitoringService` (AC-2)**: Modify the existing `UptimeMonitoringService` (from task_001) to call `IDegradationModeManager` during each health probe cycle. After `IOutageAlertService.EvaluateHealthTransitionAsync()` detects a state transition:
   - Map health check entry names to `DependencyCategory`: `"database"` â†’ `Database`, `"redis"` â†’ `Redis`, `"ai_openai"` or `"ai_anthropic"` â†’ `AiProviders`.
   - On Unhealthy transition: call `degradationManager.ActivateDegradation(category)`.
   - On Healthy transition: call `degradationManager.DeactivateDegradation(category)`.
   This ensures degradation mode activates automatically within 30 seconds of detecting a dependency failure (one probe cycle) and deactivates when the dependency recovers.

6. **Handle Redis-specific partial outage (edge case)**: When Redis becomes unavailable, the following features degrade gracefully:
   - **Slot caching**: The `AppointmentSlotCacheService` (from US_081 task_002) should already handle `RedisConnectionException` by falling through to the database query. The degradation manager confirms this by setting `IsFeatureAvailable("slot_caching") = false`, which the frontend can use to warn staff about slower slot lookups.
   - **Session management**: The JWT-based stateless auth (Decision #8) continues to work â€” only the Redis-backed token blacklist (for immediate logout) is affected. Sessions continue to validate via JWT signature until expiry.
   - **AI rate limiting**: The Redis-backed rate limiter (from US_079 task_003) should fail open (allow requests) when Redis is down, since blocking all AI requests during a Redis outage would be more disruptive than temporarily relaxing rate limits.
   - **Core CRUD**: All database-backed operations (appointment CRUD, patient management, queue management) continue normally since they depend only on PostgreSQL. The middleware passes these requests through without degradation checks.

7. **Implement `SystemStatusController` (AC-2)**: Create an admin API controller with endpoints:
   - `GET /api/admin/system-status` â€” Returns current `DegradationState` including mode, per-dependency health, and feature availability map. Requires `Admin` or `Staff` role authorization.
   - `POST /api/admin/system-status/override` â€” Allows an admin to manually activate/deactivate degradation for a specific `DependencyCategory` (useful for pre-maintenance mode activation). Requires `Admin` role only. Body: `{ "category": "AiProviders", "isHealthy": false }`.
   - `GET /api/admin/system-status/history` â€” Returns last 24 hours of degradation events from `OutageRecord` (from task_001). Requires `Admin` role.
   These endpoints enable both automated degradation (via monitoring) and manual override (for planned maintenance).

8. **Register services and configure middleware ordering**: In `Program.cs`: register `services.AddSingleton<IDegradationModeManager, DegradationModeManager>()` and bind `DegradationOptions`. Add `GracefulDegradationMiddleware` after authentication but before routing â€” this ensures the degradation check happens after the user is authenticated (so the fallback response respects authorization) but before the request reaches the controller (avoiding unnecessary processing). Add `SystemStatusController` to the admin API route group. Middleware pipeline order: ExceptionHandler â†’ CorrelationId â†’ ErrorRateTracking â†’ ConnectionPoolGuard â†’ CircuitBreaker â†’ Authentication â†’ **GracefulDegradation** â†’ RateLimiting â†’ PerformanceInstrumentation â†’ Routing.

## Current Project State

```text
UPACIP/
â”œâ”€â”€ UPACIP.sln
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ UPACIP.Api/
â”‚   â”‚   â”œâ”€â”€ Program.cs
â”‚   â”‚   â”œâ”€â”€ Controllers/
â”‚   â”‚   â”‚   â”œâ”€â”€ Admin/
â”‚   â”‚   â”‚   â”‚   â”œâ”€â”€ AbTestingController.cs           â† from US_080
â”‚   â”‚   â”‚   â”‚   â”œâ”€â”€ AiAuditController.cs             â† from US_080
â”‚   â”‚   â”‚   â”‚   â””â”€â”€ RateLimitAdminController.cs      â† from US_079
â”‚   â”‚   â”‚   â”œâ”€â”€ AppointmentController.cs
â”‚   â”‚   â”‚   â””â”€â”€ DocumentController.cs
â”‚   â”‚   â”œâ”€â”€ Middleware/
â”‚   â”‚   â”‚   â”œâ”€â”€ GlobalExceptionHandlerMiddleware.cs   â† from US_001
â”‚   â”‚   â”‚   â”œâ”€â”€ CorrelationIdMiddleware.cs            â† from US_001
â”‚   â”‚   â”‚   â”œâ”€â”€ AiRateLimitingMiddleware.cs           â† from US_079
â”‚   â”‚   â”‚   â”œâ”€â”€ PerformanceInstrumentationMiddleware.cs â† from US_081
â”‚   â”‚   â”‚   â”œâ”€â”€ ConnectionPoolGuardMiddleware.cs      â† from US_082
â”‚   â”‚   â”‚   â”œâ”€â”€ EndpointCircuitBreakerMiddleware.cs   â† from US_082
â”‚   â”‚   â”‚   â””â”€â”€ ErrorRateTrackingMiddleware.cs        â† from task_001
â”‚   â”‚   â”œâ”€â”€ HealthChecks/
â”‚   â”‚   â”‚   â”œâ”€â”€ HealthCheckResponseWriter.cs          â† from US_007
â”‚   â”‚   â”‚   â””â”€â”€ StartupHealthCheck.cs                 â† from US_007
â”‚   â”‚   â””â”€â”€ appsettings.json
â”‚   â”œâ”€â”€ UPACIP.Service/
â”‚   â”‚   â”œâ”€â”€ UPACIP.Service.csproj
â”‚   â”‚   â”œâ”€â”€ Performance/
â”‚   â”‚   â”‚   â”œâ”€â”€ IPerformanceTracker.cs                â† from US_081
â”‚   â”‚   â”‚   â””â”€â”€ PerformanceMonitoringService.cs       â† from US_081
â”‚   â”‚   â”œâ”€â”€ Monitoring/
â”‚   â”‚   â”‚   â”œâ”€â”€ IUptimeTracker.cs                     â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ UptimeTracker.cs                      â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ IOutageAlertService.cs                â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ OutageAlertService.cs                 â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ IErrorRateMonitor.cs                  â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ ErrorRateMonitor.cs                   â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ UptimeMonitoringService.cs            â† from task_001
â”‚   â”‚   â”‚   â””â”€â”€ Models/
â”‚   â”‚   â”‚       â”œâ”€â”€ UptimeSnapshot.cs                 â† from task_001
â”‚   â”‚   â”‚       â”œâ”€â”€ OutageRecord.cs                   â† from task_001
â”‚   â”‚   â”‚       â””â”€â”€ MonitoringOptions.cs              â† from task_001
â”‚   â”‚   â”œâ”€â”€ Infrastructure/
â”‚   â”‚   â”œâ”€â”€ Caching/
â”‚   â”‚   â””â”€â”€ AiSafety/
â”‚   â””â”€â”€ UPACIP.DataAccess/
â”‚       â”œâ”€â”€ ApplicationDbContext.cs
â”‚       â””â”€â”€ Entities/
â”œâ”€â”€ Server/
â”‚   â”œâ”€â”€ Services/
â”‚   â”‚   â””â”€â”€ AppointmentSlotCacheService.cs
â”‚   â””â”€â”€ AI/
â”‚       â”œâ”€â”€ AiGatewayService.cs                       â† from US_067
â”‚       â””â”€â”€ DocumentParsing/
â”œâ”€â”€ app/
â”œâ”€â”€ config/
â””â”€â”€ scripts/
```

> Assumes task_001 (uptime monitoring), US_007 (health checks), US_082 (circuit breaker), US_079 (rate limiting), and all AI service infrastructure are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Monitoring/IDegradationModeManager.cs | Interface: GetCurrentMode, ActivateDegradation, DeactivateDegradation, IsFeatureAvailable |
| CREATE | src/UPACIP.Service/Monitoring/DegradationModeManager.cs | Per-dependency degradation tracking, feature availability computation |
| CREATE | src/UPACIP.Service/Monitoring/Models/DegradationState.cs | Model: SystemMode enum, dependency health map, feature availability map |
| CREATE | src/UPACIP.Service/Monitoring/Models/DependencyCategory.cs | Enum: AiProviders, Redis, Database, ExternalServices |
| CREATE | src/UPACIP.Api/Middleware/GracefulDegradationMiddleware.cs | Intercepts AI-dependent requests, returns structured fallback with manual workflow action |
| CREATE | src/UPACIP.Api/Controllers/Admin/SystemStatusController.cs | Admin API: system mode, dependency statuses, manual degradation override |
| MODIFY | src/UPACIP.Service/Monitoring/UptimeMonitoringService.cs | Integrate degradation mode activation on health transitions |
| MODIFY | src/UPACIP.Api/Program.cs | Register degradation services, add GracefulDegradationMiddleware |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Degradation section with feature-dependency map and fallback messages |

## External References

- [Polly Circuit Breaker â€” State Management](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [ASP.NET Core Health Checks â€” HealthCheckService](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice)
- [Graceful Degradation Patterns â€” Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/graceful-degradation)
- [ConcurrentDictionary â€” Thread-Safe Operations](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2)
- [HTTP 503 Service Unavailable â€” RFC 9110](https://www.rfc-editor.org/rfc/rfc9110#status.503)

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

- [x] `dotnet build` completes with zero errors for all projects
- [x] When AI provider health check transitions to Unhealthy, `DegradationModeManager` mode changes to Degraded
- [x] `IsFeatureAvailable("ai_intake")` returns false when AI providers are unhealthy
- [x] `IsFeatureAvailable("core_crud")` returns true when only Redis is down (partial outage)
- [x] Requests to `/api/intake/conversational` return HTTP 503 with fallback response when AI is degraded
- [x] Fallback response includes `fallbackAction` field mapping to manual workflow
- [x] Staff notification log `STAFF_NOTIFICATION` is emitted on degradation activation
- [x] Redis key `system:degradation:status` is set with current state on degradation
- [x] `GET /api/admin/system-status` returns current mode, dependency health, and feature availability
- [x] `POST /api/admin/system-status/override` allows manual degradation activation (Admin role)
- [x] When dependency recovers, degradation mode deactivates and recovery log is emitted
- [x] Non-AI endpoints (appointments, patients) continue to work during AI degradation

## Implementation Checklist

- [x] Create `DependencyCategory` enum and `DegradationState` model in `src/UPACIP.Service/Monitoring/Models/`
- [x] Implement `IDegradationModeManager` / `DegradationModeManager` with per-dependency tracking and feature availability
- [x] Implement `GracefulDegradationMiddleware` with route-to-feature mapping and structured fallback responses
- [x] Implement staff notification via Serilog alert and Redis degradation flag
- [x] Integrate `IDegradationModeManager` with `UptimeMonitoringService` health transition events
- [x] Implement Redis-specific partial outage handling (cache fallthrough, rate limiter fail-open)
- [x] Create `SystemStatusController` with status query, manual override, and history endpoints
- [x] Register services in DI, configure middleware ordering, and add Degradation configuration to appsettings.json

