# Task - task_002_be_retry_policies_exponential_backoff

## Requirement Reference

- User Story: us_095
- Story Location: .propel/context/tasks/EP-018/us_095/us_095.md
- Acceptance Criteria:
  - AC-2: Given a transient failure occurs (database timeout, external API error), When the failure is detected, Then the system retries up to 3 times with exponential backoff (1s, 5s, 15s) before returning an error.

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
| Backend | Polly | 8.x |
| Backend | Entity Framework Core | 8.x |
| Backend | Serilog | 8.x |
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

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement centralized transient fault handling using Polly 8.x resilience pipelines that provide retry logic with exponential backoff (1s, 5s, 15s — max 3 retries) for database timeouts, external API errors, and transient HTTP failures (AC-2, NFR-032, NFR-023). The implementation defines reusable Polly resilience pipelines — `DatabaseRetryPipeline`, `HttpRetryPipeline`, and `ExternalServiceRetryPipeline` — registered as named pipelines in the .NET 8 `IResiliencePipelineProvider`. Each pipeline classifies transient vs. permanent failures (only transient failures trigger retries), logs every retry attempt with the correlation ID and attempt number via Serilog, and wraps the final failure in a structured error response with the correlation ID for client troubleshooting. Circuit breaker policies are layered on top of retry for external services (NFR-023) — opening after 5 consecutive failures and half-opening after 30 seconds.

## Dependent Tasks

- US_007 — Requires base logging configuration.
- US_001 — Requires middleware pipeline.
- US_095 task_001 — Requires ICorrelationIdAccessor for correlation ID in retry logs.

## Impacted Components

- **NEW** `src/UPACIP.Service/Resilience/ResiliencePipelineConfigurator.cs` — Configures and registers named Polly pipelines
- **NEW** `src/UPACIP.Service/Resilience/TransientFaultClassifier.cs` — ITransientFaultClassifier: determines if an exception is transient
- **NEW** `src/UPACIP.Service/Resilience/Models/ResilienceOptions.cs` — Configuration: retry delays, circuit breaker thresholds
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register resilience pipelines via AddResiliencePipeline, configure HttpClient with Polly
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Resilience configuration section
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Configure EF Core execution strategy with retry

## Implementation Plan

1. **Create `ResilienceOptions` configuration model**: Create in `src/UPACIP.Service/Resilience/Models/ResilienceOptions.cs`:
   - `int MaxRetries` (default: 3). Maximum retry attempts per NFR-032.
   - `double[] RetryDelaysSeconds` (default: `[1.0, 5.0, 15.0]`). Exponential backoff delays matching AC-2 specification exactly (1s, 5s, 15s).
   - `int CircuitBreakerFailureThreshold` (default: 5). Consecutive failures before circuit opens (NFR-023).
   - `int CircuitBreakerBreakDurationSeconds` (default: 30). Duration the circuit stays open before half-open.
   - `int CircuitBreakerSamplingDurationSeconds` (default: 60). Time window for failure counting.
   - `int HttpTimeoutSeconds` (default: 30). Timeout for external HTTP calls.
   - `bool EnableRetryLogging` (default: true). Whether to log each retry attempt.
   Register via `IOptionsMonitor<ResilienceOptions>`. Add to `appsettings.json`:
   ```json
   "Resilience": {
     "MaxRetries": 3,
     "RetryDelaysSeconds": [1.0, 5.0, 15.0],
     "CircuitBreakerFailureThreshold": 5,
     "CircuitBreakerBreakDurationSeconds": 30,
     "CircuitBreakerSamplingDurationSeconds": 60,
     "HttpTimeoutSeconds": 30,
     "EnableRetryLogging": true
   }
   ```

2. **Implement `ITransientFaultClassifier` / `TransientFaultClassifier`**: Create in `src/UPACIP.Service/Resilience/TransientFaultClassifier.cs`. This service determines whether an exception represents a transient (retryable) or permanent (non-retryable) failure:

   **Database transient exceptions** — `bool IsTransientDatabaseException(Exception ex)`:
   - `Npgsql.NpgsqlException` with `IsTransient == true` (Npgsql's built-in transient detection).
   - `TimeoutException` — query execution timeout.
   - `Npgsql.PostgresException` with SqlState codes: `"57014"` (query cancelled), `"40001"` (serialization failure), `"40P01"` (deadlock detected), `"08006"` (connection failure), `"08001"` (unable to connect).
   - NOT transient: `"23505"` (unique violation), `"23503"` (FK violation), `"42P01"` (undefined table) — these are permanent failures.

   **HTTP transient exceptions** — `bool IsTransientHttpException(Exception ex)`:
   - `HttpRequestException` with status codes: 408 (Request Timeout), 429 (Too Many Requests), 500 (Internal Server Error), 502 (Bad Gateway), 503 (Service Unavailable), 504 (Gateway Timeout).
   - `TaskCanceledException` with inner `TimeoutException` — HTTP client timeout.
   - NOT transient: 400 (Bad Request), 401 (Unauthorized), 403 (Forbidden), 404 (Not Found) — these are permanent.

   **General transient exceptions** — `bool IsTransient(Exception ex)`:
   - `IOException` — network I/O error.
   - `SocketException` — connection reset.
   - Delegates to database or HTTP classifiers based on exception type.

3. **Implement `ResiliencePipelineConfigurator`**: Create in `src/UPACIP.Service/Resilience/ResiliencePipelineConfigurator.cs`. This static class configures three named Polly 8.x resilience pipelines:

   **`DatabaseRetryPipeline`** — For EF Core and raw database operations:
   - Retry: 3 attempts with delays [1s, 5s, 15s] (AC-2).
   - Handle: exceptions where `TransientFaultClassifier.IsTransientDatabaseException()` returns true.
   - `OnRetry` callback: Log `Log.Warning("DB_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={Delay}s, Exception={ExceptionType}, CorrelationId={CorrelationId}")`.
   - No circuit breaker (database is local, connection pooling handles recovery).

   **`HttpRetryPipeline`** — For external HTTP calls (SMTP, Twilio, OpenAI):
   - Retry: 3 attempts with delays [1s, 5s, 15s] (AC-2).
   - Handle: exceptions where `TransientFaultClassifier.IsTransientHttpException()` returns true, or `HttpResponseMessage` with transient status codes.
   - Circuit Breaker layered on top: open after 5 consecutive failures, break for 30 seconds (NFR-023).
   - `OnRetry` callback: Log `Log.Warning("HTTP_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={Delay}s, StatusCode={StatusCode}, Url={Url}, CorrelationId={CorrelationId}")`.
   - Timeout: 30 seconds per attempt.

   **`ExternalServiceRetryPipeline`** — For non-HTTP external integrations (file system, SMTP direct):
   - Retry: 3 attempts with delays [1s, 5s, 15s] (AC-2).
   - Handle: `IOException`, `SocketException`, `TimeoutException`.
   - Circuit Breaker: same as HttpRetryPipeline (NFR-023).
   - `OnRetry` callback: Log `Log.Warning("SERVICE_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={Delay}s, Service={ServiceName}, CorrelationId={CorrelationId}")`.

4. **Register resilience pipelines in Program.cs**: Use .NET 8's `Microsoft.Extensions.Resilience` integration:
   ```csharp
   builder.Services.AddResiliencePipeline("database-retry", (builder, context) =>
   {
       var options = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
       builder.AddRetry(new RetryStrategyOptions
       {
           MaxRetryAttempts = options.MaxRetries,
           DelayGenerator = args => new ValueTask<TimeSpan?>(
               TimeSpan.FromSeconds(options.RetryDelaysSeconds[args.AttemptNumber])),
           ShouldHandle = new PredicateBuilder().Handle<Exception>(
               ex => classifier.IsTransientDatabaseException(ex))
       });
   });
   ```
   Register `HttpRetryPipeline` and `ExternalServiceRetryPipeline` similarly with added circuit breaker.
   Register `services.AddSingleton<ITransientFaultClassifier, TransientFaultClassifier>()`.

5. **Configure EF Core execution strategy**: Modify `ApplicationDbContext` or the Npgsql registration in `Program.cs` to use Npgsql's built-in retry execution strategy:
   ```csharp
   builder.Services.AddDbContext<ApplicationDbContext>(options =>
       options.UseNpgsql(connectionString, npgsqlOptions =>
           npgsqlOptions.EnableRetryOnFailure(
               maxRetryCount: 3,
               maxRetryDelay: TimeSpan.FromSeconds(15),
               errorCodesToAdd: new[] { "57014", "40001", "40P01" })));
   ```
   This provides EF Core-native retry for transient database errors, complementing the Polly pipeline for manual database operations.

6. **Configure `HttpClient` with Polly pipeline**: For `IHttpClientFactory` registrations (OpenAI, Twilio, SendGrid), apply the `HttpRetryPipeline`:
   ```csharp
   builder.Services.AddHttpClient("OpenAI")
       .AddResilienceHandler("http-retry", (builder, context) => { ... });
   ```
   This ensures all outbound HTTP calls from named clients automatically retry transient failures with the configured backoff (AC-2).

7. **Structured error response on final failure**: When all retries are exhausted, the exception propagates to the global exception handler. Ensure the error response includes the correlation ID for client troubleshooting:
   - The existing `OperationLoggingMiddleware` (task_001) logs the final failure with correlation ID.
   - The API `ProblemDetails` response includes `correlationId` in the extensions:
     ```json
     {
       "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
       "title": "Service Unavailable",
       "status": 503,
       "detail": "The request failed after 3 retry attempts.",
       "extensions": { "correlationId": "abc123..." }
     }
     ```
   - Create or modify a global exception handler middleware to include correlation ID in `ProblemDetails` when all retries fail.

8. **Retry telemetry and monitoring**: Add structured log properties for retry monitoring in Seq:
   - `RETRY_EXHAUSTED` — logged when all 3 retries fail: `Log.Error("RETRY_EXHAUSTED: Pipeline={PipelineName}, Attempts={Attempts}, FinalException={ExceptionType}, CorrelationId={CorrelationId}")`.
   - `CIRCUIT_BREAKER_OPENED` — logged when circuit opens: `Log.Warning("CIRCUIT_BREAKER_OPENED: Service={ServiceName}, FailureCount={Count}")`.
   - `CIRCUIT_BREAKER_HALF_OPEN` — logged when circuit transitions to half-open for probe.
   - `CIRCUIT_BREAKER_CLOSED` — logged when circuit closes (service recovered).
   - These events enable Seq dashboards and alerts for transient failure trends.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   ├── CorrelationIdMiddleware.cs            ← from task_001
│   │   │   └── OperationLoggingMiddleware.cs         ← from task_001
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/
│   │   ├── Import/
│   │   ├── Logging/
│   │   │   ├── CorrelationIdAccessor.cs              ← from task_001
│   │   │   ├── StructuredLogEnricher.cs              ← from task_001
│   │   │   ├── FallbackLogQueue.cs                   ← from task_001
│   │   │   └── Models/
│   │   │       └── LoggingOptions.cs                 ← from task_001
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   └── PatientRights/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_095 task_001 (correlation ID and structured logging) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Resilience/ResiliencePipelineConfigurator.cs | Configures Database, HTTP, ExternalService retry pipelines with circuit breaker |
| CREATE | src/UPACIP.Service/Resilience/TransientFaultClassifier.cs | ITransientFaultClassifier: classifies exceptions as transient vs permanent |
| CREATE | src/UPACIP.Service/Resilience/Models/ResilienceOptions.cs | Config: retry delays, circuit breaker thresholds, timeout |
| MODIFY | src/UPACIP.Api/Program.cs | Register resilience pipelines, configure HttpClient with Polly, EF Core retry |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Resilience configuration section |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Configure Npgsql EnableRetryOnFailure execution strategy |

## External References

- [Polly 8.x — .NET Resilience Library](https://github.com/App-vNext/Polly)
- [Microsoft.Extensions.Resilience — .NET 8 Integration](https://learn.microsoft.com/en-us/dotnet/core/resilience)
- [Npgsql EnableRetryOnFailure — EF Core](https://www.npgsql.org/efcore/misc/other.html#execution-strategy)
- [PostgreSQL Error Codes (SqlState)](https://www.postgresql.org/docs/16/errcodes-appendix.html)
- [Circuit Breaker Pattern — Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [HttpClient Resilience — .NET 8](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Database timeout triggers retry with 1s, 5s, 15s delays (AC-2)
- [ ] HTTP 503 response triggers retry with 1s, 5s, 15s delays (AC-2)
- [ ] Permanent errors (400, 404, unique violations) do NOT trigger retry
- [ ] Each retry attempt is logged with attempt number, delay, and correlation ID
- [ ] RETRY_EXHAUSTED is logged when all 3 attempts fail
- [ ] Circuit breaker opens after 5 consecutive HTTP failures (NFR-023)
- [ ] Circuit breaker half-opens after 30 seconds for probe request
- [ ] ProblemDetails error response includes correlation ID after final failure
- [ ] EF Core EnableRetryOnFailure configured with PostgreSQL transient error codes

## Implementation Checklist

- [ ] Create ResilienceOptions configuration with retry delays and circuit breaker thresholds
- [ ] Implement ITransientFaultClassifier for database, HTTP, and general exception classification
- [ ] Configure DatabaseRetryPipeline with 3 retries and exponential backoff
- [ ] Configure HttpRetryPipeline with retry + circuit breaker for external services
- [ ] Configure ExternalServiceRetryPipeline with retry + circuit breaker
- [ ] Configure EF Core Npgsql EnableRetryOnFailure with transient error codes
- [ ] Apply Polly pipeline to IHttpClientFactory named clients
- [ ] Add retry telemetry logging (RETRY_EXHAUSTED, CIRCUIT_BREAKER_OPENED/CLOSED)
