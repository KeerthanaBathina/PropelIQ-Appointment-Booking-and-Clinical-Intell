# Task - task_001_be_health_check_dependency_status

## Requirement Reference

- User Story: us_099
- Story Location: .propel/context/tasks/EP-020/us_099/us_099.md
- Acceptance Criteria:
  - AC-1: Given the /health endpoint is called, When it responds, Then it returns HTTP 200 with a JSON body reporting overall status and individual dependency statuses (database, Redis, AI gateway) within 500ms.
  - AC-3: Given a dependency is degraded, When /health is called, Then the overall status shows "degraded" with the specific dependency flagged and the response still completes within 500ms.
- Edge Case:
  - What happens when the health check itself times out? Load balancer treats timeout as unhealthy; health endpoint has a hard 500ms internal timeout.

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
| Backend | AspNetCore.HealthChecks.NpgSql | 8.x |
| Backend | AspNetCore.HealthChecks.Redis | 8.x |
| Backend | Microsoft.Extensions.Diagnostics.HealthChecks | 8.x |
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

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `/health` endpoint using ASP.NET Core Health Checks with dependency-specific checks for PostgreSQL, Redis, and AI gateway, satisfying AC-1, AC-3, NFR-020, and edge case 1. The endpoint returns a JSON body reporting overall status (`Healthy`, `Degraded`, `Unhealthy`) and individual dependency statuses, each with name, status, duration, and optional description. All checks execute with a hard 500ms internal timeout — if any individual check exceeds its budget, it is reported as `Unhealthy` rather than blocking the response (edge case 1). Checks run in parallel via `Task.WhenAll` to stay within the 500ms budget even when multiple dependencies are slow. A `Degraded` overall status is returned when at least one dependency is degraded but others are healthy (AC-3), while `Unhealthy` is returned when any critical dependency (database) is unreachable.

## Dependent Tasks

- US_001 — Requires backend project scaffold with `Program.cs`.
- US_007 — Requires health check infrastructure baseline.

## Impacted Components

- **NEW** `src/UPACIP.Api/HealthChecks/DatabaseHealthCheck.cs` — Custom PostgreSQL health check with connection and query validation
- **NEW** `src/UPACIP.Api/HealthChecks/RedisHealthCheck.cs` — Custom Redis health check with PING command validation
- **NEW** `src/UPACIP.Api/HealthChecks/AiGatewayHealthCheck.cs` — Custom AI gateway health check (OpenAI/Claude endpoint reachability)
- **NEW** `src/UPACIP.Api/HealthChecks/HealthCheckResponseWriter.cs` — Custom JSON response writer for detailed dependency status
- **NEW** `src/UPACIP.Api/HealthChecks/HealthCheckConfiguration.cs` — Extension method for health check service registration
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register health checks and map /health endpoint
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add health check NuGet packages

## Implementation Plan

1. **Add health check NuGet packages**: Add to `src/UPACIP.Api/UPACIP.Api.csproj`:
   ```xml
   <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="8.*" />
   <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="8.*" />
   ```
   The `Microsoft.Extensions.Diagnostics.HealthChecks` package is already included via `Microsoft.AspNetCore.App` framework reference. The community packages provide pre-built checks for PostgreSQL (Npgsql connection test) and Redis (PING command).

2. **Implement `DatabaseHealthCheck` (AC-1 — database dependency)**: Create in `src/UPACIP.Api/HealthChecks/DatabaseHealthCheck.cs`:
   ```csharp
   public class DatabaseHealthCheck : IHealthCheck
   {
       private readonly ApplicationDbContext _dbContext;
       private readonly ILogger<DatabaseHealthCheck> _logger;

       public DatabaseHealthCheck(ApplicationDbContext dbContext, ILogger<DatabaseHealthCheck> logger)
       {
           _dbContext = dbContext;
           _logger = logger;
       }

       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context,
           CancellationToken cancellationToken = default)
       {
           try
           {
               // Test connection with a lightweight query
               var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
               if (!canConnect)
               {
                   return HealthCheckResult.Unhealthy("PostgreSQL connection failed",
                       data: new Dictionary<string, object> { ["server"] = "PostgreSQL 16" });
               }

               // Execute a simple query to verify read capability
               await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

               return HealthCheckResult.Healthy("PostgreSQL connection successful",
                   data: new Dictionary<string, object> { ["server"] = "PostgreSQL 16" });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "HEALTH_CHECK_FAILED: Database health check exception");
               return HealthCheckResult.Unhealthy("PostgreSQL health check failed",
                   exception: ex,
                   data: new Dictionary<string, object> { ["error"] = ex.Message });
           }
       }
   }
   ```
   - Database is a **critical** dependency — `Unhealthy` result triggers overall `Unhealthy` status.
   - Uses `CanConnectAsync` (connection pool check) + `SELECT 1` (query execution check).
   - Exception details included in `data` dictionary for diagnostic output.

3. **Implement `RedisHealthCheck` (AC-1 — Redis dependency)**: Create in `src/UPACIP.Api/HealthChecks/RedisHealthCheck.cs`:
   ```csharp
   public class RedisHealthCheck : IHealthCheck
   {
       private readonly IConnectionMultiplexer _redis;
       private readonly ILogger<RedisHealthCheck> _logger;

       public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
       {
           _redis = redis;
           _logger = logger;
       }

       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context,
           CancellationToken cancellationToken = default)
       {
           try
           {
               var db = _redis.GetDatabase();
               var latency = await db.PingAsync();

               if (latency > TimeSpan.FromMilliseconds(100))
               {
                   return HealthCheckResult.Degraded(
                       $"Redis responding slowly: {latency.TotalMilliseconds:F0}ms",
                       data: new Dictionary<string, object>
                       {
                           ["latency_ms"] = latency.TotalMilliseconds,
                           ["server"] = "Upstash Redis 7.x"
                       });
               }

               return HealthCheckResult.Healthy(
                   $"Redis PING: {latency.TotalMilliseconds:F0}ms",
                   data: new Dictionary<string, object>
                   {
                       ["latency_ms"] = latency.TotalMilliseconds,
                       ["server"] = "Upstash Redis 7.x"
                   });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "HEALTH_CHECK_FAILED: Redis health check exception");
               return HealthCheckResult.Degraded("Redis unavailable — cache functionality degraded",
                   exception: ex,
                   data: new Dictionary<string, object> { ["error"] = ex.Message });
           }
       }
   }
   ```
   - Redis is a **non-critical** dependency — failure returns `Degraded` (not `Unhealthy`) since the application can operate without cache (AC-3).
   - PING latency > 100ms triggers `Degraded` to flag slow cache performance.

4. **Implement `AiGatewayHealthCheck` (AC-1 — AI gateway dependency)**: Create in `src/UPACIP.Api/HealthChecks/AiGatewayHealthCheck.cs`:
   ```csharp
   public class AiGatewayHealthCheck : IHealthCheck
   {
       private readonly IHttpClientFactory _httpClientFactory;
       private readonly IConfiguration _configuration;
       private readonly ILogger<AiGatewayHealthCheck> _logger;

       public AiGatewayHealthCheck(
           IHttpClientFactory httpClientFactory,
           IConfiguration configuration,
           ILogger<AiGatewayHealthCheck> logger)
       {
           _httpClientFactory = httpClientFactory;
           _configuration = configuration;
           _logger = logger;
       }

       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context,
           CancellationToken cancellationToken = default)
       {
           try
           {
               var client = _httpClientFactory.CreateClient("AiGatewayHealthCheck");
               var openAiUrl = _configuration["AiGateway:OpenAi:BaseUrl"] ?? "https://api.openai.com";

               // Lightweight HEAD request to verify endpoint reachability
               var request = new HttpRequestMessage(HttpMethod.Head, $"{openAiUrl}/v1/models");
               request.Headers.Add("Authorization",
                   $"Bearer {_configuration["AiGateway:OpenAi:ApiKey"]}");

               var response = await client.SendAsync(request, cancellationToken);

               if (response.IsSuccessStatusCode)
               {
                   return HealthCheckResult.Healthy("AI Gateway (OpenAI) reachable",
                       data: new Dictionary<string, object> { ["provider"] = "OpenAI GPT-4o-mini" });
               }

               return HealthCheckResult.Degraded(
                   $"AI Gateway returned {(int)response.StatusCode}",
                   data: new Dictionary<string, object>
                   {
                       ["statusCode"] = (int)response.StatusCode,
                       ["provider"] = "OpenAI GPT-4o-mini"
                   });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "HEALTH_CHECK_FAILED: AI Gateway health check exception");
               return HealthCheckResult.Degraded("AI Gateway unreachable — AI features degraded",
                   exception: ex,
                   data: new Dictionary<string, object> { ["error"] = ex.Message });
           }
       }
   }
   ```
   - AI gateway is a **non-critical** dependency — failure returns `Degraded` since the system can fall back to manual workflows (AC-3).
   - Uses a lightweight HEAD request to `/v1/models` — does not consume API tokens.
   - API key is read from configuration (not hardcoded).

5. **Implement `HealthCheckResponseWriter` for detailed JSON output (AC-1, AC-3)**: Create in `src/UPACIP.Api/HealthChecks/HealthCheckResponseWriter.cs`:
   ```csharp
   public static class HealthCheckResponseWriter
   {
       public static async Task WriteResponse(HttpContext context, HealthReport report)
       {
           context.Response.ContentType = "application/json; charset=utf-8";

           var response = new
           {
               status = report.Status.ToString(),
               totalDuration = report.TotalDuration.TotalMilliseconds,
               timestamp = DateTime.UtcNow.ToString("o"),
               entries = report.Entries.Select(e => new
               {
                   name = e.Key,
                   status = e.Value.Status.ToString(),
                   duration = e.Value.Duration.TotalMilliseconds,
                   description = e.Value.Description,
                   data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                   exception = e.Value.Exception?.Message
               })
           };

           var options = new JsonSerializerOptions
           {
               PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
               DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
               WriteIndented = true
           };

           await context.Response.WriteAsJsonAsync(response, options);
       }
   }
   ```
   JSON response structure (AC-1):
   ```json
   {
     "status": "Degraded",
     "totalDuration": 142.5,
     "timestamp": "2026-04-17T10:30:00.000Z",
     "entries": [
       {
         "name": "postgresql",
         "status": "Healthy",
         "duration": 12.3,
         "description": "PostgreSQL connection successful",
         "data": { "server": "PostgreSQL 16" }
       },
       {
         "name": "redis",
         "status": "Degraded",
         "duration": 125.7,
         "description": "Redis responding slowly: 126ms",
         "data": { "latency_ms": 125.7, "server": "Upstash Redis 7.x" }
       },
       {
         "name": "ai-gateway",
         "status": "Healthy",
         "duration": 89.1,
         "description": "AI Gateway (OpenAI) reachable",
         "data": { "provider": "OpenAI GPT-4o-mini" }
       }
     ]
   }
   ```
   - `status` reflects the worst individual status: any `Unhealthy` → overall `Unhealthy`, any `Degraded` → overall `Degraded` (AC-3).
   - `totalDuration` shows aggregate response time — must be < 500ms (AC-1).
   - `exception` field is only included when non-null.

6. **Create `HealthCheckConfiguration` extension method**: Create in `src/UPACIP.Api/HealthChecks/HealthCheckConfiguration.cs`:
   ```csharp
   public static class HealthCheckConfiguration
   {
       public static IServiceCollection AddHealthCheckServices(this IServiceCollection services,
           IConfiguration configuration)
       {
           services.AddHealthChecks()
               .AddCheck<DatabaseHealthCheck>(
                   "postgresql",
                   failureStatus: HealthStatus.Unhealthy,
                   tags: new[] { "db", "critical" },
                   timeout: TimeSpan.FromMilliseconds(400))
               .AddCheck<RedisHealthCheck>(
                   "redis",
                   failureStatus: HealthStatus.Degraded,
                   tags: new[] { "cache", "non-critical" },
                   timeout: TimeSpan.FromMilliseconds(400))
               .AddCheck<AiGatewayHealthCheck>(
                   "ai-gateway",
                   failureStatus: HealthStatus.Degraded,
                   tags: new[] { "ai", "non-critical" },
                   timeout: TimeSpan.FromMilliseconds(400));

           return services;
       }

       public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
       {
           app.MapHealthChecks("/health", new HealthCheckOptions
           {
               ResponseWriter = HealthCheckResponseWriter.WriteResponse,
               AllowCachingResponses = false
           });

           return app;
       }
   }
   ```
   Key configuration:
   - **Per-check timeout (edge case 1)**: Each check has a 400ms timeout — if exceeded, the check is automatically cancelled and reported as `Unhealthy`. This leaves 100ms headroom for response serialization, ensuring the total response stays within 500ms.
   - **`failureStatus`**: Database failures are `Unhealthy` (critical). Redis and AI gateway failures are `Degraded` (non-critical, AC-3).
   - **Tags**: Enable filtered health check queries (e.g., check only `critical` dependencies).
   - **`AllowCachingResponses = false`**: Health checks must run fresh on every request — no caching.

7. **Configure HTTP client for AI gateway health check**: Register a named `HttpClient` with a short timeout for health check purposes:
   ```csharp
   builder.Services.AddHttpClient("AiGatewayHealthCheck", client =>
   {
       client.Timeout = TimeSpan.FromMilliseconds(400);
       client.DefaultRequestHeaders.Add("Accept", "application/json");
   });
   ```
   The 400ms timeout prevents the AI gateway check from exceeding the health endpoint's overall 500ms budget (edge case 1).

8. **Integrate health checks in `Program.cs`**: Add service registration and endpoint mapping:
   ```csharp
   // In service registration
   builder.Services.AddHealthCheckServices(builder.Configuration);

   // In endpoint mapping (after authentication middleware, no [Authorize] required)
   app.MapHealthCheckEndpoints();
   ```
   The `/health` endpoint is publicly accessible (no authentication) so that load balancers, monitoring tools, and deployment scripts can query it without credentials.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Logging/
│   │   │   └── SerilogConfiguration.cs              ← from US_098 task_001
│   │   ├── Swagger/
│   │   │   └── SwaggerConfiguration.cs              ← from US_098 task_002
│   │   ├── Middleware/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── ...
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (project scaffold), US_007 (health check baseline), and US_098 (Serilog/Swagger) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/HealthChecks/DatabaseHealthCheck.cs | PostgreSQL health check: CanConnectAsync + SELECT 1 |
| CREATE | src/UPACIP.Api/HealthChecks/RedisHealthCheck.cs | Redis health check: PING with latency threshold |
| CREATE | src/UPACIP.Api/HealthChecks/AiGatewayHealthCheck.cs | AI gateway health check: HEAD request to OpenAI API |
| CREATE | src/UPACIP.Api/HealthChecks/HealthCheckResponseWriter.cs | Custom JSON response writer with per-dependency detail |
| CREATE | src/UPACIP.Api/HealthChecks/HealthCheckConfiguration.cs | Extension methods: AddHealthCheckServices, MapHealthCheckEndpoints |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add AspNetCore.HealthChecks.NpgSql and Redis packages |
| MODIFY | src/UPACIP.Api/Program.cs | Register health check services and map /health endpoint |

## External References

- [ASP.NET Core Health Checks — Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [AspNetCore.Diagnostics.HealthChecks — Community Library](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)
- [Health Check Response Writers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#customize-output)
- [Kubernetes Liveness/Readiness Probes Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Test health endpoint (application must be running)
Invoke-RestMethod -Uri http://localhost:5000/health -Method Get
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding health check packages
- [ ] GET /health returns HTTP 200 with JSON body (AC-1)
- [ ] Response includes overall status and per-dependency statuses (AC-1)
- [ ] Response includes database, Redis, and AI gateway entries (AC-1)
- [ ] Total response time is under 500ms (AC-1, NFR-020)
- [ ] When Redis is slow (>100ms PING), overall status shows "Degraded" (AC-3)
- [ ] When AI gateway is unreachable, overall status shows "Degraded" (AC-3)
- [ ] When database is unreachable, overall status shows "Unhealthy" (AC-3)
- [ ] Individual check timeout at 400ms prevents response exceeding 500ms (edge case 1)
- [ ] Health endpoint is accessible without authentication

## Implementation Checklist

- [ ] Add health check NuGet packages to UPACIP.Api.csproj
- [ ] Implement DatabaseHealthCheck with CanConnectAsync and SELECT 1
- [ ] Implement RedisHealthCheck with PING and latency degradation threshold
- [ ] Implement AiGatewayHealthCheck with HEAD request to OpenAI
- [ ] Create HealthCheckResponseWriter with detailed JSON output
- [ ] Create HealthCheckConfiguration with per-check timeouts and failure statuses
- [ ] Register named HttpClient for AI gateway with 400ms timeout
- [ ] Map /health endpoint in Program.cs without authentication
