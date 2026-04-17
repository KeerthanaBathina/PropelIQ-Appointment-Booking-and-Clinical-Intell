# Task - task_001_be_input_validation_sanitization

## Requirement Reference

- User Story: us_066
- Story Location: .propel/context/tasks/EP-011/us_066/us_066.md
- Acceptance Criteria:
    - AC-1: **Given** any user input is received, **When** the input is processed, **Then** it is sanitized against SQL injection, XSS, and command injection before reaching business logic.
    - AC-4: **Given** a validation rule fails, **When** the response is generated, **Then** the error message specifies which field failed and the expected format without exposing internal logic.
- Requirement Tags: FR-095, NFR-018
- Edge Case:
    - EC-1: Medical data containing special characters (e.g., "O'Brien") — system uses parameterized queries; single quotes in data are properly escaped without data loss.

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
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
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

Implement an input validation and sanitization layer in the ASP.NET Core Web API middleware pipeline. This task creates reusable middleware and validation infrastructure that intercepts all incoming requests, sanitizes user inputs against SQL injection, XSS, and command injection attacks, and returns structured validation error responses that specify the failing field and expected format without exposing internal system logic. The solution leverages ASP.NET Core's built-in model validation pipeline, custom action filters, and the HtmlSanitizer library for XSS prevention, while relying on Entity Framework Core's parameterized queries for SQL injection protection.

## Dependent Tasks

- US_001 — Foundational — Requires backend middleware pipeline to be established (Program.cs middleware registration infrastructure)

## Impacted Components

- **NEW** — `Server/Middleware/InputSanitizationMiddleware.cs` — ASP.NET Core middleware for request body sanitization
- **NEW** — `Server/Filters/ValidateModelAttribute.cs` — Action filter for standardized model validation error responses
- **NEW** — `Server/Validation/SanitizationExtensions.cs` — String extension methods for input sanitization (anti-XSS, command injection prevention)
- **NEW** — `Server/Models/ValidationErrorResponse.cs` — Standardized validation error DTO
- **MODIFY** — `Server/Program.cs` — Register sanitization middleware and validation filters in the pipeline

## Implementation Plan

1. **Create `SanitizationExtensions` utility class** with static extension methods for string sanitization:
   - `SanitizeForXss()` — Uses the HtmlSanitizer NuGet package (v8.x) to strip dangerous HTML/JS while preserving safe content. Configure allowed tags/attributes via `HtmlSanitizer` options.
   - `SanitizeForCommandInjection()` — Strips or encodes shell metacharacters (`|`, `&`, `;`, `` ` ``, `$`, `(`, `)`, `>`, `<`) from string inputs that could reach OS-level commands.
   - `IsCleanInput()` — Composite check that validates input against known injection patterns using regex deny-lists.
   - **Edge case handling (EC-1)**: Single quotes in names like "O'Brien" are NOT stripped. SQL injection prevention is handled at the ORM layer via EF Core parameterized queries, not by sanitizing quotes from input data.

2. **Create `InputSanitizationMiddleware`** that:
   - Reads the request body stream (buffering enabled via `EnableBuffering()`).
   - For `application/json` content types, deserializes the body, recursively walks string properties, and applies `SanitizeForXss()` and `SanitizeForCommandInjection()`.
   - Replaces the request body stream with the sanitized version.
   - For query string parameters, applies sanitization to each value.
   - Passes through non-string and non-applicable content types unchanged.
   - Logs a warning via `ILogger<InputSanitizationMiddleware>` when sanitization modifies input (without logging the original PII value).

3. **Create `ValidateModelAttribute` action filter** that:
   - Inherits from `ActionFilterAttribute` and overrides `OnActionExecuting`.
   - Checks `ModelState.IsValid`; if false, constructs a `ValidationErrorResponse` listing each invalid field with its user-friendly error message.
   - Returns `400 Bad Request` with the structured response — field name + expected format only, no stack traces or internal details (AC-4).
   - Suppresses ASP.NET Core's default `[ApiController]` automatic 400 response by configuring `ApiBehaviorOptions.SuppressModelStateInvalidFilter = true` in `Program.cs`.

4. **Create `ValidationErrorResponse` DTO**:
   - Properties: `CorrelationId` (string), `Errors` (list of `{ Field: string, Message: string }`).
   - Error messages use format: `"Field '{field}' is invalid. Expected: {format description}."` — no internal logic exposed.

5. **Register in `Program.cs`**:
   - Add `app.UseMiddleware<InputSanitizationMiddleware>()` early in the pipeline (after authentication, before routing).
   - Configure `builder.Services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true)`.
   - Register `ValidateModelAttribute` as a global filter via `builder.Services.AddControllers(o => o.Filters.Add<ValidateModelAttribute>())`.

6. **Install NuGet dependency**: `Ganss.Xss` (HtmlSanitizer) v8.x for XSS sanitization.

7. **Verify EF Core parameterized queries**: Confirm all data access uses LINQ or parameterized raw SQL — no string-concatenated queries. Add an `.editorconfig` or Roslyn analyzer rule to warn on `FromSqlRaw` with string interpolation (use `FromSqlInterpolated` instead).

## Current Project State

```text
Server/
├── Program.cs
├── appsettings.json
├── Middleware/
│   └── (empty — to be created)
├── Filters/
│   └── (empty — to be created)
├── Validation/
│   └── (empty — to be created)
├── Models/
│   └── (existing DTOs)
└── Server.csproj
```

> Project structure is a placeholder — will be updated based on completion of dependent task US_001.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Middleware/InputSanitizationMiddleware.cs | ASP.NET Core middleware that sanitizes request body and query strings against XSS and command injection |
| CREATE | Server/Filters/ValidateModelAttribute.cs | Action filter that returns structured validation errors (field + expected format) |
| CREATE | Server/Validation/SanitizationExtensions.cs | String extension methods for XSS and command injection sanitization |
| CREATE | Server/Models/ValidationErrorResponse.cs | Standardized validation error response DTO with CorrelationId |
| MODIFY | Server/Program.cs | Register middleware, suppress default model state filter, add global validation filter |
| MODIFY | Server/Server.csproj | Add Ganss.Xss (HtmlSanitizer) NuGet package reference |

## External References

- [ASP.NET Core Middleware Documentation (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [ASP.NET Core Model Validation (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-8.0)
- [HtmlSanitizer (Ganss.Xss) — GitHub](https://github.com/mganss/HtmlSanitizer)
- [EF Core Raw SQL — Parameterized Queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#passing-parameters)
- [OWASP Input Validation Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html)
- [OWASP XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Scripting_Prevention_Cheat_Sheet.html)

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
- [ ] XSS payloads (e.g., `<script>alert(1)</script>`) are stripped from request body and query strings
- [ ] SQL injection patterns (e.g., `' OR 1=1 --`) are handled by EF Core parameterized queries without data loss
- [ ] Command injection metacharacters are sanitized from applicable string inputs
- [ ] Medical data with special characters (e.g., "O'Brien") passes through without data loss (EC-1)
- [ ] Validation errors return 400 with field name and expected format only — no stack traces or internal details (AC-4)
- [ ] No string-concatenated SQL queries exist in the codebase (Roslyn analyzer check)

## Implementation Checklist

- [ ] Install `Ganss.Xss` (HtmlSanitizer) v8.x NuGet package
- [ ] Create `SanitizationExtensions` with `SanitizeForXss()`, `SanitizeForCommandInjection()`, and `IsCleanInput()` methods
- [ ] Create `InputSanitizationMiddleware` with request body and query string sanitization
- [ ] Create `ValidationErrorResponse` DTO with `CorrelationId` and `Errors` list
- [ ] Create `ValidateModelAttribute` action filter returning structured 400 responses
- [ ] Register middleware, suppress default model state filter, and add global filter in `Program.cs`
- [ ] Verify EF Core parameterized query usage and configure Roslyn analyzer rule for `FromSqlRaw` warning
