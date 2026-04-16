# Task - task_001_be_redis_connection_setup

## Requirement Reference

- User Story: us_004
- Story Location: .propel/context/tasks/EP-TECH/us_004/us_004.md
- Acceptance Criteria:
  - AC-1: Given Upstash Redis is configured, When the application starts, Then it establishes a connection to Redis and logs successful connection status.
  - AC-4: Given Redis is unavailable, When an API request is made, Then the system falls back to database queries without error and logs a warning.
- Edge Case:
  - How does the system handle Redis connection timeout? Circuit breaker opens after 3 failures; cache calls bypass Redis until recovery.

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
| Caching | Upstash Redis | 7.x |
| Redis Client | StackExchange.Redis | 2.x |
| Distributed Cache | Microsoft.Extensions.Caching.StackExchangeRedis | 8.x |

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

Configure the Upstash Redis 7.x connection infrastructure in the ASP.NET Core backend. Register `IDistributedCache` backed by Redis using `StackExchange.Redis`, configure the connection string from `appsettings.json` with TLS (required by Upstash), add startup health check logging that confirms Redis connectivity, and configure an exponential retry policy for transient connection failures. This task establishes the Redis connection layer that the caching service (task_002) will consume.

## Dependent Tasks

- US_001 task_001_be_solution_scaffold — Backend solution with layered architecture must exist.
- US_001 task_002_be_middleware_pipeline — Error handling and correlation ID middleware must exist for logging context.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add StackExchange.Redis and Microsoft.Extensions.Caching.StackExchangeRedis NuGet packages
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IDistributedCache with Redis provider, add startup connection health check
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Redis connection string configuration section
- **MODIFY** `src/UPACIP.Api/appsettings.Development.json` — Add development-specific Redis connection string

## Implementation Plan

1. **Install NuGet packages**: Add `Microsoft.Extensions.Caching.StackExchangeRedis` (8.x) to the Api project. This transitively includes `StackExchange.Redis` and provides the `AddStackExchangeRedisCache` extension method for `IDistributedCache` registration.
2. **Configure Redis connection string**: Add a `Redis` section to `appsettings.json` with the Upstash Redis connection string. Upstash requires TLS, so the connection string format is: `{endpoint}:{port},password={password},ssl=True,abortConnect=False`. Store the password in user secrets or environment variables — never hardcode credentials.
3. **Register IDistributedCache**: In `Program.cs`, call `AddStackExchangeRedisCache` with the connection string from configuration. Set `InstanceName` to `upacip:` as a key prefix to namespace all cache entries. Configure `ConfigurationOptions` with `AbortOnConnectFail = false` so the application starts even when Redis is temporarily unavailable.
4. **Configure reconnect retry policy**: Set the `ReconnectRetryPolicy` to `ExponentialRetry(5000)` on the `ConfigurationOptions` so transient disconnections trigger progressive backoff retries rather than immediate failure.
5. **Add startup health check**: After building the app but before `app.Run()`, add a startup probe that attempts a Redis PING. On success, log an informational message with connection details (endpoint, no password). On failure, log a warning indicating Redis is unreachable and the application will operate with cache bypass — do not throw or prevent startup.
6. **Validate connection**: Run the application and confirm the startup log shows a successful Redis connection, then stop Redis and confirm the warning log appears and the application still starts.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   ├── Models/
│   │   │   └── ErrorResponse.cs
│   │   └── Controllers/
│   │       └── WeatherForecastController.cs
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       └── ApplicationDbContext.cs
└── scripts/
    └── ...
```

> Assumes US_001 tasks are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `Microsoft.Extensions.Caching.StackExchangeRedis` 8.x NuGet package |
| MODIFY | src/UPACIP.Api/Program.cs | Register `AddStackExchangeRedisCache` with connection string, `AbortOnConnectFail=false`, `ExponentialRetry`, instance name prefix, and startup PING health check |
| MODIFY | src/UPACIP.Api/appsettings.json | Add `Redis:ConnectionString` configuration with placeholder for Upstash endpoint |
| MODIFY | src/UPACIP.Api/appsettings.Development.json | Add development Redis connection string (localhost or Upstash dev instance) |

## External References

- [Microsoft.Extensions.Caching.StackExchangeRedis](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0#distributed-redis-cache)
- [StackExchange.Redis configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration)
- [Upstash Redis .NET quickstart](https://upstash.com/docs/redis/sdks/dotnet)
- [StackExchange.Redis reconnect retry policy](https://stackexchange.github.io/StackExchange.Redis/Configuration#reconnectretrypolicy)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Run the API (verify Redis log on startup)
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors and zero warnings after adding Redis packages
- [ ] Application startup logs "Redis connection established" when Upstash Redis is reachable
- [ ] Application startup logs a warning (not an error/crash) when Redis is unreachable
- [ ] `IDistributedCache` is resolvable from the DI container
- [ ] Redis connection string is not hardcoded — password is read from user secrets or environment variable
- [ ] Connection uses TLS (`ssl=True` in connection string)

## Implementation Checklist

- [ ] Add `Microsoft.Extensions.Caching.StackExchangeRedis` 8.x NuGet package to `UPACIP.Api.csproj`
- [ ] Add `Redis:ConnectionString` to `appsettings.json` with format `{endpoint}:{port},password={from-secrets},ssl=True,abortConnect=False`
- [ ] Register `AddStackExchangeRedisCache` in `Program.cs` with `InstanceName = "upacip:"`, `AbortOnConnectFail = false`, and `ReconnectRetryPolicy = new ExponentialRetry(5000)`
- [ ] Add startup Redis PING health check in `Program.cs` that logs success or warning without blocking application startup
- [ ] Verify `dotnet build` succeeds with zero errors and zero warnings
