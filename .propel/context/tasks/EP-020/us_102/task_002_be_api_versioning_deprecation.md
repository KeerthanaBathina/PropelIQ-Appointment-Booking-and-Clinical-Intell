# Task - task_002_be_api_versioning_deprecation

## Requirement Reference

- User Story: us_102
- Story Location: .propel/context/tasks/EP-020/us_102/us_102.md
- Acceptance Criteria:
  - AC-2: Given the API is versioned, When a breaking change is introduced, Then the major version increments (v1 → v2) and the previous version remains available for a documented deprecation period.
  - AC-3: Given the API is documented, When a developer accesses the OpenAPI 3.0 spec, Then all endpoints, request/response schemas, error codes, and example payloads are generated from the code.
- Edge Case:
  - How does the system handle version negotiation when the client does not specify a version? System defaults to the latest stable version and includes a Deprecation header for sunset versions.

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
| Backend | Asp.Versioning.Mvc | 8.x |
| Backend | Asp.Versioning.Mvc.ApiExplorer | 8.x |
| Documentation | Swagger (Swashbuckle) | 6.x |
| Monitoring | Serilog | 8.x |

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

Implement API versioning using `Asp.Versioning.Mvc` with URL segment versioning (`/api/v1/...`, `/api/v2/...`), semantic versioning conventions (MAJOR.MINOR.PATCH per TR-035), and a deprecation lifecycle with `Sunset` and `Deprecation` response headers. When a breaking change is introduced, the major version increments and the previous version remains available for a documented deprecation period (AC-2). The versioning infrastructure integrates with the existing Swashbuckle setup (from US_098/task_002) to generate per-version Swagger documents — each version has its own OpenAPI 3.0 spec with accurate endpoints, schemas, error codes, and examples (AC-3). When a client does not specify a version, the system defaults to the latest stable version and includes a `Deprecation` header on sunset versions (edge case 2). A `VersionDeprecationMiddleware` adds `Sunset` and `Deprecation` headers to responses from deprecated API versions, giving consumers advance notice of version retirement.

## Dependent Tasks

- US_001 — Requires backend API scaffold.
- US_098/task_002 — Requires Swashbuckle + Asp.Versioning.Mvc.ApiExplorer setup (versioned Swagger documents).

## Impacted Components

- **CREATE** `src/UPACIP.Api/Configuration/ApiVersioningConfiguration.cs` — Versioning setup with default version and deprecation policy
- **CREATE** `src/UPACIP.Api/Middleware/VersionDeprecationMiddleware.cs` — Adds Sunset/Deprecation headers on deprecated versions
- **CREATE** `src/UPACIP.Api/Swagger/DeprecatedVersionDocumentFilter.cs` — Marks deprecated versions in Swagger UI
- **MODIFY** `src/UPACIP.Api/Controllers/` — Apply [ApiVersion] attributes to all controllers
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register versioning services and deprecation middleware

## Implementation Plan

1. **Configure API versioning with `Asp.Versioning.Mvc` (AC-2, edge case 2)**: Create `src/UPACIP.Api/Configuration/ApiVersioningConfiguration.cs`:
   ```csharp
   public static class ApiVersioningConfiguration
   {
       public static IServiceCollection AddApiVersioningServices(this IServiceCollection services)
       {
           services.AddApiVersioning(options =>
           {
               // Default to latest stable when client omits version
               options.DefaultApiVersion = new ApiVersion(1, 0);
               options.AssumeDefaultVersionWhenUnspecified = true;
               options.ReportApiVersions = true;

               // URL segment versioning: /api/v1/...
               options.ApiVersionReader = new UrlSegmentApiVersionReader();
           })
           .AddApiExplorer(options =>
           {
               // Format: 'v'major[.minor][-status]
               options.GroupNameFormat = "'v'VVV";
               options.SubstituteApiVersionInUrl = true;
           });

           return services;
       }
   }
   ```
   Key configuration:
   - **`AssumeDefaultVersionWhenUnspecified = true`** — clients that omit the version segment are routed to the default version (edge case 2). Prevents 400 errors for version-unaware consumers.
   - **`ReportApiVersions = true`** — response headers include `api-supported-versions` and `api-deprecated-versions`, informing clients of available and deprecated versions.
   - **`UrlSegmentApiVersionReader`** — version is read from the URL path (`/api/v1/appointments`). Chosen over header-based versioning for discoverability and cacheability.
   - **`GroupNameFormat = "'v'VVV"`** — Swagger groups use format `v1`, `v1.1`, etc.
   - **`SubstituteApiVersionInUrl = true`** — Swagger UI replaces `{version}` template with actual version numbers.

2. **Apply `[ApiVersion]` and route templates to controllers (AC-2)**: Update all controllers to use versioned routes:
   ```csharp
   [ApiController]
   [ApiVersion("1.0")]
   [Route("api/v{version:apiVersion}/[controller]")]
   public class AppointmentsController : ControllerBase
   {
       // All actions available in v1.0
   }
   ```
   For deprecated versions that remain available during the deprecation period:
   ```csharp
   [ApiController]
   [ApiVersion("1.0", Deprecated = true)] // v1 deprecated
   [ApiVersion("2.0")]                      // v2 is current
   [Route("api/v{version:apiVersion}/[controller]")]
   public class AppointmentsController : ControllerBase
   {
       [HttpGet]
       [MapToApiVersion("1.0")]
       public IActionResult GetV1() { /* v1 response shape */ }

       [HttpGet]
       [MapToApiVersion("2.0")]
       public IActionResult GetV2() { /* v2 response shape with breaking changes */ }
   }
   ```
   Versioning rules:
   - **Minor version increment** (1.0 → 1.1): Additive changes only (new fields, new endpoints). Backward compatible. No action required by consumers.
   - **Major version increment** (1.0 → 2.0): Breaking changes (removed fields, changed response shapes, renamed endpoints). Previous version marked `Deprecated = true` and remains available.
   - **`[MapToApiVersion]`** on action methods — enables a single controller class to serve multiple versions. Avoids controller proliferation.
   - **Deprecation flag** (`Deprecated = true`) triggers `api-deprecated-versions` response header and enables the deprecation middleware (step 3).

3. **Implement `VersionDeprecationMiddleware` (AC-2, edge case 2)**: Create `src/UPACIP.Api/Middleware/VersionDeprecationMiddleware.cs`:
   ```csharp
   public sealed class VersionDeprecationMiddleware
   {
       private readonly RequestDelegate _next;
       private readonly ILogger<VersionDeprecationMiddleware> _logger;

       // Deprecated version → sunset date mapping
       private static readonly Dictionary<string, DateTimeOffset> DeprecationSchedule = new()
       {
           // Example: v1 deprecated with sunset date 6 months from deprecation
           // { "1.0", new DateTimeOffset(2027, 04, 17, 0, 0, 0, TimeSpan.Zero) }
       };

       public VersionDeprecationMiddleware(
           RequestDelegate next,
           ILogger<VersionDeprecationMiddleware> logger)
       {
           _next = next;
           _logger = logger;
       }

       public async Task InvokeAsync(HttpContext context)
       {
           await _next(context);

           var apiVersion = context.GetRequestedApiVersion();
           if (apiVersion is null) return;

           var versionString = apiVersion.ToString();

           if (DeprecationSchedule.TryGetValue(versionString, out var sunsetDate))
           {
               // RFC 8594 — Sunset header
               context.Response.Headers.Append(
                   "Sunset",
                   sunsetDate.ToString("R")); // HTTP-date format (RFC 7231)

               // RFC 8594 — Deprecation header
               context.Response.Headers.Append(
                   "Deprecation",
                   "true");

               // Link to migration guide
               context.Response.Headers.Append(
                   "Link",
                   $"</api/docs/migration/v{versionString}>; rel=\"deprecation\"");

               _logger.LogInformation(
                   "DEPRECATED_API_VERSION_USED: Version={Version}, SunsetDate={SunsetDate}, " +
                   "Path={Path}",
                   versionString,
                   sunsetDate.ToString("o"),
                   context.Request.Path);
           }
       }
   }
   ```
   Key behaviors:
   - **`Sunset` header (RFC 8594)** — informs clients of the exact date when the deprecated version will be removed. Format: HTTP-date per RFC 7231 (e.g., `Thu, 17 Apr 2027 00:00:00 GMT`).
   - **`Deprecation` header** — signals that the version is deprecated. API consumers can check this header to trigger migration warnings in their code.
   - **`Link` header** — points to a migration guide document. Consumers can programmatically discover how to upgrade.
   - **Structured logging** — logs each request to a deprecated version with the path and sunset date, enabling monitoring of deprecated version usage.
   - **`DeprecationSchedule` dictionary** — maps version strings to sunset dates. Updated when a new version is released and the old version is deprecated.
   - **Deprecation period**: Minimum 6 months from the deprecation announcement to the sunset date (configurable per organizational policy).

4. **Create `DeprecatedVersionDocumentFilter` for Swagger (AC-3)**: Create `src/UPACIP.Api/Swagger/DeprecatedVersionDocumentFilter.cs`:
   ```csharp
   public sealed class DeprecatedVersionDocumentFilter : IDocumentFilter
   {
       public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
       {
           var apiDescription = context.ApiDescriptions
               .FirstOrDefault();

           if (apiDescription is null) return;

           var versionModel = apiDescription.GetApiVersion();
           if (versionModel is null) return;

           // Check if any action in this version group is deprecated
           var isDeprecated = context.ApiDescriptions
               .Any(desc => desc.ActionDescriptor.EndpointMetadata
                   .OfType<ApiVersionAttribute>()
                   .Any(attr => attr.Versions
                       .Any(v => v.ToString() == versionModel.ToString()
                                 && attr.Deprecated)));

           if (isDeprecated)
           {
               swaggerDoc.Info.Description =
                   $"⚠️ **DEPRECATED** — This API version ({versionModel}) is deprecated " +
                   $"and will be removed on the sunset date. Migrate to the latest version. " +
                   $"See the migration guide for details.\n\n" +
                   swaggerDoc.Info.Description;
           }
       }
   }
   ```
   - Prepends a deprecation warning to the Swagger document description when the version is marked deprecated.
   - Visible in Swagger UI as a prominent warning banner.
   - Non-deprecated versions show no warning.

5. **Enrich Swagger with comprehensive endpoint documentation (AC-3)**: Ensure all endpoints document request/response schemas, error codes, and examples. Apply conventions across controllers:
   ```csharp
   // Standard response documentation pattern for all controllers:
   [HttpPost]
   [IdempotentEndpoint]
   [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
   public async Task<IActionResult> CreateAppointment(
       [FromBody] CreateAppointmentRequest request)
   {
       // ...
   }
   ```
   Documentation requirements:
   - **`[ProducesResponseType]`** on every action — generates response schemas in OpenAPI spec.
   - **`ProblemDetails`** for all error responses — RFC 7807 standard error format.
   - **XML doc comments** on request/response DTOs — Swashbuckle extracts these into the OpenAPI description field.
   - **`[SwaggerOperation]`** attributes for complex endpoints (optional) — adds operation-level summary and description.
   - This builds on the Swagger infrastructure established in US_098/task_002 (Swashbuckle + `Asp.Versioning.Mvc.ApiExplorer` + XML doc comments + `SwaggerExampleSchemaFilter`).

6. **Configure Swagger to show per-version documents (AC-3)**: Integrate versioning with the existing Swashbuckle setup:
   ```csharp
   builder.Services.AddSwaggerGen(c =>
   {
       // Existing: SwaggerExampleSchemaFilter, IdempotencyKeyOperationFilter (from task_001)
       c.DocumentFilter<DeprecatedVersionDocumentFilter>();
   });
   ```
   The existing `ConfigureSwaggerOptions` (from US_098/task_002) already generates per-version OpenAPI documents using `IApiVersionDescriptionProvider`. This task adds the deprecation document filter and ensures `[ProducesResponseType]` is applied consistently.

7. **Define semantic versioning conventions**: Document the versioning policy as code via a constants class:
   ```csharp
   public static class ApiVersionConstants
   {
       /// <summary>
       /// Current stable API version.
       /// </summary>
       public static readonly ApiVersion Current = new(1, 0);

       /// <summary>
       /// Minimum deprecation period before version removal (months).
       /// </summary>
       public const int MinDeprecationPeriodMonths = 6;

       /// <summary>
       /// Semantic versioning rules:
       /// - MAJOR: Breaking changes (removed fields, changed response shapes, renamed endpoints)
       /// - MINOR: Additive changes (new fields, new endpoints, new optional parameters)
       /// - PATCH: Bug fixes, documentation updates (no behavioral change)
       ///
       /// Backward compatibility: Minor versions MUST NOT break existing consumers.
       /// Deprecation: Major version deprecation announced via Sunset header (RFC 8594).
       /// </summary>
       public const string VersioningPolicy = "SemVer 2.0.0";
   }
   ```

8. **Register versioning and deprecation middleware in `Program.cs`**: Update the application bootstrap:
   ```csharp
   // API versioning
   builder.Services.AddApiVersioningServices();

   // Middleware pipeline (after auth, before endpoint routing)
   app.UseMiddleware<VersionDeprecationMiddleware>();

   // Swagger (endpoint mapping)
   app.UseSwagger();
   app.UseSwaggerUI(options =>
   {
       var descriptions = app.DescribeApiVersions();
       foreach (var description in descriptions)
       {
           var url = $"/swagger/{description.GroupName}/swagger.json";
           var name = description.GroupName.ToUpperInvariant();
           if (description.IsDeprecated)
               name += " [DEPRECATED]";
           options.SwaggerEndpoint(url, name);
       }
   });
   ```
   - **`DescribeApiVersions()`** returns all registered API versions.
   - **Deprecated versions** are labeled `[DEPRECATED]` in the Swagger UI dropdown.
   - Each version has its own Swagger JSON endpoint.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Attributes/
│   │   │   └── IdempotentEndpointAttribute.cs    ← from task_001
│   │   ├── Configuration/
│   │   ├── Controllers/
│   │   ├── HealthChecks/
│   │   ├── Middleware/
│   │   │   └── IdempotencyMiddleware.cs           ← from task_001
│   │   ├── Swagger/
│   │   │   ├── ConfigureSwaggerOptions.cs         ← from US_098/task_002
│   │   │   ├── SwaggerExampleSchemaFilter.cs      ← from US_098/task_002
│   │   │   └── IdempotencyKeyOperationFilter.cs   ← from task_001
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   │   ├── Idempotency/                          ← from task_001
│   │   └── Configuration/
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (backend API scaffold), US_098/task_002 (Swagger + versioning base), and task_001 (idempotency) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Configuration/ApiVersioningConfiguration.cs | Asp.Versioning setup with URL segment reader, default version, version reporting |
| CREATE | src/UPACIP.Api/Middleware/VersionDeprecationMiddleware.cs | Adds Sunset, Deprecation, Link headers for deprecated versions |
| CREATE | src/UPACIP.Api/Swagger/DeprecatedVersionDocumentFilter.cs | Marks deprecated versions with warning banner in Swagger docs |
| MODIFY | src/UPACIP.Api/Controllers/ | Apply [ApiVersion], [MapToApiVersion], [ProducesResponseType] to all controllers |
| MODIFY | src/UPACIP.Api/Program.cs | Register versioning services, deprecation middleware, Swagger version dropdown |

## External References

- [Asp.Versioning.Mvc — URL Segment Versioning](https://github.com/dotnet/aspnet-api-versioning/wiki/URL-Path-Segment)
- [Asp.Versioning — API Version Deprecation](https://github.com/dotnet/aspnet-api-versioning/wiki/Version-Deprecation)
- [RFC 8594 — The Sunset HTTP Header Field](https://www.rfc-editor.org/rfc/rfc8594)
- [RFC 7807 — Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc7807)
- [Semantic Versioning 2.0.0](https://semver.org/)
- [Swashbuckle — IDocumentFilter](https://github.com/domaindrivendev/Swashbuckle.AspNetCore#document-filters)
- [ASP.NET Core API Versioning — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/web-api/advanced/conventions)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Verify Swagger spec generation
# 1. Run application
# 2. Navigate to /swagger/v1/swagger.json
# 3. Validate JSON against OpenAPI 3.0 schema
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] Breaking change increments major version (v1 → v2), previous version remains available (AC-2)
- [ ] Deprecated version responses include Sunset, Deprecation, and Link headers (AC-2)
- [ ] Client without version specified → routed to default latest stable version (edge case 2)
- [ ] /swagger/v1/swagger.json lists all v1 endpoints with schemas, error codes, examples (AC-3)
- [ ] /swagger/v2/swagger.json lists all v2 endpoints (when v2 exists) (AC-3)
- [ ] Deprecated version Swagger document shows deprecation warning banner (AC-3)
- [ ] Response headers include api-supported-versions and api-deprecated-versions
- [ ] [ProducesResponseType] on all actions with ProblemDetails for error responses (AC-3)

## Implementation Checklist

- [ ] Configure Asp.Versioning.Mvc with URL segment reader, default version, ReportApiVersions
- [ ] Apply [ApiVersion] and versioned route templates to all controllers
- [ ] Implement VersionDeprecationMiddleware with Sunset/Deprecation/Link headers
- [ ] Create DeprecatedVersionDocumentFilter for Swagger deprecation warnings
- [ ] Apply [ProducesResponseType] with ProblemDetails to all controller actions
- [ ] Define ApiVersionConstants with Current version and deprecation policy
- [ ] Integrate per-version Swagger documents with deprecated label in dropdown
- [ ] Register versioning services and deprecation middleware in Program.cs
