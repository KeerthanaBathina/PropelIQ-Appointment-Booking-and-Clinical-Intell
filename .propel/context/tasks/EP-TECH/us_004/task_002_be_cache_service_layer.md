# Task - task_002_be_cache_service_layer

## Requirement Reference

- User Story: us_004
- Story Location: .propel/context/tasks/EP-TECH/us_004/us_004.md
- Acceptance Criteria:
  - AC-2: Given Redis caching is available, When a cache-eligible API request is made, Then the response is cached with a default 5-minute TTL.
  - AC-3: Given a cached entry exists, When the TTL expires, Then the next request fetches fresh data from the database and re-populates the cache.
  - AC-4: Given Redis is unavailable, When an API request is made, Then the system falls back to database queries without error and logs a warning.
- Edge Case:
  - What happens when Upstash free tier limit (10K requests/day) is reached? System falls back to direct database queries and alerts admin.
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
| Resilience | Polly | 8.x |
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

Implement a generic caching service abstraction (`ICacheService`) in the Service layer that wraps `IDistributedCache` with a cache-aside pattern, a default 5-minute TTL (per NFR-030), and a Polly circuit breaker that gracefully falls back to database queries when Redis is unavailable. The circuit breaker opens after 3 consecutive failures and stays open for 30 seconds before attempting recovery. This service is the single caching interface that all subsequent feature implementations will consume.

## Dependent Tasks

- task_001_be_redis_connection_setup — Redis connection and `IDistributedCache` must be registered in DI before the cache service can consume it.

## Impacted Components

- **MODIFY** `src/UPACIP.Service/UPACIP.Service.csproj` — Add Microsoft.Extensions.Caching.Abstractions and Polly NuGet packages
- **NEW** `src/UPACIP.Service/Caching/ICacheService.cs` — Caching service interface with generic get/set/remove methods
- **NEW** `src/UPACIP.Service/Caching/RedisCacheService.cs` — Implementation wrapping IDistributedCache with circuit breaker, TTL, and serialization
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register ICacheService with RedisCacheService in DI container

## Implementation Plan

1. **Install NuGet packages**: Add `Microsoft.Extensions.Caching.Abstractions` (8.x) and `Microsoft.Extensions.Http.Polly` or `Polly` (8.x) to the Service project. The caching abstractions provide `IDistributedCache` interface access without coupling to Redis implementation.
2. **Define ICacheService interface**: Create a generic caching interface in `src/UPACIP.Service/Caching/ICacheService.cs` with methods: `GetAsync<T>(string key)`, `SetAsync<T>(string key, T value, TimeSpan? expiration = null)`, `RemoveAsync(string key)`, and `GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)` for the cache-aside pattern.
3. **Implement RedisCacheService**: Create the implementation that wraps `IDistributedCache`. Serialize/deserialize values using `System.Text.Json`. Apply a default 5-minute sliding expiration (per NFR-030) when no explicit TTL is provided. The `GetOrSetAsync` method implements cache-aside: check cache first, on miss call the factory to get data from the database, store in cache, and return.
4. **Add Polly circuit breaker**: Wrap all `IDistributedCache` calls in a Polly `CircuitBreakerAsync` policy. Configure: break after 3 consecutive exceptions (`exceptionsAllowedBeforeBreaking: 3`), with a 30-second duration of break. When the circuit is open, catch the `BrokenCircuitException`, log a warning, and fall through to the database query factory — no error propagated to the caller.
5. **Add Redis exception handling**: Catch `RedisConnectionException`, `RedisTimeoutException`, and general `Exception` from cache operations. On any cache failure, log a warning with the cache key and exception details, then return the fallback (null for get, skip for set) — cache failures must never break the request pipeline.
6. **Register in DI**: In `Program.cs`, register `ICacheService` as `RedisCacheService` with singleton lifetime (the underlying `IDistributedCache` and circuit breaker state should persist across requests).
7. **Add usage example**: Create a brief integration verification by adding a cache-through call in an existing controller (e.g., `WeatherForecastController`) to demonstrate the cache-aside pattern works end-to-end.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs (with Redis IDistributedCache registered)
│   │   ├── appsettings.json (with Redis connection string)
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

> Assumes task_001_be_redis_connection_setup is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Service/UPACIP.Service.csproj | Add `Microsoft.Extensions.Caching.Abstractions` 8.x and `Polly` 8.x NuGet packages |
| CREATE | src/UPACIP.Service/Caching/ICacheService.cs | Generic caching interface with GetAsync, SetAsync, RemoveAsync, GetOrSetAsync methods |
| CREATE | src/UPACIP.Service/Caching/RedisCacheService.cs | Implementation wrapping IDistributedCache with System.Text.Json serialization, 5-min default TTL, Polly circuit breaker (3 failures / 30s break), and graceful fallback on Redis unavailability |
| MODIFY | src/UPACIP.Api/Program.cs | Register `ICacheService` as `RedisCacheService` (singleton) in DI container |

## External References

- [ASP.NET Core distributed caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)
- [Polly circuit breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [Cache-aside pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside)
- [System.Text.Json serialization](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Run the API and test caching
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test cache-aside (first request: miss, second: hit)
curl -i https://localhost:{port}/weatherforecast
curl -i https://localhost:{port}/weatherforecast
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors and zero warnings after adding Polly and caching abstractions
- [x] `ICacheService.GetOrSetAsync` returns data from cache on second call (cache hit)
- [x] Cache entries expire after 5 minutes (default TTL) — subsequent request fetches fresh data
- [x] When Redis is stopped, API requests still succeed (fallback to database) and a warning is logged
- [x] Circuit breaker opens after 3 consecutive Redis failures — subsequent cache calls bypass Redis immediately
- [x] Circuit breaker half-opens after 30 seconds and retries Redis connection
- [x] No unhandled exceptions propagate to the caller from cache operations

## Implementation Checklist

- [x] Add `Microsoft.Extensions.Caching.Abstractions` 8.x and `Polly` 8.x NuGet packages to `UPACIP.Service.csproj`
- [x] Create `src/UPACIP.Service/Caching/ICacheService.cs` with generic methods: `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `GetOrSetAsync<T>`
- [x] Create `src/UPACIP.Service/Caching/RedisCacheService.cs` implementing `ICacheService` with `System.Text.Json` serialization, 5-minute default sliding expiration, and cache-aside pattern in `GetOrSetAsync`
- [x] Wrap all `IDistributedCache` calls in a Polly `CircuitBreakerAsync` policy configured with 3 exceptions before breaking and 30-second duration of break
- [x] Handle `RedisConnectionException`, `RedisTimeoutException`, and `BrokenCircuitException` by logging a warning and falling through to the factory/null — never propagate cache errors to callers
- [x] Register `ICacheService` as singleton `RedisCacheService` in `Program.cs`
- [x] Verify end-to-end by adding a cache-aside call in `WeatherForecastController` and confirming cache hit on repeated requests
