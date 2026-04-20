# Task - task_001_be_health_check_endpoints

## Requirement Reference

- User Story: us_007
- Story Location: .propel/context/tasks/EP-TECH/us_007/us_007.md
- Acceptance Criteria:
  - AC-1: Given health check endpoints are implemented, When a GET request is sent to `/health`, Then the system returns 200 OK with JSON status within 500ms.
  - AC-2: Given the `/ready` endpoint is called, When all dependencies (database, Redis) are available, Then it returns 200 OK; otherwise it returns 503 Service Unavailable with details.
- Edge Case:
  - What happens when the database is down during health check? `/health` returns 200 (app alive) but `/ready` returns 503 with "Database: Unhealthy" detail.

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
| Library | AspNetCore.HealthChecks.NpgSql | 8.x |
| Library | AspNetCore.HealthChecks.Redis | 8.x |
| Library | Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis (StackExchange.Redis) | 7.x / 2.x |

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

Implement ASP.NET Core health check endpoints at `/health` (liveness) and `/ready` (readiness) per TR-020. The `/health` endpoint returns a simple 200 OK JSON response confirming the application process is alive — it does NOT check external dependencies. The `/ready` endpoint probes PostgreSQL (via EF Core `DbContext`) and Redis connectivity, returning 200 OK when all dependencies are healthy or 503 Service Unavailable with per-dependency status details when any dependency is unhealthy. Both endpoints use a custom JSON `ResponseWriter` for structured monitoring output. Response time must be under 500ms (NFR-020).

## Dependent Tasks

- US_001 task_001_be_solution_scaffold — Backend solution with `Program.cs` and middleware pipeline must exist.
- US_003 task_002_be_efcore_integration — `ApplicationDbContext` must be registered for the database health check.
- US_004 task_001_be_redis_connection_setup — Redis `IDistributedCache` / `IConnectionMultiplexer` must be configured for the Redis health check.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add health check NuGet packages
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register health check services and map `/health` and `/ready` endpoints
- **NEW** `src/UPACIP.Api/HealthChecks/HealthCheckResponseWriter.cs` — Custom JSON response writer producing structured `{ status, totalDuration, entries: { name, status, duration, description } }` output
- **NEW** `src/UPACIP.Api/HealthChecks/StartupHealthCheck.cs` — Startup probe that reports unhealthy until all initialization completes (optional readiness gate)

## Implementation Plan

1. **Add health check NuGet packages**: Install `AspNetCore.HealthChecks.NpgSql` (8.x) for PostgreSQL connectivity probing, `AspNetCore.HealthChecks.Redis` (8.x) for Redis PING probing, and `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (8.x) for `DbContext` integration. These are the community-standard health check packages for ASP.NET Core.

2. **Register health check services**: In `Program.cs`, call `builder.Services.AddHealthChecks()` to enable the health checks middleware. Chain `.AddDbContextCheck<ApplicationDbContext>(name: "database", tags: new[] { "ready" })` for the PostgreSQL EF Core probe. Chain `.AddRedis(redisConnectionString, name: "redis", tags: new[] { "ready" })` for the Redis probe. Tag both with `"ready"` so they are only invoked by the `/ready` endpoint.

3. **Implement custom JSON response writer**: Create `HealthCheckResponseWriter.cs` that serializes the `HealthReport` into a JSON object with `status` (Healthy/Degraded/Unhealthy), `totalDuration` (milliseconds), and `entries` array. Each entry includes `name`, `status`, `duration`, and `description`. Use `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`. Set `Content-Type: application/json`.

4. **Map `/health` liveness endpoint**: Map `app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthCheckResponseWriter.WriteAsync })`. Setting `Predicate = _ => false` excludes all registered checks — this endpoint only confirms the process is alive (200 OK). It returns immediately without probing dependencies, ensuring sub-500ms response.

5. **Map `/ready` readiness endpoint**: Map `app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = HealthCheckResponseWriter.WriteAsync, ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 200, [HealthStatus.Unhealthy] = 503 } })`. This filters to only `"ready"`-tagged checks (database + Redis). Returns 503 with per-dependency details when any dependency is down.

6. **Configure health check timeout**: Set a timeout of 3 seconds on each health check registration to prevent slow dependency probes from exceeding the 500ms target under normal conditions. Use `.AddDbContextCheck<ApplicationDbContext>(..., timeout: TimeSpan.FromSeconds(3))` and equivalent for Redis.

7. **Allow anonymous access**: Ensure both health check endpoints allow anonymous access (no JWT required) since they are used by monitoring tools and load balancers. Add `.AllowAnonymous()` to the endpoint mappings.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── Controllers/
│   ├── UPACIP.Service/
│   │   └── Caching/
│   │       ├── ICacheService.cs
│   │       └── RedisCacheService.cs
│   └── UPACIP.DataAccess/
│       └── ApplicationDbContext.cs
├── app/
└── scripts/
```

> Assumes US_001, US_003, and US_004 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `AspNetCore.HealthChecks.NpgSql` 8.x, `AspNetCore.HealthChecks.Redis` 8.x, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 8.x NuGet packages |
| MODIFY | src/UPACIP.Api/Program.cs | Register health check services with DB + Redis probes (tagged "ready"), map `/health` and `/ready` endpoints with custom response writer |
| CREATE | src/UPACIP.Api/HealthChecks/HealthCheckResponseWriter.cs | Custom JSON response writer serializing HealthReport to `{ status, totalDuration, entries[] }` |

## External References

- [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0)
- [AspNetCore.Diagnostics.HealthChecks GitHub](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)
- [EF Core DbContext health check](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0#entity-framework-core-dbcontext-health-check)
- [Health check response writer customization](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0#customize-output)

## Build Commands

```powershell
# Restore packages
dotnet restore src/UPACIP.Api/UPACIP.Api.csproj

# Build
dotnet build src/UPACIP.Api/UPACIP.Api.csproj --no-restore

# Run and test health endpoints
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test liveness (should return 200 always)
curl -s http://localhost:5000/health | ConvertFrom-Json

# Test readiness (200 if DB+Redis up, 503 if any down)
curl -s -o NUL -w "%{http_code}" http://localhost:5000/ready

# Verify response time < 500ms
Measure-Command { Invoke-WebRequest -Uri http://localhost:5000/health -UseBasicParsing } | Select-Object TotalMilliseconds
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors after adding health check packages
- [x] GET `/health` returns 200 OK with JSON `{ "status": "Healthy" }` when the app is running
- [x] GET `/health` does NOT probe database or Redis (returns immediately)
- [ ] GET `/ready` returns 200 OK with JSON details when database and Redis are both healthy
- [ ] GET `/ready` returns 503 Service Unavailable when database is unreachable, with `"database": "Unhealthy"` in response
- [x] GET `/ready` returns 503 Service Unavailable when Redis is unreachable, with `"redis": "Unhealthy"` in response
- [x] Both endpoints respond within 500ms under normal conditions
- [x] Both endpoints are accessible without authentication (anonymous)

## Implementation Checklist

- [x] Add `AspNetCore.HealthChecks.NpgSql` 8.x, `AspNetCore.HealthChecks.Redis` 8.x, and `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 8.x to `UPACIP.Api.csproj`
- [x] Register health check services in `Program.cs` with `AddHealthChecks()`, chain `AddDbContextCheck<ApplicationDbContext>` (tag: "ready") and `AddRedis` (tag: "ready") with 3-second timeouts
- [x] Create `HealthChecks/HealthCheckResponseWriter.cs` with `WriteAsync` method that serializes `HealthReport` to JSON with `status`, `totalDuration`, and per-entry details using `System.Text.Json`
- [x] Map `/health` liveness endpoint with `Predicate = _ => false` (no dependency probes) and custom response writer
- [x] Map `/ready` readiness endpoint filtering to `"ready"` tag, returning 503 for Unhealthy status, with custom response writer
- [x] Ensure both endpoints allow anonymous access (`.AllowAnonymous()`)
