# Task - task_002_be_input_sanitization_middleware

## Requirement Reference

- User Story: us_093
- Story Location: .propel/context/tasks/EP-018/us_093/us_093.md
- Acceptance Criteria:
  - AC-3: Given input is received from any user interface, When the input is processed, Then SQL injection, XSS, and command injection attacks are prevented through parameterized queries and input sanitization.

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
| Backend | Entity Framework Core | 8.x |
| Backend | Serilog | 8.x |
| Backend | FluentValidation.AspNetCore | 11.x |

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

Implement a defense-in-depth input sanitization and security middleware pipeline that prevents SQL injection, XSS, and command injection attacks across all API endpoints (AC-3, NFR-018). The implementation provides three layers of protection: (1) an `InputSanitizationMiddleware` in the ASP.NET Core request pipeline that inspects and sanitizes all incoming request bodies, query strings, and headers for known attack patterns before they reach controller actions; (2) Content Security Policy (CSP) and security response headers middleware that prevents XSS by restricting script execution contexts; (3) a `SecurityValidationFilter` action filter that validates model-bound inputs using pattern-based detection for SQL injection keywords, HTML/script tags, and OS command sequences. The system uses EF Core's parameterized queries as the primary SQL injection defense and adds application-level sanitization as a secondary barrier. All detected attack attempts are logged via Serilog with correlation IDs for incident investigation without exposing sensitive payload details.

## Dependent Tasks

- US_066 — Requires base input sanitization infrastructure.

## Impacted Components

- **NEW** `src/UPACIP.Api/Middleware/InputSanitizationMiddleware.cs` — Request pipeline middleware: inspects body/query/headers for attack patterns
- **NEW** `src/UPACIP.Api/Middleware/SecurityHeadersMiddleware.cs` — Adds CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy headers
- **NEW** `src/UPACIP.Api/Filters/SecurityValidationFilter.cs` — Action filter: pattern-based detection on model-bound inputs
- **NEW** `src/UPACIP.Service/Security/InputSanitizer.cs` — IInputSanitizer: centralized sanitization logic (HTML encode, SQL pattern strip, command pattern strip)
- **NEW** `src/UPACIP.Service/Security/Models/SecurityOptions.cs` — Configuration: blocked patterns, max input lengths, CSP directives
- **NEW** `src/UPACIP.Service/Security/Models/ThreatDetectionResult.cs` — DTO: detected threat type, pattern matched, sanitized value
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register middleware pipeline, bind SecurityOptions
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add SecurityOptions configuration section

## Implementation Plan

1. **Create `SecurityOptions` configuration model**: Create in `src/UPACIP.Service/Security/Models/SecurityOptions.cs`:
   - `int MaxRequestBodySizeBytes` (default: 1048576 — 1 MB). Rejects oversized request bodies to prevent buffer overflow attacks.
   - `int MaxStringInputLength` (default: 10000). Maximum length for any single string input field.
   - `bool EnableInputSanitization` (default: true). Feature flag to enable/disable sanitization.
   - `bool LogBlockedRequests` (default: true). Whether to log blocked requests with correlation ID.
   - `string ContentSecurityPolicy` (default: `"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; frame-ancestors 'none'"`).
   Register via `IOptionsMonitor<SecurityOptions>`. Add to `appsettings.json`:
   ```json
   "Security": {
     "MaxRequestBodySizeBytes": 1048576,
     "MaxStringInputLength": 10000,
     "EnableInputSanitization": true,
     "LogBlockedRequests": true,
     "ContentSecurityPolicy": "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; frame-ancestors 'none'"
   }
   ```

2. **Implement `IInputSanitizer` / `InputSanitizer`**: Create in `src/UPACIP.Service/Security/InputSanitizer.cs`. Constructor injection of `IOptionsMonitor<SecurityOptions>` and `ILogger<InputSanitizer>`.

   **SQL injection detection** — `ThreatDetectionResult DetectSqlInjection(string input)`:
   - Pattern-match against known SQL injection keywords using case-insensitive regex: `(?i)\b(SELECT|INSERT|UPDATE|DELETE|DROP|ALTER|EXEC|EXECUTE|UNION|CREATE|TRUNCATE)\b.*\b(FROM|INTO|TABLE|SET|WHERE|DATABASE|SCHEMA)\b`.
   - Detect SQL comment sequences: `--`, `/*`, `*/`.
   - Detect string termination attacks: single quotes followed by SQL keywords: `';\s*(SELECT|DROP|INSERT|UPDATE|DELETE)`.
   - Detect tautology attacks: `' OR '1'='1`, `' OR 1=1`.
   - Note: EF Core parameterized queries are the primary defense. This is a secondary detection layer.

   **XSS detection** — `ThreatDetectionResult DetectXss(string input)`:
   - Detect HTML script tags: `<script[^>]*>`, `</script>`.
   - Detect event handler attributes: `on(load|error|click|mouseover|focus|blur|submit|change|input)\s*=`.
   - Detect JavaScript protocol: `javascript:`, `vbscript:`, `data:text/html`.
   - Detect encoded script injection: `&#x3C;script`, `%3Cscript`, `\u003cscript`.

   **Command injection detection** — `ThreatDetectionResult DetectCommandInjection(string input)`:
   - Detect OS command separators: `; `, ` && `, ` || `, ` | `, backtick.
   - Detect common OS commands: `(?i)\b(cmd|powershell|bash|sh|wget|curl|net\s+user|whoami|cat\s+/etc|rm\s+-rf)\b`.
   - Detect path traversal: `\.\.[\\/]`, `%2e%2e%2f`.

   **Sanitize** — `string Sanitize(string input)`:
   - HTML-encode using `System.Net.WebUtility.HtmlEncode`.
   - Trim to `MaxStringInputLength`.
   - Remove null bytes (`\0`).
   - Normalize Unicode to NFC form to prevent homoglyph attacks.

   **`ThreatDetectionResult`**: `bool ThreatDetected`, `string ThreatType` ("SqlInjection", "XSS", "CommandInjection", "None"), `string? MatchedPattern` (the regex that matched, not the payload), `string SanitizedValue`.

3. **Implement `InputSanitizationMiddleware`**: Create in `src/UPACIP.Api/Middleware/InputSanitizationMiddleware.cs`. This middleware sits early in the request pipeline (after authentication, before routing).

   **`Task InvokeAsync(HttpContext context, IInputSanitizer sanitizer)`**:
   - (a) Skip sanitization for specific content types: `multipart/form-data` (file uploads handled separately), `application/octet-stream`.
   - (b) **Query string inspection**: Iterate over `context.Request.Query` key-value pairs. For each value, run all three detection methods. If threat detected, log warning: `Log.Warning("THREAT_BLOCKED: Type={ThreatType}, Source=QueryString, Key={Key}, CorrelationId={CorrelationId}")` and return `400 Bad Request` with generic error (do not reveal detection details to attacker).
   - (c) **Request body inspection** (for JSON content): Enable request body buffering (`context.Request.EnableBuffering()`). Read the body, run detection on all string values within the JSON. If threat detected, log and return `400 Bad Request`.
   - (d) **Header inspection**: Inspect custom headers (skip standard headers like `Authorization`, `Content-Type`). Detect injection in custom header values.
   - (e) If no threats detected, call `next(context)`.
   - (f) Never log the raw malicious payload (PII/attack data risk). Log only the threat type and matched pattern name.

4. **Implement `SecurityHeadersMiddleware`**: Create in `src/UPACIP.Api/Middleware/SecurityHeadersMiddleware.cs`. Adds security response headers to every response:
   - `Content-Security-Policy`: from `SecurityOptions.ContentSecurityPolicy` — prevents inline script execution (XSS primary defense on response side).
   - `X-Content-Type-Options: nosniff` — prevents MIME type sniffing.
   - `X-Frame-Options: DENY` — prevents clickjacking.
   - `X-XSS-Protection: 0` — disable legacy browser XSS filter (CSP is the modern replacement).
   - `Referrer-Policy: strict-origin-when-cross-origin` — limits referrer leakage.
   - `Permissions-Policy: camera=(), microphone=(), geolocation=()` — restricts browser feature access.
   - `Strict-Transport-Security: max-age=31536000; includeSubDomains` — enforces HTTPS (TR-018).

5. **Implement `SecurityValidationFilter`**: Create in `src/UPACIP.Api/Filters/SecurityValidationFilter.cs` as an `IAsyncActionFilter`. This runs after model binding but before the controller action:
   - (a) Iterate over `context.ActionArguments` (the model-bound parameters).
   - (b) For each argument that is a string or contains string properties (via reflection, max depth 3 to avoid performance issues), run `IInputSanitizer.DetectSqlInjection`, `DetectXss`, `DetectCommandInjection`.
   - (c) If any threat detected, add model state error and return `400 Bad Request` with `ProblemDetails`: `{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "Invalid Input", "status": 400, "detail": "The request contains potentially unsafe content." }`.
   - (d) Log the detection with correlation ID but without the raw payload.

6. **Verify EF Core parameterized query compliance**: This step is a verification-only activity (no new code):
   - Document in the task that EF Core LINQ queries (`.Where(x => x.Email == email)`) automatically generate parameterized SQL (`WHERE email = @p0`).
   - Document that raw SQL usage (`FromSqlRaw`, `ExecuteSqlRaw`) MUST use parameterized overloads (`FromSqlInterpolated`, `ExecuteSqlInterpolated`) — never string concatenation.
   - Add a comment in `SecurityOptions` referencing this requirement: `// Primary SQL injection defense: EF Core parameterized queries (NFR-018)`.

7. **Configure middleware pipeline order in `Program.cs`**: Add middleware in the correct order:
   ```csharp
   // Security headers FIRST (applies to all responses)
   app.UseMiddleware<SecurityHeadersMiddleware>();
   // Input sanitization AFTER authentication (needs correlation ID)
   app.UseMiddleware<InputSanitizationMiddleware>();
   ```
   Register services:
   - `services.AddScoped<IInputSanitizer, InputSanitizer>()`.
   - `services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"))`.
   - `services.AddScoped<SecurityValidationFilter>()`.
   - Register `SecurityValidationFilter` globally: `services.AddControllers(options => options.Filters.AddService<SecurityValidationFilter>())`.

8. **Structured logging for security events**: All security-related log entries use the `SECURITY_` prefix for easy filtering in Seq:
   - `SECURITY_THREAT_BLOCKED` — attack attempt detected and blocked.
   - `SECURITY_INPUT_SANITIZED` — input was sanitized (debug level only, not in production).
   - `SECURITY_HEADERS_APPLIED` — confirmation that security headers are set (startup only).
   - Include correlation ID from `HttpContext.TraceIdentifier` in all security log entries for incident tracing.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   └── [existing middleware]
│   │   ├── Filters/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/                              ← from task_001
│   │   ├── Import/
│   │   ├── Migration/
│   │   └── Monitoring/
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

> Assumes US_066 (input sanitization base) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Middleware/InputSanitizationMiddleware.cs | Request pipeline middleware: query/body/header attack detection |
| CREATE | src/UPACIP.Api/Middleware/SecurityHeadersMiddleware.cs | CSP, HSTS, X-Frame-Options, X-Content-Type-Options headers |
| CREATE | src/UPACIP.Api/Filters/SecurityValidationFilter.cs | Action filter: model-bound input threat detection |
| CREATE | src/UPACIP.Service/Security/InputSanitizer.cs | IInputSanitizer: SQL/XSS/command injection detection and sanitization |
| CREATE | src/UPACIP.Service/Security/Models/SecurityOptions.cs | Config: CSP directives, max input lengths, feature flags |
| CREATE | src/UPACIP.Service/Security/Models/ThreatDetectionResult.cs | DTO: threat type, pattern matched, sanitized value |
| MODIFY | src/UPACIP.Api/Program.cs | Register middleware pipeline order, SecurityValidationFilter, bind SecurityOptions |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Security configuration section |

## External References

- [OWASP SQL Injection Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
- [OWASP XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
- [OWASP Command Injection Prevention](https://cheatsheetseries.owasp.org/cheatsheets/OS_Command_Injection_Defense_Cheat_Sheet.html)
- [Content Security Policy — MDN](https://developer.mozilla.org/en-US/docs/Web/HTTP/CSP)
- [ASP.NET Core Middleware — .NET 8](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [ASP.NET Core Action Filters](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters#action-filters)
- [WebUtility.HtmlEncode — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.net.webutility.htmlencode)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] SQL injection patterns in query strings return 400 Bad Request (AC-3)
- [ ] XSS script tags in request body return 400 Bad Request (AC-3)
- [ ] Command injection sequences in inputs return 400 Bad Request (AC-3)
- [ ] CSP header is present on all API responses
- [ ] HSTS header enforces HTTPS with 1-year max-age
- [ ] X-Frame-Options DENY prevents clickjacking
- [ ] Security events logged with SECURITY_ prefix and correlation ID
- [ ] Raw malicious payloads are NOT logged (only threat type and pattern name)
- [ ] File upload content types (multipart/form-data) bypass body sanitization

## Implementation Checklist

- [ ] Create SecurityOptions configuration model with CSP directives and feature flags
- [ ] Implement IInputSanitizer with SQL injection, XSS, and command injection detection
- [ ] Implement InputSanitizationMiddleware for query string, body, and header inspection
- [ ] Implement SecurityHeadersMiddleware with CSP, HSTS, X-Frame-Options, XCTO
- [ ] Implement SecurityValidationFilter for model-bound input validation
- [ ] Configure middleware pipeline order in Program.cs (headers → sanitization)
- [ ] Add Security configuration section to appsettings.json
- [ ] Verify structured logging with SECURITY_ prefix and no raw payload exposure
