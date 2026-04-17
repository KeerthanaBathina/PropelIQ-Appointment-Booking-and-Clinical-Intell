# Task - task_002_be_readiness_probe_state_change_logging

## Requirement Reference

- User Story: us_099
- Story Location: .propel/context/tasks/EP-020/us_099/us_099.md
- Acceptance Criteria:
  - AC-2: Given the /ready endpoint is called, When the application has fully started, Then it returns HTTP 200; during startup it returns HTTP 503.
  - AC-4: Given monitoring tools poll the endpoints, When the system transitions between healthy/degraded/unhealthy, Then state changes are logged with timestamp and affected dependencies.
- Edge Case:
  - How does the system handle health check during deployment? Rolling deployment ensures at least one instance responds healthy at all times.

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
| Backend | Microsoft.Extensions.Diagnostics.HealthChecks | 8.x |
| Backend | Serilog | 8.x |

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

Implement the `/ready` readiness probe endpoint and a health state change monitoring service that logs transitions between healthy/degraded/unhealthy states with timestamps and affected dependencies, satisfying AC-2, AC-4, and edge case 2. The readiness probe uses `IHostApplicationLifetime` to track application startup completion — returning HTTP 503 during startup and HTTP 200 once fully initialized. A `HealthStateMonitorService` (BackgroundService) periodically evaluates the `/health` check results, compares them to the previously recorded state, and logs structured events whenever a dependency transitions between states (e.g., `Healthy → Degraded`). This produces an auditable trail of system availability changes for operational dashboards and incident response. The readiness endpoint supports rolling deployments by returning 503 until all services are registered and database migrations have completed (edge case 2).

## Dependent Tasks

- task_001_be_health_check_dependency_status — Requires health check infrastructure, dependency checks, and HealthCheckConfiguration.
- US_001 — Requires backend project scaffold.
- US_007 — Requires health check infrastructure baseline.

## Impacted Components

- **NEW** `src/UPACIP.Api/HealthChecks/ReadinessCheck.cs` — Readiness probe tracking application startup state
- **NEW** `src/UPACIP.Api/HealthChecks/HealthStateMonitorService.cs` — BackgroundService: polls health checks and logs state transitions
- **NEW** `src/UPACIP.Api/HealthChecks/HealthStateRecord.cs` — DTO tracking per-dependency state with timestamp
- **MODIFY** `src/UPACIP.Api/HealthChecks/HealthCheckConfiguration.cs` — Add /ready endpoint mapping and HealthStateMonitorService registration
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register readiness check and state monitor service

## Implementation Plan

1. **Implement `ReadinessCheck` (AC-2)**: Create in `src/UPACIP.Api/HealthChecks/ReadinessCheck.cs`:
   ```csharp
   public class ReadinessCheck : IHealthCheck
   {
       private volatile bool _isReady;
       private readonly ILogger<ReadinessCheck> _logger;

       public ReadinessCheck(ILogger<ReadinessCheck> logger)
       {
           _logger = logger;
       }

       public void MarkReady()
       {
           _isReady = true;
           _logger.LogInformation("READINESS_STATE_CHANGED: Application is ready to accept traffic");
       }

       public Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context,
           CancellationToken cancellationToken = default)
       {
           if (_isReady)
           {
               return Task.FromResult(HealthCheckResult.Healthy("Application is ready"));
           }

           return Task.FromResult(HealthCheckResult.Unhealthy("Application is starting up"));
       }
   }
   ```
   - `_isReady` is a `volatile bool` set to `true` after all startup tasks complete.
   - `MarkReady()` is called from `Program.cs` after the middleware pipeline is fully configured and database migrations have been verified.
   - During startup: returns `Unhealthy` → `/ready` returns HTTP 503 (AC-2).
   - After startup: returns `Healthy` → `/ready` returns HTTP 200 (AC-2).
   - Thread-safe via `volatile` — no locking needed for a single boolean flag.

2. **Register `ReadinessCheck` as a singleton and map `/ready` endpoint (AC-2)**: Update `HealthCheckConfiguration.cs`:
   ```csharp
   public static IServiceCollection AddReadinessCheck(this IServiceCollection services)
   {
       var readinessCheck = new ReadinessCheck(
           services.BuildServiceProvider().GetRequiredService<ILogger<ReadinessCheck>>());
       services.AddSingleton(readinessCheck);

       services.AddHealthChecks()
           .AddCheck<ReadinessCheck>(
               "readiness",
               failureStatus: HealthStatus.Unhealthy,
               tags: new[] { "ready" });

       return services;
   }

   public static WebApplication MapReadinessEndpoint(this WebApplication app)
   {
       app.MapHealthChecks("/ready", new HealthCheckOptions
       {
           Predicate = check => check.Tags.Contains("ready"),
           ResponseWriter = HealthCheckResponseWriter.WriteResponse,
           AllowCachingResponses = false,
           ResultStatusCodes =
           {
               [HealthStatus.Healthy] = StatusCodes.Status200OK,
               [HealthStatus.Degraded] = StatusCodes.Status200OK,
               [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
           }
       });

       return app;
   }
   ```
   Key configuration:
   - **Tag-based filtering**: `/ready` only evaluates checks tagged `ready` — isolates the readiness probe from dependency health checks.
   - **Status codes**: `Unhealthy` → 503 (during startup), `Healthy` → 200 (after startup) (AC-2).
   - **Rolling deployment (edge case 2)**: Load balancers query `/ready`. During deployment, the new instance returns 503 until fully started. The existing instance continues serving traffic. Once the new instance returns 200, the load balancer routes traffic to it.

3. **Call `MarkReady()` after startup completion (AC-2, edge case 2)**: In `Program.cs`, after all middleware and services are configured:
   ```csharp
   var app = builder.Build();

   // ... middleware pipeline configuration ...

   // Mark application as ready after all initialization
   app.Lifetime.ApplicationStarted.Register(() =>
   {
       var readinessCheck = app.Services.GetRequiredService<ReadinessCheck>();
       readinessCheck.MarkReady();
   });

   app.Run();
   ```
   The `ApplicationStarted` event fires after the HTTP server is listening and all hosted services have started — ensuring database connections, background services, and dependency injection are fully initialized before accepting traffic.

4. **Create `HealthStateRecord` DTO**: Create in `src/UPACIP.Api/HealthChecks/HealthStateRecord.cs`:
   ```csharp
   public class HealthStateRecord
   {
       public string DependencyName { get; init; }
       public HealthStatus PreviousStatus { get; set; }
       public HealthStatus CurrentStatus { get; set; }
       public DateTime LastChangedUtc { get; set; }
       public string? Description { get; set; }
   }
   ```
   Tracks the last known state for each dependency. When `CurrentStatus` differs from `PreviousStatus`, a state transition is detected and logged (AC-4).

5. **Implement `HealthStateMonitorService` (AC-4)**: Create in `src/UPACIP.Api/HealthChecks/HealthStateMonitorService.cs`:
   ```csharp
   public class HealthStateMonitorService : BackgroundService
   {
       private readonly HealthCheckService _healthCheckService;
       private readonly ILogger<HealthStateMonitorService> _logger;
       private readonly ConcurrentDictionary<string, HealthStateRecord> _stateMap = new();
       private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(60);

       public HealthStateMonitorService(
           HealthCheckService healthCheckService,
           ILogger<HealthStateMonitorService> logger)
       {
           _healthCheckService = healthCheckService;
           _logger = logger;
       }

       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           // Wait for application to start before polling
           await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

           while (!stoppingToken.IsCancellationRequested)
           {
               try
               {
                   await CheckAndLogTransitionsAsync(stoppingToken);
               }
               catch (Exception ex)
               {
                   _logger.LogError(ex, "HEALTH_MONITOR_ERROR: Failed to evaluate health state");
               }

               await Task.Delay(_pollInterval, stoppingToken);
           }
       }

       private async Task CheckAndLogTransitionsAsync(CancellationToken ct)
       {
           var report = await _healthCheckService.CheckHealthAsync(ct);

           foreach (var entry in report.Entries)
           {
               var name = entry.Key;
               var currentStatus = entry.Value.Status;

               var stateRecord = _stateMap.GetOrAdd(name, _ => new HealthStateRecord
               {
                   DependencyName = name,
                   PreviousStatus = currentStatus,
                   CurrentStatus = currentStatus,
                   LastChangedUtc = DateTime.UtcNow,
                   Description = entry.Value.Description
               });

               if (stateRecord.CurrentStatus != currentStatus)
               {
                   var previousStatus = stateRecord.CurrentStatus;
                   stateRecord.PreviousStatus = previousStatus;
                   stateRecord.CurrentStatus = currentStatus;
                   stateRecord.LastChangedUtc = DateTime.UtcNow;
                   stateRecord.Description = entry.Value.Description;

                   LogStateTransition(name, previousStatus, currentStatus, entry.Value.Description);
               }
           }

           // Log overall status transition
           var overallState = _stateMap.GetOrAdd("_overall", _ => new HealthStateRecord
           {
               DependencyName = "_overall",
               PreviousStatus = report.Status,
               CurrentStatus = report.Status,
               LastChangedUtc = DateTime.UtcNow
           });

           if (overallState.CurrentStatus != report.Status)
           {
               var previousOverall = overallState.CurrentStatus;
               overallState.PreviousStatus = previousOverall;
               overallState.CurrentStatus = report.Status;
               overallState.LastChangedUtc = DateTime.UtcNow;

               var affectedDeps = report.Entries
                   .Where(e => e.Value.Status != HealthStatus.Healthy)
                   .Select(e => e.Key)
                   .ToList();

               _logger.LogWarning(
                   "HEALTH_STATE_CHANGED: Overall {PreviousStatus} → {CurrentStatus}, " +
                   "AffectedDependencies=[{AffectedDependencies}], Timestamp={Timestamp}",
                   previousOverall,
                   report.Status,
                   string.Join(", ", affectedDeps),
                   DateTime.UtcNow.ToString("o"));
           }
       }

       private void LogStateTransition(
           string dependencyName,
           HealthStatus previous,
           HealthStatus current,
           string? description)
       {
           var logLevel = current switch
           {
               HealthStatus.Unhealthy => LogLevel.Critical,
               HealthStatus.Degraded => LogLevel.Warning,
               HealthStatus.Healthy when previous != HealthStatus.Healthy => LogLevel.Information,
               _ => LogLevel.Information
           };

           _logger.Log(logLevel,
               "DEPENDENCY_STATE_CHANGED: {DependencyName} {PreviousStatus} → {CurrentStatus}, " +
               "Description={Description}, Timestamp={Timestamp}",
               dependencyName,
               previous,
               current,
               description,
               DateTime.UtcNow.ToString("o"));
       }
   }
   ```
   Key behaviors (AC-4):
   - **Polling interval**: 60 seconds — balances monitoring responsiveness with resource usage.
   - **State comparison**: `ConcurrentDictionary` stores last known state per dependency. On each poll, current state is compared to stored state.
   - **Transition logging**: When a dependency changes state (e.g., `Healthy → Degraded`), a structured log event is emitted with dependency name, previous/current status, description, and ISO 8601 timestamp.
   - **Overall status tracking**: Logs overall system state transitions with a list of affected (non-healthy) dependencies.
   - **Log level by severity**: `Unhealthy` → Critical, `Degraded` → Warning, recovery to `Healthy` → Information.
   - **Initial delay**: 10-second startup delay prevents false state transitions during application initialization.

6. **Register `HealthStateMonitorService` in DI**: Update `HealthCheckConfiguration.cs` to add the monitor service registration:
   ```csharp
   public static IServiceCollection AddHealthStateMonitoring(this IServiceCollection services)
   {
       services.AddHostedService<HealthStateMonitorService>();
       return services;
   }
   ```
   In `Program.cs`:
   ```csharp
   builder.Services.AddHealthCheckServices(builder.Configuration);
   builder.Services.AddReadinessCheck();
   builder.Services.AddHealthStateMonitoring();
   ```

7. **Map both endpoints in the middleware pipeline**: Update `HealthCheckConfiguration.MapHealthCheckEndpoints` to include both `/health` and `/ready`:
   ```csharp
   public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
   {
       // Liveness probe — all dependency checks
       app.MapHealthChecks("/health", new HealthCheckOptions
       {
           Predicate = check => !check.Tags.Contains("ready"),
           ResponseWriter = HealthCheckResponseWriter.WriteResponse,
           AllowCachingResponses = false
       });

       // Readiness probe — startup check only
       app.MapHealthChecks("/ready", new HealthCheckOptions
       {
           Predicate = check => check.Tags.Contains("ready"),
           ResponseWriter = HealthCheckResponseWriter.WriteResponse,
           AllowCachingResponses = false,
           ResultStatusCodes =
           {
               [HealthStatus.Healthy] = StatusCodes.Status200OK,
               [HealthStatus.Degraded] = StatusCodes.Status200OK,
               [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
           }
       });

       return app;
   }
   ```
   Endpoint separation:
   - `/health` — evaluates dependency checks (database, Redis, AI gateway). Excludes the readiness tag.
   - `/ready` — evaluates only the readiness check. Returns 503 during startup, 200 after.
   - Both endpoints are publicly accessible (no authentication).

8. **Document rolling deployment pattern (edge case 2)**: The readiness probe enables safe rolling deployments:
   - **Pre-deployment**: Instance A serves traffic, `/ready` returns 200.
   - **Deployment starts**: Instance B starts, `/ready` returns 503. Load balancer keeps routing to Instance A.
   - **Instance B initializes**: Database migrations verified, services registered, `MarkReady()` called. `/ready` returns 200.
   - **Load balancer routes**: Traffic shifts to Instance B. Instance A can be stopped or updated.
   - **Result**: Zero downtime — at least one instance is always healthy.

   For IIS deployments (Phase 1), configure the Application Initialization module:
   ```xml
   <!-- web.config -->
   <system.webServer>
     <applicationInitialization doAppInitAfterRestart="true">
       <add initializationPage="/ready" />
     </applicationInitialization>
   </system.webServer>
   ```
   IIS sends a warmup request to `/ready` before routing traffic to the application pool.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── HealthChecks/
│   │   │   ├── DatabaseHealthCheck.cs               ← from task_001
│   │   │   ├── RedisHealthCheck.cs                  ← from task_001
│   │   │   ├── AiGatewayHealthCheck.cs              ← from task_001
│   │   │   ├── HealthCheckResponseWriter.cs         ← from task_001
│   │   │   └── HealthCheckConfiguration.cs          ← from task_001
│   │   ├── Logging/
│   │   ├── Swagger/
│   │   ├── Middleware/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (project scaffold), US_007 (health check baseline), and task_001 (health check dependency status) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/HealthChecks/ReadinessCheck.cs | Readiness probe: 503 during startup, 200 after MarkReady() |
| CREATE | src/UPACIP.Api/HealthChecks/HealthStateMonitorService.cs | BackgroundService: polls health, logs state transitions |
| CREATE | src/UPACIP.Api/HealthChecks/HealthStateRecord.cs | DTO: per-dependency state with previous/current status and timestamp |
| MODIFY | src/UPACIP.Api/HealthChecks/HealthCheckConfiguration.cs | Add /ready mapping, readiness registration, monitor service |
| MODIFY | src/UPACIP.Api/Program.cs | Register readiness check, state monitor, call MarkReady() on startup |

## External References

- [ASP.NET Core Health Checks — Readiness and Liveness](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#separate-readiness-and-liveness-probes)
- [IHostApplicationLifetime — Application Lifecycle](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihostapplicationlifetime)
- [Health Endpoint Monitoring Pattern — Azure Architecture](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring)
- [IIS Application Initialization Module](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization)
- [Rolling Deployment — Zero Downtime](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/deployment-jobs#rolling-deployment-strategy)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Test readiness endpoint (application must be running)
Invoke-RestMethod -Uri http://localhost:5000/ready -Method Get

# Test health endpoint
Invoke-RestMethod -Uri http://localhost:5000/health -Method Get
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] GET /ready returns HTTP 503 during application startup (AC-2)
- [ ] GET /ready returns HTTP 200 after application fully started (AC-2)
- [ ] ReadinessCheck.MarkReady() is called via ApplicationStarted lifetime event
- [ ] HealthStateMonitorService polls health checks every 60 seconds
- [ ] State transition log emitted when dependency changes from Healthy to Degraded (AC-4)
- [ ] State transition log includes timestamp, dependency name, previous/current status (AC-4)
- [ ] Overall state transition log includes list of affected dependencies (AC-4)
- [ ] Log level: Critical for Unhealthy, Warning for Degraded, Information for recovery (AC-4)
- [ ] /ready endpoint returns 503 before MarkReady() during rolling deployment (edge case 2)

## Implementation Checklist

- [ ] Implement ReadinessCheck with volatile _isReady flag and MarkReady()
- [ ] Register ReadinessCheck as singleton with "ready" tag
- [ ] Map /ready endpoint with 503 for Unhealthy, 200 for Healthy
- [ ] Call MarkReady() via ApplicationStarted lifetime event in Program.cs
- [ ] Create HealthStateRecord DTO for per-dependency state tracking
- [ ] Implement HealthStateMonitorService with 60-second polling and ConcurrentDictionary
- [ ] Log state transitions with structured fields (dependency, status, timestamp)
- [ ] Document rolling deployment pattern with IIS Application Initialization
