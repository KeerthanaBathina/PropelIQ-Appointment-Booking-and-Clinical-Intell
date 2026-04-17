# Task - task_002_be_swagger_openapi_documentation

## Requirement Reference

- User Story: us_098
- Story Location: .propel/context/tasks/EP-019/us_098/us_098.md
- Acceptance Criteria:
  - AC-2: Given API endpoints exist, When a developer navigates to /swagger, Then Swagger UI displays auto-generated OpenAPI 3.0 documentation with request/response schemas and example values.
- Edge Case:
  - How does the system handle API documentation for versioned endpoints? Swagger groups endpoints by API version with separate documentation pages per version.

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
| Backend | Swashbuckle.AspNetCore | 6.x |
| Backend | Asp.Versioning.Mvc.ApiExplorer | 8.x |

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

Configure Swagger/Swashbuckle for auto-generated OpenAPI 3.0 API documentation with request/response schemas, example values, and API version grouping, satisfying AC-2, TR-032, NFR-038, and edge case 2. The implementation registers `Swashbuckle.AspNetCore` with `Asp.Versioning.Mvc.ApiExplorer` to produce separate Swagger documents per API version (v1, v2, etc.), each accessible at `/swagger/v{n}/swagger.json`. Swagger UI at `/swagger` displays versioned documentation pages with a version dropdown selector. XML documentation comments from controllers and DTOs are included for rich schema descriptions. Example values are provided via `[SwaggerExample]` attributes and Swashbuckle schema filters. JWT Bearer authentication is documented via a security scheme definition, allowing developers to test authenticated endpoints directly from Swagger UI.

## Dependent Tasks

- US_001 — Requires backend project scaffold with controllers.
- US_096 task_002 — Coordinates with HATEOAS response wrappers (schemas include Links).

## Impacted Components

- **NEW** `src/UPACIP.Api/Swagger/SwaggerConfiguration.cs` — Extension methods for Swagger service registration and middleware
- **NEW** `src/UPACIP.Api/Swagger/SwaggerExampleSchemaFilter.cs` — Schema filter providing example values for DTOs
- **NEW** `src/UPACIP.Api/Swagger/ConfigureSwaggerOptions.cs` — IConfigureNamedOptions for versioned Swagger document generation
- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add Swashbuckle and API versioning packages, enable XML docs
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register Swagger services and middleware

## Implementation Plan

1. **Add Swagger and versioning NuGet packages**: Add to `src/UPACIP.Api/UPACIP.Api.csproj`:
   ```xml
   <PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
   <PackageReference Include="Swashbuckle.AspNetCore.Annotations" Version="6.*" />
   <PackageReference Include="Asp.Versioning.Mvc" Version="8.*" />
   <PackageReference Include="Asp.Versioning.Mvc.ApiExplorer" Version="8.*" />
   ```
   Enable XML documentation generation for schema descriptions:
   ```xml
   <PropertyGroup>
     <GenerateDocumentationFile>true</GenerateDocumentationFile>
     <NoWarn>$(NoWarn);1591</NoWarn>
   </PropertyGroup>
   ```
   - `Swashbuckle.AspNetCore` — Swagger generation and UI.
   - `Swashbuckle.AspNetCore.Annotations` — `[SwaggerOperation]`, `[SwaggerResponse]`, `[SwaggerExample]` attributes.
   - `Asp.Versioning.Mvc` — API version routing (`[ApiVersion]` attribute).
   - `Asp.Versioning.Mvc.ApiExplorer` — integrates API versioning with Swagger document generation.
   - `NoWarn 1591` — suppresses "Missing XML comment" warnings for non-documented types (only controllers and DTOs require XML docs).

2. **Create `ConfigureSwaggerOptions` for versioned documents (edge case 2)**: Create in `src/UPACIP.Api/Swagger/ConfigureSwaggerOptions.cs`:
   ```csharp
   public class ConfigureSwaggerOptions : IConfigureNamedOptions<SwaggerGenOptions>
   {
       private readonly IApiVersionDescriptionProvider _provider;

       public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
       {
           _provider = provider;
       }

       public void Configure(SwaggerGenOptions options)
       {
           foreach (var description in _provider.ApiVersionDescriptions)
           {
               options.SwaggerDoc(description.GroupName, CreateVersionInfo(description));
           }
       }

       public void Configure(string? name, SwaggerGenOptions options) => Configure(options);

       private static OpenApiInfo CreateVersionInfo(ApiVersionDescription description)
       {
           var info = new OpenApiInfo
           {
               Title = "UPACIP API",
               Version = description.ApiVersion.ToString(),
               Description = "Unified Patient Access & Clinical Intelligence Platform API",
               Contact = new OpenApiContact
               {
                   Name = "UPACIP Development Team",
                   Email = "dev@upacip.com"
               }
           };

           if (description.IsDeprecated)
           {
               info.Description += " [DEPRECATED - This API version is no longer supported]";
           }

           return info;
       }
   }
   ```
   This produces a separate OpenAPI document for each API version (`v1`, `v2`, etc.). Deprecated versions are labeled in the description. The Swagger UI dropdown lets developers select which version to browse.

3. **Create `SwaggerExampleSchemaFilter` for example values (AC-2)**: Create in `src/UPACIP.Api/Swagger/SwaggerExampleSchemaFilter.cs`:
   ```csharp
   public class SwaggerExampleSchemaFilter : ISchemaFilter
   {
       public void Apply(OpenApiSchema schema, SchemaFilterContext context)
       {
           if (context.Type == typeof(AuditLogEntry))
           {
               schema.Example = new OpenApiObject
               {
                   ["userId"] = new OpenApiString(Guid.NewGuid().ToString()),
                   ["action"] = new OpenApiString("Create"),
                   ["entityType"] = new OpenApiString("Patient"),
                   ["entityId"] = new OpenApiString(Guid.NewGuid().ToString()),
                   ["ipAddress"] = new OpenApiString("192.168.1.100"),
                   ["correlationId"] = new OpenApiString(Guid.NewGuid().ToString())
               };
           }
           else if (context.Type == typeof(AuditLogQueryFilter))
           {
               schema.Example = new OpenApiObject
               {
                   ["fromUtc"] = new OpenApiString("2026-01-01T00:00:00Z"),
                   ["toUtc"] = new OpenApiString("2026-12-31T23:59:59Z"),
                   ["entityType"] = new OpenApiString("Patient"),
                   ["page"] = new OpenApiInteger(1),
                   ["pageSize"] = new OpenApiInteger(50)
               };
           }
       }
   }
   ```
   The schema filter provides realistic example values for DTOs in the Swagger UI "Try it out" feature. Additional DTO examples can be added following the same pattern.

4. **Create `SwaggerConfiguration` extension methods**: Create in `src/UPACIP.Api/Swagger/SwaggerConfiguration.cs`:
   ```csharp
   public static class SwaggerConfiguration
   {
       public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
       {
           services.AddApiVersioning(options =>
           {
               options.DefaultApiVersion = new ApiVersion(1, 0);
               options.AssumeDefaultVersionWhenUnspecified = true;
               options.ReportApiVersions = true;
               options.ApiVersionReader = ApiVersionReader.Combine(
                   new UrlSegmentApiVersionReader(),
                   new HeaderApiVersionReader("X-Api-Version"));
           })
           .AddApiExplorer(options =>
           {
               options.GroupNameFormat = "'v'VVV";
               options.SubstituteApiVersionInUrl = true;
           });

           services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

           services.AddSwaggerGen(options =>
           {
               // Include XML comments for schema descriptions
               var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
               var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
               if (File.Exists(xmlPath))
                   options.IncludeXmlComments(xmlPath);

               // Include Contracts project XML comments
               var contractsXml = Path.Combine(AppContext.BaseDirectory, "UPACIP.Contracts.xml");
               if (File.Exists(contractsXml))
                   options.IncludeXmlComments(contractsXml);

               // JWT Bearer security definition
               options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
               {
                   Name = "Authorization",
                   Type = SecuritySchemeType.Http,
                   Scheme = "bearer",
                   BearerFormat = "JWT",
                   In = ParameterLocation.Header,
                   Description = "Enter your JWT token: Bearer {token}"
               });

               options.AddSecurityRequirement(new OpenApiSecurityRequirement
               {
                   {
                       new OpenApiSecurityScheme
                       {
                           Reference = new OpenApiReference
                           {
                               Type = ReferenceType.SecurityScheme,
                               Id = "Bearer"
                           }
                       },
                       Array.Empty<string>()
                   }
               });

               // Register schema filter for example values
               options.SchemaFilter<SwaggerExampleSchemaFilter>();

               // Enable annotations
               options.EnableAnnotations();
           });

           return services;
       }

       public static WebApplication UseSwaggerDocumentation(this WebApplication app)
       {
           app.UseSwagger();
           app.UseSwaggerUI(options =>
           {
               var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
               foreach (var description in provider.ApiVersionDescriptions.Reverse())
               {
                   options.SwaggerEndpoint(
                       $"/swagger/{description.GroupName}/swagger.json",
                       $"UPACIP API {description.GroupName}");
               }

               options.RoutePrefix = "swagger";
               options.DocumentTitle = "UPACIP API Documentation";
               options.DefaultModelsExpandDepth(2);
               options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
               options.EnableDeepLinking();
               options.EnableFilter();
               options.ShowExtensions();
           });

           return app;
       }
   }
   ```
   Key features:
   - **API versioning (edge case 2)**: URL segment (`/api/v1/...`) and header (`X-Api-Version`) readers. Swagger generates separate documents per version with a version dropdown.
   - **JWT Bearer (AC-2)**: Security scheme definition allows developers to paste a JWT token and test authenticated endpoints in Swagger UI.
   - **XML comments (AC-2)**: Includes XML docs from both `UPACIP.Api` and `UPACIP.Contracts` assemblies for complete schema descriptions.
   - **Schema filter**: Injects example values for DTOs so "Try it out" pre-populates realistic data.

5. **Add API version attribute to controllers**: Existing and new controllers need `[ApiVersion]` and versioned routes:
   ```csharp
   [ApiController]
   [ApiVersion("1.0")]
   [Route("api/v{version:apiVersion}/audit-logs")]
   public class AuditLogController : ControllerBase { ... }
   ```
   Controllers targeting multiple versions use `[MapToApiVersion]`:
   ```csharp
   [ApiVersion("1.0")]
   [ApiVersion("2.0")]
   [ApiController]
   [Route("api/v{version:apiVersion}/patients")]
   public class PatientController : ControllerBase
   {
       [HttpGet("{id}")]
       [MapToApiVersion("1.0")]
       public IActionResult GetV1(Guid id) { ... }

       [HttpGet("{id}")]
       [MapToApiVersion("2.0")]
       public IActionResult GetV2(Guid id) { ... }
   }
   ```

6. **Add XML documentation annotations to controllers**: Annotate controller actions with XML summary, param, and response docs:
   ```csharp
   /// <summary>
   /// Retrieves a paginated list of audit log entries with optional filtering.
   /// </summary>
   /// <param name="filter">Query filter for date range, user, entity type, and pagination.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Paginated audit log entries with HATEOAS navigation links.</returns>
   /// <response code="200">Returns filtered audit log entries.</response>
   /// <response code="401">Unauthorized — missing or invalid JWT token.</response>
   /// <response code="403">Forbidden — caller does not have Admin role.</response>
   [HttpGet(Name = "QueryAuditLogs")]
   [ProducesResponseType(typeof(PagedHateoasResponse<AuditLogReadModel>), StatusCodes.Status200OK)]
   [ProducesResponseType(StatusCodes.Status401Unauthorized)]
   [ProducesResponseType(StatusCodes.Status403Forbidden)]
   [SwaggerOperation(Summary = "Query audit logs", Tags = new[] { "Audit Logs" })]
   public async Task<IActionResult> Query([FromQuery] AuditLogQueryFilter filter, CancellationToken ct)
   ```
   This produces rich Swagger documentation with request/response schemas, status codes, and descriptions (AC-2).

7. **Enable XML docs for `UPACIP.Contracts.csproj`**: Add to `UPACIP.Contracts.csproj` so DTO XML comments appear in Swagger schemas:
   ```xml
   <PropertyGroup>
     <GenerateDocumentationFile>true</GenerateDocumentationFile>
     <NoWarn>$(NoWarn);1591</NoWarn>
   </PropertyGroup>
   ```

8. **Integrate Swagger in `Program.cs`**: Add Swagger registration and middleware:
   ```csharp
   // In service registration
   builder.Services.AddSwaggerDocumentation();

   // In middleware pipeline (after authentication, before endpoints)
   app.UseSwaggerDocumentation();
   ```
   Swagger UI is accessible at `/swagger` in all environments. For production, consider restricting access via middleware or authorization policy.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AuditLogController.cs                ← from US_096 task_003
│   │   │   ├── RecoveryController.cs
│   │   │   └── ...
│   │   ├── Logging/
│   │   │   └── SerilogConfiguration.cs              ← from task_001
│   │   ├── Middleware/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   │   ├── UPACIP.Contracts.csproj
│   │   ├── Models/
│   │   │   ├── HateoasLink.cs                       ← from US_096 task_002
│   │   │   ├── HateoasResponse.cs
│   │   │   ├── PagedHateoasResponse.cs
│   │   │   ├── AuditLogEntry.cs                     ← from US_096 task_003
│   │   │   └── ...
│   │   └── Services/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (project scaffold), US_096 (layered architecture, HATEOAS, CQRS), and task_001 (Serilog/Seq) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Api/Swagger/SwaggerConfiguration.cs | Extension methods: AddSwaggerDocumentation, UseSwaggerDocumentation |
| CREATE | src/UPACIP.Api/Swagger/ConfigureSwaggerOptions.cs | Versioned Swagger document generation per API version |
| CREATE | src/UPACIP.Api/Swagger/SwaggerExampleSchemaFilter.cs | Schema filter providing example values for DTOs |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add Swashbuckle, Asp.Versioning packages, enable XML docs |
| MODIFY | src/UPACIP.Contracts/UPACIP.Contracts.csproj | Enable GenerateDocumentationFile for DTO XML comments |
| MODIFY | src/UPACIP.Api/Program.cs | Register AddSwaggerDocumentation and UseSwaggerDocumentation |

## External References

- [Swashbuckle.AspNetCore — Getting Started](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)
- [ASP.NET Core API Versioning](https://github.com/dotnet/aspnet-api-versioning)
- [OpenAPI 3.0 Specification](https://spec.openapis.org/oas/v3.0.3)
- [Swagger UI Configuration](https://swagger.io/docs/open-source-tools/swagger-ui/usage/configuration/)
- [XML Documentation Comments — C#](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/)

## Build Commands

```powershell
# Build with XML doc generation
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Verify Swagger endpoint
# Start application, then navigate to http://localhost:5000/swagger
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding Swashbuckle packages
- [ ] Navigating to /swagger displays Swagger UI (AC-2)
- [ ] Swagger UI shows OpenAPI 3.0 documentation with request/response schemas (AC-2)
- [ ] DTOs display example values in "Try it out" feature (AC-2)
- [ ] XML documentation comments appear as schema descriptions (AC-2)
- [ ] Version dropdown shows v1 (and v2 when added) (edge case 2)
- [ ] Each version has a separate swagger.json document (edge case 2)
- [ ] JWT Bearer "Authorize" button allows token input for authenticated endpoint testing
- [ ] ProducesResponseType attributes generate correct status code documentation
- [ ] Swagger correctly documents HATEOAS response structures with Links

## Implementation Checklist

- [ ] Add Swashbuckle and Asp.Versioning NuGet packages to UPACIP.Api.csproj
- [ ] Enable GenerateDocumentationFile in Api and Contracts projects
- [ ] Create ConfigureSwaggerOptions for versioned document generation
- [ ] Create SwaggerExampleSchemaFilter with DTO example values
- [ ] Create SwaggerConfiguration with AddSwaggerDocumentation and UseSwaggerDocumentation
- [ ] Add JWT Bearer security scheme definition for authenticated endpoint testing
- [ ] Add ApiVersion attributes and versioned routes to controllers
- [ ] Integrate Swagger registration and middleware in Program.cs
