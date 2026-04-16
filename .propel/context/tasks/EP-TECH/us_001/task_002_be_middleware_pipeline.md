# Task - task_002_be_middleware_pipeline

## Requirement Reference

- User Story: us_001
- Story Location: .propel/context/tasks/EP-TECH/us_001/us_001.md
- Acceptance Criteria:
  - AC-4: Given the API is started, When an HTTP request is sent to any endpoint, Then the response includes proper CORS headers allowing the React frontend origin.
  - AC-5: Given the project is scaffolded, When a developer reviews the middleware pipeline, Then error handling middleware returns structured JSON error responses with correlation IDs.
- Edge Case:
  - How does the system handle port conflicts on startup? Configuration supports port override via appsettings.json or environment variables.

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
| API Framework | ASP.NET Core MVC | 8.x |

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

Configure the ASP.NET Core middleware pipeline with CORS policy for the React 18 frontend origin, a global exception handling middleware that returns structured JSON error responses, and a correlation ID middleware that generates and propagates unique request trace identifiers. Additionally, configure environment-specific settings for port overrides to handle startup port conflicts.

## Dependent Tasks

- task_001_be_solution_scaffold — Solution structure and Program.cs must exist before middleware can be registered.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/Program.cs` — Register CORS policy, error handling middleware, and correlation ID middleware in the pipeline
- **NEW** `src/UPACIP.Api/Middleware/GlobalExceptionHandlerMiddleware.cs` — Global exception handler returning structured JSON error responses
- **NEW** `src/UPACIP.Api/Middleware/CorrelationIdMiddleware.cs` — Generates/propagates correlation IDs on each request
- **NEW** `src/UPACIP.Api/Models/ErrorResponse.cs` — Structured JSON error response model with correlation ID field
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add CORS allowed origins and Kestrel port override configuration

## Implementation Plan

1. **Define CORS policy**: In `Program.cs`, configure a named CORS policy using `AddCors` that allows the React frontend origin (`http://localhost:3000` for development). The policy must allow any header and any method for the configured origin. Use environment-specific configuration so production origin can differ.
2. **Create error response model**: Define `ErrorResponse` class with properties: `StatusCode` (int), `Message` (string), `CorrelationId` (string), `Timestamp` (DateTimeOffset). This ensures all error responses follow a consistent JSON structure.
3. **Implement global exception handler middleware**: Create `GlobalExceptionHandlerMiddleware` that wraps the entire pipeline in a try-catch. On unhandled exceptions, log the error with Serilog-compatible structured logging, and return a JSON `ErrorResponse` with the appropriate HTTP status code and the current correlation ID. Ensure no stack traces or internal details leak to the client.
4. **Implement correlation ID middleware**: Create `CorrelationIdMiddleware` that checks for an incoming `X-Correlation-ID` header. If present, use it; otherwise, generate a new GUID. Store the correlation ID in `HttpContext.Items` and add it to the response headers. This enables distributed tracing across services (TR-028).
5. **Register middleware pipeline**: In `Program.cs`, register middleware in correct order: CorrelationId → ExceptionHandler → CORS → HTTPS Redirection → Routing → Authorization → Endpoints. Middleware order is critical for correct behavior.
6. **Configure port overrides**: Update `appsettings.json` and `appsettings.Development.json` to support Kestrel URL configuration via `Kestrel:Endpoints:Http:Url` and allow override via `ASPNETCORE_URLS` environment variable.
7. **Validate middleware pipeline**: Test that CORS headers appear on cross-origin requests, error responses return structured JSON with correlation IDs, and port conflicts are resolvable via configuration.

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
│   │   └── Controllers/
│   │       └── WeatherForecastController.cs
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       └── UPACIP.DataAccess.csproj
└── scripts/
    └── check-sdk.ps1
```

> Assumes task_001_be_solution_scaffold is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Middleware/GlobalExceptionHandlerMiddleware.cs | Global exception handler catching unhandled exceptions and returning structured JSON error responses |
| CREATE | src/UPACIP.Api/Middleware/CorrelationIdMiddleware.cs | Middleware generating/propagating X-Correlation-ID header on each request |
| CREATE | src/UPACIP.Api/Models/ErrorResponse.cs | Structured error response model (StatusCode, Message, CorrelationId, Timestamp) |
| MODIFY | src/UPACIP.Api/Program.cs | Add CORS policy registration, middleware pipeline ordering (CorrelationId → ExceptionHandler → CORS → HTTPS → Routing → Auth → Endpoints) |
| MODIFY | src/UPACIP.Api/appsettings.json | Add CorsSettings:AllowedOrigins array and Kestrel endpoint URL configuration |
| MODIFY | src/UPACIP.Api/appsettings.Development.json | Add development-specific CORS origin (http://localhost:3000) and port configuration |

## External References

- [ASP.NET Core CORS documentation](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-8.0)
- [ASP.NET Core middleware pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-8.0)
- [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0)

## Build Commands

```powershell
# Build solution
dotnet build UPACIP.sln --configuration Debug

# Run API and test CORS headers
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test CORS with curl (from different origin)
curl -i -H "Origin: http://localhost:3000" https://localhost:{port}/swagger

# Test error handling (hit a non-existent endpoint or trigger exception)
curl -i https://localhost:{port}/api/nonexistent

# Override port via environment variable
$env:ASPNETCORE_URLS="https://localhost:5010"; dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] Cross-origin request from `http://localhost:3000` receives `Access-Control-Allow-Origin` header in response
- [ ] CORS preflight (OPTIONS) request returns correct CORS headers
- [ ] Unhandled exception returns HTTP 500 with structured JSON body containing `statusCode`, `message`, `correlationId`, and `timestamp` fields
- [ ] Response JSON on error does not contain stack traces or internal implementation details
- [ ] Every HTTP response includes `X-Correlation-ID` header with a valid GUID
- [ ] Incoming requests with `X-Correlation-ID` header preserve the provided value in response
- [ ] Application starts on custom port when configured via `appsettings.json` or `ASPNETCORE_URLS` environment variable
- [ ] `dotnet build` completes with zero errors and zero warnings after all changes

## Implementation Checklist

- [ ] Configure named CORS policy in `Program.cs` using `AddCors` with allowed origins loaded from `appsettings.json` (CorsSettings:AllowedOrigins)
- [ ] Create `Models/ErrorResponse.cs` with properties: `StatusCode`, `Message`, `CorrelationId`, `Timestamp`
- [ ] Create `Middleware/GlobalExceptionHandlerMiddleware.cs` that catches unhandled exceptions, logs the error, and returns a serialized `ErrorResponse` with the correlation ID from `HttpContext.Items`
- [ ] Create `Middleware/CorrelationIdMiddleware.cs` that reads or generates `X-Correlation-ID`, stores it in `HttpContext.Items`, and adds it to the response headers
- [ ] Register middleware in `Program.cs` in correct order: `UseCorrelationId` → `UseGlobalExceptionHandler` → `UseCors` → `UseHttpsRedirection` → `UseRouting` → `UseAuthorization` → `MapControllers`
- [ ] Add `CorsSettings:AllowedOrigins` and `Kestrel:Endpoints` configuration sections to `appsettings.json` and `appsettings.Development.json`
- [ ] Verify `dotnet build` succeeds with zero errors and zero warnings after all middleware additions
