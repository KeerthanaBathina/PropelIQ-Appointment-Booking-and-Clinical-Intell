# Task - task_001_be_correlation_id_structured_logging

## Requirement Reference

- User Story: us_095
- Story Location: .propel/context/tasks/EP-018/us_095/us_095.md
- Acceptance Criteria:
  - AC-1: Given any HTTP request enters the system, When it is processed, Then a unique correlation ID is generated and included in all log entries, API responses, and downstream service calls for that request.
  - AC-4: Given structured logs are written, When they are stored, Then each log entry includes timestamp, level, correlation ID, user ID (if authenticated), operation name, duration, and outcome.
- Edge Case:
  - What happens when the logging service itself fails? System falls back to console logging and queues structured log entries for later flush.
  - How does the system handle correlation IDs across async operations (background jobs)? The correlation ID from the originating request is propagated to all async jobs spawned from that request.

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
| Backend | Serilog.Sinks.Seq | 8.x |
| Backend | Serilog.Sinks.Console | 6.x |
| Backend | Serilog.Sinks.File | 6.x |
| Monitoring | Seq (Community Edition) | 2024.x |

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

Implement a correlation ID middleware and structured logging pipeline that assigns a unique correlation ID to every HTTP request and enriches all log entries with standardized fields — timestamp, level, correlation ID, user ID, operation name, duration, and outcome (AC-1, AC-4, TR-028, TR-031, NFR-035). The `CorrelationIdMiddleware` generates or reads a correlation ID from the incoming `X-Correlation-ID` header, stores it in `AsyncLocal<T>` for cross-service propagation, and adds it to the response headers and Serilog `LogContext`. An `OperationLoggingMiddleware` captures request duration and outcome (success/failure/status code) for every request. The Serilog pipeline writes to three sinks: Seq (primary structured search), rolling file (backup), and console (fallback when Seq is unavailable — edge case 1). A `CorrelationIdAccessor` service provides the current correlation ID to background jobs spawned from HTTP requests, propagating the originating request's correlation ID into async operations (edge case 2).

## Dependent Tasks

- US_007 — Requires base logging configuration (Serilog setup).
- US_001 — Requires middleware pipeline for HTTP request processing.

## Impacted Components

- **NEW** `src/UPACIP.Api/Middleware/CorrelationIdMiddleware.cs` — Generates/reads X-Correlation-ID, stores in AsyncLocal, enriches LogContext
- **NEW** `src/UPACIP.Api/Middleware/OperationLoggingMiddleware.cs` — Captures request duration, operation name, outcome for each request
- **NEW** `src/UPACIP.Service/Logging/CorrelationIdAccessor.cs` — ICorrelationIdAccessor: provides current correlation ID via AsyncLocal for DI consumers
- **NEW** `src/UPACIP.Service/Logging/StructuredLogEnricher.cs` — Serilog ILogEventEnricher: adds UserID, OperationName from HttpContext
- **NEW** `src/UPACIP.Service/Logging/Models/LoggingOptions.cs` — Configuration: Seq URL, file path, fallback behavior, retention
- **NEW** `src/UPACIP.Service/Logging/FallbackLogQueue.cs` — IFallbackLogQueue: queues log entries when Seq is unavailable, flushes on reconnect
- **MODIFY** `src/UPACIP.Api/Program.cs` — Configure Serilog pipeline with Seq, file, console sinks; register middleware and services
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Serilog and Seq configuration sections

## Implementation Plan

1. **Create `LoggingOptions` configuration model**: Create in `src/UPACIP.Service/Logging/Models/LoggingOptions.cs`:
   - `string SeqServerUrl` (default: `"http://localhost:5341"`). The Seq Community Edition endpoint for structured log ingestion.
   - `string SeqApiKey` (default: `null`). Optional API key for Seq authentication.
   - `string FileLogPath` (default: `"D:\\Logs\\UPACIP\\app-.log"`). Rolling file path pattern for backup log sink.
   - `int FileRetainedDays` (default: 30). Number of days to retain file logs before auto-deletion.
   - `long FileSizeLimitBytes` (default: 104857600 — 100 MB). Max size per log file before rolling.
   - `bool EnableConsoleSink` (default: true). Whether to write to console (useful for development and fallback).
   - `int FallbackQueueCapacity` (default: 10000). Max queued log entries when Seq is unavailable.
   - `string CorrelationIdHeader` (default: `"X-Correlation-ID"`). HTTP header name for correlation ID propagation.
   Register via `IOptionsMonitor<LoggingOptions>`. Add to `appsettings.json`:
   ```json
   "Logging": {
     "SeqServerUrl": "http://localhost:5341",
     "SeqApiKey": null,
     "FileLogPath": "D:\\Logs\\UPACIP\\app-.log",
     "FileRetainedDays": 30,
     "FileSizeLimitBytes": 104857600,
     "EnableConsoleSink": true,
     "FallbackQueueCapacity": 10000,
     "CorrelationIdHeader": "X-Correlation-ID"
   }
   ```

2. **Implement `ICorrelationIdAccessor` / `CorrelationIdAccessor` (AC-1, edge case 2)**: Create in `src/UPACIP.Service/Logging/CorrelationIdAccessor.cs`:
   ```csharp
   public interface ICorrelationIdAccessor
   {
       string CorrelationId { get; }
       void SetCorrelationId(string correlationId);
   }
   ```
   Implementation uses `AsyncLocal<string>` to store the correlation ID:
   - `AsyncLocal<string>` ensures the value flows across `await` boundaries and into `Task.Run` and `BackgroundService` operations spawned from the same async context (edge case 2).
   - `CorrelationId` getter returns the `AsyncLocal` value, or generates a new GUID if unset (defensive fallback).
   - Register as `services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>()` — scoped to HTTP request lifetime.
   - For background jobs: the spawning code must capture the correlation ID before queueing and pass it to the job, which calls `SetCorrelationId()` at the start of execution.

3. **Implement `CorrelationIdMiddleware` (AC-1)**: Create in `src/UPACIP.Api/Middleware/CorrelationIdMiddleware.cs`:

   **`Task InvokeAsync(HttpContext context, ICorrelationIdAccessor accessor)`**:
   - (a) Check for incoming `X-Correlation-ID` header. If present, use it (supports upstream service propagation). If absent, generate `Guid.NewGuid().ToString("N")` (32-char lowercase hex — compact, URL-safe).
   - (b) Store in `ICorrelationIdAccessor` via `SetCorrelationId()`.
   - (c) Add to Serilog `LogContext` via `LogContext.PushProperty("CorrelationId", correlationId)` — ensures all log entries within the request scope include the correlation ID (AC-1).
   - (d) Set response header: `context.Response.Headers[options.CorrelationIdHeader] = correlationId` — enables clients to trace their requests (AC-1).
   - (e) Set `HttpContext.TraceIdentifier = correlationId` — integrates with ASP.NET Core built-in tracing.
   - (f) Call `next(context)`.

4. **Implement `StructuredLogEnricher` (AC-4)**: Create in `src/UPACIP.Service/Logging/StructuredLogEnricher.cs` implementing `Serilog.Core.ILogEventEnricher`:

   The enricher adds standard fields to every log entry from `IHttpContextAccessor`:
   - `UserId` — from `context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value` (AC-4: "user ID if authenticated"). Set to `"anonymous"` for unauthenticated requests.
   - `OperationName` — from `context.Request.Method + " " + context.Request.Path` (e.g., `"POST /api/appointments"`). AC-4: "operation name".
   - `ClientIp` — from `context.Connection.RemoteIpAddress`.
   - `UserAgent` — from `context.Request.Headers["User-Agent"]` (truncated to 200 chars).

   These fields are added via `logEvent.AddPropertyIfAbsent()`. Combined with Serilog's built-in `Timestamp` and `Level`, this satisfies AC-4's requirement for: timestamp, level, correlation ID, user ID, operation name. Duration and outcome are added by the `OperationLoggingMiddleware` (step 5).

5. **Implement `OperationLoggingMiddleware` (AC-4 — duration and outcome)**: Create in `src/UPACIP.Api/Middleware/OperationLoggingMiddleware.cs`:

   **`Task InvokeAsync(HttpContext context)`**:
   - (a) Start a `Stopwatch` before calling `next(context)`.
   - (b) After the response completes, stop the stopwatch.
   - (c) Determine outcome:
     - Status code 2xx → `"Success"`.
     - Status code 4xx → `"ClientError"`.
     - Status code 5xx → `"ServerError"`.
     - Exception caught → `"UnhandledException"`.
   - (d) Log at appropriate level:
     - 2xx: `Log.Information("HTTP_REQUEST_COMPLETED: {Method} {Path} responded {StatusCode} in {Duration}ms", method, path, statusCode, elapsed.TotalMilliseconds)`.
     - 4xx: `Log.Warning(...)`.
     - 5xx or exception: `Log.Error(...)`.
   - (e) Push `Duration` and `Outcome` properties into `LogContext` before logging, satisfying AC-4's duration and outcome fields.
   - (f) Use `try-catch-finally` to ensure the log entry is written even when exceptions occur.
   - (g) Re-throw exceptions after logging (let the global exception handler produce the response).

6. **Implement `IFallbackLogQueue` / `FallbackLogQueue` (edge case 1)**: Create in `src/UPACIP.Service/Logging/FallbackLogQueue.cs`:

   When the Seq sink fails (network error, Seq service down), Serilog's `AuditTo.Seq` would throw. Instead, use `WriteTo.Seq` (non-throwing) combined with a custom fallback:
   - Configure Serilog with `WriteTo.Seq()` (async, non-blocking — drops silently on failure) AND `WriteTo.File()` (always-on backup) AND `WriteTo.Console()` (immediate fallback).
   - The `FallbackLogQueue` monitors Seq connectivity by periodically (every 60 seconds) issuing an HTTP HEAD request to the Seq health endpoint (`{SeqServerUrl}/api`).
   - If Seq is unreachable, log a warning to console/file: `Log.Warning("LOGGING_FALLBACK: Seq unavailable, using file and console sinks")`.
   - When Seq reconnects, log: `Log.Information("LOGGING_RESTORED: Seq connection re-established")`.
   - The file sink acts as the durable backup — Seq can ingest historical file logs via Seq's import feature.
   - `FallbackQueueCapacity` limits in-memory buffering (bounded `Channel<LogEvent>` with capacity 10,000). Oldest entries dropped if capacity exceeded.

7. **Configure Serilog pipeline in `Program.cs`**: Set up the full Serilog configuration:
   ```csharp
   Log.Logger = new LoggerConfiguration()
       .MinimumLevel.Information()
       .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
       .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
       .MinimumLevel.Override("System", LogEventLevel.Warning)
       .Enrich.FromLogContext()
       .Enrich.WithMachineName()
       .Enrich.WithEnvironmentName()
       .Enrich.With<StructuredLogEnricher>()
       .WriteTo.Seq(loggingOptions.SeqServerUrl, apiKey: loggingOptions.SeqApiKey)
       .WriteTo.File(
           loggingOptions.FileLogPath,
           rollingInterval: RollingInterval.Day,
           retainedFileCountLimit: loggingOptions.FileRetainedDays,
           fileSizeLimitBytes: loggingOptions.FileSizeLimitBytes,
           outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] [{UserId}] {Message:lj}{NewLine}{Exception}")
       .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
       .CreateLogger();
   ```
   Register middleware in order:
   ```csharp
   app.UseMiddleware<CorrelationIdMiddleware>();   // First — sets correlation ID
   app.UseMiddleware<OperationLoggingMiddleware>(); // Second — wraps request timing
   ```
   Register services:
   - `services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>()`.
   - `services.AddSingleton<IFallbackLogQueue, FallbackLogQueue>()`.
   - `services.AddHttpContextAccessor()` (required by StructuredLogEnricher).

8. **Correlation ID propagation to background jobs (edge case 2)**: Define a pattern for background job correlation ID propagation:
   - When a controller action enqueues a background job (e.g., via `BackgroundService` or `IHostedService`), the calling code must capture the correlation ID:
     ```csharp
     var correlationId = _correlationIdAccessor.CorrelationId;
     _jobQueue.Enqueue(new JobPayload { CorrelationId = correlationId, ... });
     ```
   - In the `BackgroundService.ExecuteAsync`, before processing each job:
     ```csharp
     _correlationIdAccessor.SetCorrelationId(job.CorrelationId);
     using (LogContext.PushProperty("CorrelationId", job.CorrelationId))
     {
         // Process job — all logs inherit the originating request's correlation ID
     }
     ```
   - Add `string? CorrelationId` property to existing job/queue DTOs (`ImportJob`, `BackupResult`, etc.) to support propagation.
   - Document this pattern as a code comment in `CorrelationIdAccessor` for developer reference.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/
│   │   ├── Import/
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

> Assumes US_007 (logging configuration) and US_001 (middleware pipeline) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Middleware/CorrelationIdMiddleware.cs | Generates/reads X-Correlation-ID, stores in AsyncLocal, enriches LogContext |
| CREATE | src/UPACIP.Api/Middleware/OperationLoggingMiddleware.cs | Captures request duration, outcome, status code for structured logging |
| CREATE | src/UPACIP.Service/Logging/CorrelationIdAccessor.cs | ICorrelationIdAccessor: AsyncLocal-based correlation ID storage |
| CREATE | src/UPACIP.Service/Logging/StructuredLogEnricher.cs | Serilog ILogEventEnricher: adds UserId, OperationName, ClientIp |
| CREATE | src/UPACIP.Service/Logging/Models/LoggingOptions.cs | Config: Seq URL, file path, retention, fallback capacity |
| CREATE | src/UPACIP.Service/Logging/FallbackLogQueue.cs | IFallbackLogQueue: monitors Seq health, manages fallback behavior |
| MODIFY | src/UPACIP.Api/Program.cs | Configure Serilog pipeline, register middleware, register services |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Logging and Seq configuration sections |

## External References

- [Serilog — .NET Structured Logging](https://serilog.net/)
- [Serilog.Sinks.Seq — Seq Sink for Serilog](https://github.com/datalust/serilog-sinks-seq)
- [Seq Community Edition — Structured Log Server](https://datalust.co/seq)
- [Serilog LogContext — Enrichment](https://github.com/serilog/serilog/wiki/Enrichment)
- [AsyncLocal<T> — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1)
- [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)

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
- [ ] Every HTTP response includes X-Correlation-ID header (AC-1)
- [ ] Incoming X-Correlation-ID is preserved (not regenerated) for upstream tracing (AC-1)
- [ ] All log entries include CorrelationId property in Seq (AC-1)
- [ ] Log entries contain timestamp, level, correlation ID, user ID, operation name, duration, outcome (AC-4)
- [ ] Unauthenticated requests log UserId as "anonymous" (AC-4)
- [ ] File sink writes logs when Seq is unavailable (edge case 1)
- [ ] Console sink provides immediate fallback output (edge case 1)
- [ ] Background job logs include the originating request's correlation ID (edge case 2)
- [ ] Serilog minimum level overrides suppress noisy Microsoft.AspNetCore logs

## Implementation Checklist

- [ ] Create LoggingOptions configuration with Seq URL, file path, and fallback settings
- [ ] Implement ICorrelationIdAccessor with AsyncLocal storage for cross-async propagation
- [ ] Implement CorrelationIdMiddleware generating/reading X-Correlation-ID header
- [ ] Implement StructuredLogEnricher adding UserId, OperationName, ClientIp to all entries
- [ ] Implement OperationLoggingMiddleware capturing duration and outcome per request
- [ ] Implement FallbackLogQueue monitoring Seq health with console/file fallback
- [ ] Configure Serilog pipeline with Seq, file, and console sinks in Program.cs
- [ ] Document correlation ID propagation pattern for background jobs
