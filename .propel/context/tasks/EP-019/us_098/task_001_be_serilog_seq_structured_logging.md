# Task - task_001_be_serilog_seq_structured_logging

## Requirement Reference

- User Story: us_098
- Story Location: .propel/context/tasks/EP-019/us_098/us_098.md
- Acceptance Criteria:
  - AC-1: Given the logging pipeline is configured, When the application runs, Then Serilog writes structured JSON logs to Seq Community Edition with configurable log levels.
- Edge Case:
  - What happens when Seq is temporarily unavailable? Serilog uses a durable file buffer and flushes to Seq when connectivity restores.

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
| Backend | Serilog.AspNetCore | 8.x |
| Backend | Serilog.Sinks.Seq | 7.x |
| Backend | Serilog.Sinks.File | 5.x |
| Backend | Serilog.Sinks.Console | 5.x |
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

Configure the Serilog structured logging pipeline with Seq Community Edition as the primary log aggregation sink, satisfying AC-1, TR-031, NFR-035, and edge case 1. The implementation configures Serilog via `appsettings.json` with three sinks — Seq (primary, structured JSON ingestion), rolling file (durable buffer for Seq outages), and console (development diagnostics). Log levels are configurable per namespace via `appsettings.json` overrides (e.g., `Microsoft.AspNetCore` at Warning, `UPACIP.Service` at Information). The Seq sink uses `Serilog.Sinks.Seq` with `DurableHttpUsingFileSizeRolledBuffers` for durable buffering — when Seq is temporarily unavailable, logs are written to a local file buffer and automatically flushed to Seq when connectivity restores (edge case 1). Serilog replaces the default ASP.NET Core logging provider via `UseSerilog()` in `Program.cs`.

## Dependent Tasks

- US_001 — Requires backend project scaffold with `Program.cs`.
- US_007 — Requires baseline logging infrastructure.
- US_095 task_001 — Coordinates with correlation ID middleware (CorrelationIdMiddleware enriches LogContext).

## Impacted Components

- **NEW** `src/UPACIP.Api/Logging/SerilogConfiguration.cs` — Extension method for Serilog pipeline setup
- **MODIFY** `src/UPACIP.Api/Program.cs` — Replace default logging with `UseSerilog()`, call configuration extension
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Serilog configuration section with sinks and log level overrides
- **MODIFY** `src/UPACIP.Api/appsettings.Development.json` — Development-specific overrides (verbose console, no Seq)
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add Serilog NuGet packages

## Implementation Plan

1. **Add Serilog NuGet packages to `UPACIP.Api.csproj`**: Add the following package references:
   ```xml
   <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
   <PackageReference Include="Serilog.Sinks.Seq" Version="7.*" />
   <PackageReference Include="Serilog.Sinks.File" Version="5.*" />
   <PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
   <PackageReference Include="Serilog.Enrichers.Environment" Version="2.*" />
   <PackageReference Include="Serilog.Enrichers.Thread" Version="3.*" />
   <PackageReference Include="Serilog.Expressions" Version="4.*" />
   ```
   Packages:
   - `Serilog.AspNetCore` — integration with ASP.NET Core host, replaces default `ILogger<T>` provider.
   - `Serilog.Sinks.Seq` — sends structured events to Seq over HTTP. Includes built-in durable buffering via `bufferBaseFilename` (edge case 1).
   - `Serilog.Sinks.File` — rolling file sink for persistent local log storage.
   - `Serilog.Sinks.Console` — console output for development.
   - `Serilog.Enrichers.Environment` — adds MachineName, EnvironmentName properties.
   - `Serilog.Enrichers.Thread` — adds ThreadId property.
   - `Serilog.Expressions` — template-based output formatting for console/file sinks.

2. **Configure Serilog in `appsettings.json` (AC-1)**: Add the Serilog configuration section:
   ```json
   {
     "Serilog": {
       "Using": [
         "Serilog.Sinks.Console",
         "Serilog.Sinks.File",
         "Serilog.Sinks.Seq",
         "Serilog.Enrichers.Environment",
         "Serilog.Enrichers.Thread"
       ],
       "MinimumLevel": {
         "Default": "Information",
         "Override": {
           "Microsoft.AspNetCore": "Warning",
           "Microsoft.EntityFrameworkCore": "Warning",
           "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
           "System.Net.Http.HttpClient": "Warning",
           "UPACIP.Service": "Information",
           "UPACIP.Api": "Information",
           "UPACIP.DataAccess": "Warning"
         }
       },
       "WriteTo": [
         {
           "Name": "Console",
           "Args": {
             "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"
           }
         },
         {
           "Name": "File",
           "Args": {
             "path": "logs/upacip-.log",
             "rollingInterval": "Day",
             "retainedFileCountLimit": 30,
             "fileSizeLimitBytes": 104857600,
             "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {UserId} {OperationName} {Message:lj}{NewLine}{Exception}"
           }
         },
         {
           "Name": "Seq",
           "Args": {
             "serverUrl": "http://localhost:5341",
             "apiKey": "",
             "bufferBaseFilename": "logs/seq-buffer",
             "bufferSizeLimitBytes": 104857600,
             "eventBodyLimitBytes": 262144,
             "batchPostingLimit": 1000,
             "period": "00:00:02",
             "retainedInvalidPayloadsLimitBytes": 0
           }
         }
       ],
       "Enrich": [
         "FromLogContext",
         "WithMachineName",
         "WithThreadId",
         "WithEnvironmentName"
       ],
       "Properties": {
         "Application": "UPACIP",
         "Environment": "Production"
       }
     }
   }
   ```
   Key configuration:
   - **Configurable log levels (AC-1)**: `MinimumLevel.Override` controls verbosity per namespace. Developers can change levels at runtime via `appsettings.json` reload (IOptionsMonitor).
   - **Seq sink with durable buffer (edge case 1)**: `bufferBaseFilename` enables the durable HTTP sink mode. When Seq is unreachable, events are written to `logs/seq-buffer-*.json` files. When Seq becomes available, buffered events are automatically flushed in chronological order. `bufferSizeLimitBytes` caps the local buffer at 100MB to prevent disk exhaustion.
   - **Rolling file sink**: Daily log files retained for 30 days, 100MB size limit per file. Provides persistent logs independent of Seq availability.
   - **Console sink**: Compact template for development/debugging with correlation ID.
   - **Enrichment**: `FromLogContext` captures properties pushed by middleware (CorrelationId, UserId from US_095 task_001). `WithMachineName` and `WithEnvironmentName` support multi-server log correlation.

3. **Create `SerilogConfiguration` extension method**: Create in `src/UPACIP.Api/Logging/SerilogConfiguration.cs`:
   ```csharp
   public static class SerilogConfiguration
   {
       public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
       {
           builder.Host.UseSerilog((context, services, configuration) =>
           {
               configuration.ReadFrom.Configuration(context.Configuration);
               configuration.ReadFrom.Services(services);
           });

           return builder;
       }

       public static WebApplication UseSerilogRequestLogging(this WebApplication app)
       {
           app.UseSerilogRequestLogging(options =>
           {
               options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
               {
                   diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                   diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                   diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
                   diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
               };
               options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
           });

           return app;
       }
   }
   ```
   - `AddSerilogLogging` — configures Serilog from `appsettings.json` and replaces the default `ILoggerFactory`.
   - `UseSerilogRequestLogging` — adds HTTP request logging middleware that captures method, path, status code, elapsed time, and enriches with host, scheme, user-agent, and client IP. This replaces the verbose default ASP.NET Core request logging with a single structured log event per request.

4. **Integrate Serilog in `Program.cs`**: Modify `Program.cs` to use Serilog as the logging provider:
   ```csharp
   // Early initialization for startup logging
   Log.Logger = new LoggerConfiguration()
       .WriteTo.Console()
       .CreateBootstrapLogger();

   try
   {
       Log.Information("UPACIP application starting...");

       var builder = WebApplication.CreateBuilder(args);
       builder.AddSerilogLogging();

       // ... existing service registrations ...

       var app = builder.Build();
       app.UseSerilogRequestLogging();

       // ... existing middleware pipeline ...

       app.Run();
   }
   catch (Exception ex)
   {
       Log.Fatal(ex, "Application terminated unexpectedly");
   }
   finally
   {
       Log.CloseAndFlush();
   }
   ```
   Key integration points:
   - **Bootstrap logger**: A minimal console logger captures startup errors before the full pipeline is configured.
   - **`Log.CloseAndFlush()`**: Ensures all buffered log events (including Seq durable buffer) are flushed on application shutdown.
   - **`try/catch/finally`**: Fatal startup exceptions are logged before the process exits.

5. **Configure development-specific overrides in `appsettings.Development.json`**: Add development overrides:
   ```json
   {
     "Serilog": {
       "MinimumLevel": {
         "Default": "Debug",
         "Override": {
           "Microsoft.AspNetCore": "Information",
           "Microsoft.EntityFrameworkCore.Database.Command": "Information",
           "UPACIP.Service": "Debug",
           "UPACIP.Api": "Debug"
         }
       },
       "Properties": {
         "Environment": "Development"
       }
     }
   }
   ```
   Development mode enables Debug-level logging for UPACIP namespaces and shows EF Core SQL commands for query optimization during development.

6. **Add structured log context helpers**: Create utility methods in `SerilogConfiguration.cs` for common log enrichment patterns used across the application:
   ```csharp
   public static class LogContextExtensions
   {
       public static IDisposable PushOperationContext(string operationName, Guid? entityId = null)
       {
           var stack = new List<IDisposable>
           {
               LogContext.PushProperty("OperationName", operationName)
           };
           if (entityId.HasValue)
               stack.Add(LogContext.PushProperty("EntityId", entityId.Value));

           return new CompositeDisposable(stack);
       }
   }

   internal sealed class CompositeDisposable : IDisposable
   {
       private readonly List<IDisposable> _disposables;
       public CompositeDisposable(List<IDisposable> disposables) => _disposables = disposables;
       public void Dispose()
       {
           foreach (var d in _disposables) d.Dispose();
       }
   }
   ```
   Usage pattern in service methods:
   ```csharp
   using (LogContextExtensions.PushOperationContext("PatientExport", patientId))
   {
       _logger.LogInformation("Starting patient data export");
       // ... operation ...
   }
   ```

7. **Verify Seq Community Edition connectivity**: Document the Seq setup requirements:
   - Seq Community Edition runs at `http://localhost:5341` (default).
   - No API key required for Community Edition (single-user mode).
   - Verify connectivity: `GET http://localhost:5341/api` should return Seq version info.
   - If Seq is not running, the durable buffer captures events to `logs/seq-buffer-*.json` and flushes when Seq becomes available (edge case 1).
   - Production deployment: configure `serverUrl` and `apiKey` in environment-specific `appsettings.Production.json`.

8. **Configure log retention and performance**: Set Serilog performance parameters:
   - **File sink**: 30-day retention, 100MB max per file, daily rolling. Total disk usage capped at ~3GB.
   - **Seq buffer**: 100MB buffer limit. If Seq is offline for extended periods and buffer fills, oldest events are dropped (configured via `bufferSizeLimitBytes`).
   - **Batch posting**: Seq receives logs in batches of up to 1000 events every 2 seconds, reducing HTTP overhead.
   - **Event body limit**: Individual events capped at 256KB to prevent oversized payloads from exceptions with deep stack traces.
   - **Async I/O**: Serilog's Seq sink uses async HTTP posting — log calls in application code never block on Seq I/O.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   ├── CorrelationIdMiddleware.cs            ← from US_095 task_001
│   │   │   └── OperationLoggingMiddleware.cs         ← from US_095 task_001
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   │   ├── Logging/
│   │   │   └── CorrelationIdAccessor.cs              ← from US_095 task_001
│   │   └── ...
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (project scaffold), US_007 (logging baseline), and US_095 task_001 (correlation ID middleware) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Logging/SerilogConfiguration.cs | Extension methods: AddSerilogLogging, UseSerilogRequestLogging, LogContextExtensions |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add Serilog NuGet packages (AspNetCore, Seq, File, Console, Enrichers) |
| MODIFY | src/UPACIP.Api/Program.cs | Replace default logging with UseSerilog(), bootstrap logger, CloseAndFlush |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Serilog config: sinks, levels, enrichers, Seq durable buffer |
| MODIFY | src/UPACIP.Api/appsettings.Development.json | Development overrides: Debug level, EF Core SQL logging |

## External References

- [Serilog — ASP.NET Core Integration](https://github.com/serilog/serilog-aspnetcore)
- [Serilog.Sinks.Seq — Durable Logging](https://github.com/serilog/serilog-sinks-seq)
- [Seq Community Edition — Download](https://datalust.co/seq)
- [Serilog — Configuration from appsettings.json](https://github.com/serilog/serilog-settings-configuration)
- [Structured Logging — Best Practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging)

## Build Commands

```powershell
# Restore and build API project
dotnet restore src/UPACIP.Api/UPACIP.Api.csproj
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Verify Seq connectivity
Invoke-RestMethod -Uri http://localhost:5341/api -Method Get
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding Serilog packages
- [ ] Application startup writes "UPACIP application starting..." to console (bootstrap logger)
- [ ] Serilog writes structured JSON logs to Seq at http://localhost:5341 (AC-1)
- [ ] Log levels are configurable per namespace via appsettings.json MinimumLevel.Override (AC-1)
- [ ] Changing log level in appsettings.json takes effect without restart (IOptionsMonitor reload)
- [ ] Rolling file logs appear in logs/upacip-{date}.log with 30-day retention
- [ ] When Seq is stopped, logs buffer to logs/seq-buffer-*.json (edge case 1)
- [ ] When Seq restarts, buffered events flush to Seq automatically (edge case 1)
- [ ] HTTP request logging shows method, path, status code, elapsed time per request
- [ ] Log.CloseAndFlush() on shutdown flushes all pending events

## Implementation Checklist

- [ ] Add Serilog NuGet packages to UPACIP.Api.csproj
- [ ] Configure Serilog section in appsettings.json with Seq, File, Console sinks
- [ ] Configure Seq durable buffer with bufferBaseFilename for outage resilience
- [ ] Create SerilogConfiguration extension with AddSerilogLogging and UseSerilogRequestLogging
- [ ] Integrate UseSerilog() in Program.cs with bootstrap logger and CloseAndFlush
- [ ] Add development-specific log level overrides in appsettings.Development.json
- [ ] Create LogContextExtensions for structured operation context enrichment
- [ ] Configure log retention (30 days file, 100MB Seq buffer)
