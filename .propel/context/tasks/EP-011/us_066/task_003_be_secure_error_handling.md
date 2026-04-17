# Task - task_003_be_secure_error_handling

## Requirement Reference

- User Story: us_066
- Story Location: .propel/context/tasks/EP-011/us_066/us_066.md
- Acceptance Criteria:
    - AC-3: **Given** an error occurs, **When** the error message is returned to the client, **Then** it contains no PII, stack traces, or internal system details — only a correlation ID and user-friendly message.
    - AC-4: **Given** a validation rule fails, **When** the response is generated, **Then** the error message specifies which field failed and the expected format without exposing internal logic.
- Requirement Tags: NFR-017, NFR-018
- Edge Case:
    - EC-1 (partial): Errors originating from special-character data (e.g., "O'Brien") must not leak raw exception details including the input data in the response.

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
| Logging | Serilog + Seq (Community Edition) | 8.x / 2024.x |
| Testing | xUnit + Moq | 2.x / 4.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

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

Implement a global exception handling middleware and correlation ID infrastructure for the ASP.NET Core Web API. This task ensures that all unhandled exceptions are caught at the middleware level, logged server-side with full diagnostic context (leveraging PII redaction from task_002), and returned to the client as sanitized error responses containing only a correlation ID and a user-friendly message. No PII, stack traces, database connection strings, or internal system details are ever exposed in HTTP responses. The correlation ID (TR-028) is generated per-request and propagated through headers, log context, and error responses for distributed tracing via Seq.

## Dependent Tasks

- US_001 — Foundational — Requires backend middleware pipeline to be established
- task_002_be_pii_redaction_logging — PII redaction must be active in Serilog pipeline so server-side exception logging automatically redacts PII

## Impacted Components

- **NEW** — `Server/Middleware/CorrelationIdMiddleware.cs` — Generates and propagates correlation IDs per request
- **NEW** — `Server/Middleware/GlobalExceptionHandlerMiddleware.cs` — Catches all unhandled exceptions and returns sanitized responses
- **NEW** — `Server/Models/ErrorResponse.cs` — Standardized error response DTO with correlation ID
- **MODIFY** — `Server/Program.cs` — Register correlation ID and exception handler middleware in pipeline

## Implementation Plan

1. **Create `CorrelationIdMiddleware`**:
   - Checks incoming request for `X-Correlation-ID` header; if present, uses it; otherwise generates a new `Guid.NewGuid().ToString("N")`.
   - Stores correlation ID in `HttpContext.Items["CorrelationId"]` for access throughout the request pipeline.
   - Adds correlation ID to the response header `X-Correlation-ID`.
   - Pushes correlation ID into Serilog's `LogContext` using `LogContext.PushProperty("CorrelationId", correlationId)` so all log entries for the request include it.
   - This satisfies TR-028 (request correlation IDs for distributed tracing).

2. **Create `ErrorResponse` DTO**:
   - Properties:
     - `CorrelationId` (string) — The request's correlation ID for support reference.
     - `Message` (string) — User-friendly error message (e.g., "An unexpected error occurred. Please contact support with the reference ID.").
     - `StatusCode` (int) — HTTP status code.
     - `Errors` (List<FieldError>?, optional) — Only populated for validation errors (AC-4), where each `FieldError` has `Field` and `Message`.
   - Serialized as JSON with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.

3. **Create `GlobalExceptionHandlerMiddleware`**:
   - Wraps the entire downstream pipeline in a try-catch block.
   - On exception:
     - Retrieves correlation ID from `HttpContext.Items["CorrelationId"]`.
     - Logs the full exception via `ILogger.LogError(exception, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId)` — PII in the exception message/data will be redacted by the PII enricher from task_002.
     - Maps exception types to HTTP status codes:
       - `ValidationException` → 400 Bad Request
       - `UnauthorizedAccessException` → 401 Unauthorized
       - `KeyNotFoundException` → 404 Not Found
       - `OperationCanceledException` → 499 Client Closed Request
       - All others → 500 Internal Server Error
     - Returns `ErrorResponse` with:
       - The correlation ID.
       - A generic user-friendly message per status code (no exception message, no stack trace, no PII).
       - For `ValidationException`: populates `Errors` list with field names and expected formats (AC-4).
   - Sets `Content-Type: application/json` and appropriate HTTP status code.
   - **Security guarantee (AC-3)**: The middleware NEVER includes `exception.Message`, `exception.StackTrace`, `exception.InnerException`, or `exception.Data` in the response body.

4. **Configure user-friendly error messages** (static mapping):
   ```csharp
   private static readonly Dictionary<int, string> UserFriendlyMessages = new()
   {
       [400] = "The request contains invalid data. Please check the fields below.",
       [401] = "Authentication is required to access this resource.",
       [403] = "You do not have permission to perform this action.",
       [404] = "The requested resource was not found.",
       [409] = "A conflict occurred with the current state of the resource.",
       [429] = "Too many requests. Please try again later.",
       [500] = "An unexpected error occurred. Please contact support with reference ID: {correlationId}."
   };
   ```

5. **Register middleware in `Program.cs`** (order matters):
   ```csharp
   app.UseMiddleware<CorrelationIdMiddleware>();     // 1st: Generate correlation ID
   app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); // 2nd: Catch all exceptions
   app.UseAuthentication();                          // 3rd: Auth
   app.UseAuthorization();                           // 4th: Authz
   app.UseMiddleware<InputSanitizationMiddleware>(); // 5th: Sanitize inputs (task_001)
   app.MapControllers();
   ```

6. **Ensure Serilog LogContext enrichment**: In `Program.cs` Serilog configuration, add `.Enrich.FromLogContext()` to enable correlation ID propagation from `CorrelationIdMiddleware` into all log entries for the request scope.

## Current Project State

```text
Server/
├── Program.cs
├── appsettings.json
├── Middleware/
│   └── InputSanitizationMiddleware.cs (from task_001)
├── Logging/
│   ├── PiiRedactionEnricher.cs (from task_002)
│   ├── PiiDestructuringPolicy.cs (from task_002)
│   └── PiiMaskingPatterns.cs (from task_002)
├── Models/
│   ├── ValidationErrorResponse.cs (from task_001)
│   └── (existing DTOs)
└── Server.csproj
```

> Project structure is a placeholder — will be updated based on completion of dependent tasks.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Middleware/CorrelationIdMiddleware.cs | Generates per-request correlation ID, propagates via header and Serilog LogContext |
| CREATE | Server/Middleware/GlobalExceptionHandlerMiddleware.cs | Catches unhandled exceptions, returns sanitized ErrorResponse with correlation ID |
| CREATE | Server/Models/ErrorResponse.cs | Standardized error DTO with CorrelationId, Message, StatusCode, and optional Errors list |
| MODIFY | Server/Program.cs | Register CorrelationIdMiddleware and GlobalExceptionHandlerMiddleware in correct pipeline order |

## External References

- [ASP.NET Core Error Handling (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-8.0)
- [ASP.NET Core Middleware Order](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0#middleware-order)
- [Serilog LogContext — Enrichment](https://github.com/serilog/serilog/wiki/Enrichment#the-logcontext)
- [Serilog.Enrichers.ClientInfo — WithCorrelationId()](https://github.com/serilog-contrib/Serilog.Enrichers.ClientInfo)
- [OWASP Error Handling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Error_Handling_Cheat_Sheet.html)
- [TR-028: Request Correlation IDs for Distributed Tracing](design.md)

## Build Commands

```powershell
cd Server
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Unhandled exceptions return JSON with `correlationId` and user-friendly `message` only — no stack trace, no PII, no internal details (AC-3)
- [ ] Response headers include `X-Correlation-ID` for every request
- [ ] Serilog log entries include `CorrelationId` property for traceability
- [ ] `ValidationException` returns 400 with field-level error details (AC-4)
- [ ] `KeyNotFoundException` returns 404 with generic message
- [ ] `UnauthorizedAccessException` returns 401 with generic message
- [ ] Unknown exceptions return 500 with generic message including correlation ID reference
- [ ] Exception messages containing PII are logged server-side with PII redaction applied (via task_002)

## Implementation Checklist

- [ ] Create `CorrelationIdMiddleware` with header propagation and Serilog LogContext enrichment
- [ ] Create `ErrorResponse` DTO with `CorrelationId`, `Message`, `StatusCode`, and optional `Errors` list
- [ ] Create `GlobalExceptionHandlerMiddleware` with exception-to-status-code mapping and sanitized responses
- [ ] Define static user-friendly error message mapping per HTTP status code
- [ ] Register `CorrelationIdMiddleware` and `GlobalExceptionHandlerMiddleware` in correct pipeline order in `Program.cs`
- [ ] Verify `.Enrich.FromLogContext()` is configured in Serilog pipeline for correlation ID propagation
