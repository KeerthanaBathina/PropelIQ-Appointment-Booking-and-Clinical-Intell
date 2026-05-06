using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Reflection;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Extension methods that wire Swashbuckle and ASP.NET Core API versioning into the application
/// (US_098, AC-2, TR-032, NFR-038).
///
/// Two extension points:
/// <list type="bullet">
///   <item>
///     <see cref="AddSwaggerDocumentation"/> — call on <see cref="IServiceCollection"/> during
///     service registration. Configures API versioning (default v1.0, header + query-string
///     readers) and Swashbuckle with JWT Bearer security, XML documentation comments, the
///     <see cref="SwaggerExampleSchemaFilter"/>, and per-version document generation via
///     <see cref="ConfigureSwaggerOptions"/>.
///   </item>
///   <item>
///     <see cref="UseSwaggerDocumentation"/> — call on <see cref="WebApplication"/> after
///     authentication middleware. Activates the Swagger JSON endpoints and the Swagger UI at
///     <c>/swagger</c> with a version dropdown (edge case 2).
///   </item>
/// </list>
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Registers API versioning and Swashbuckle OpenAPI services.
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        // ── API Versioning ────────────────────────────────────────────────────────────────────
        // AssumeDefaultVersionWhenUnspecified: existing controllers without [ApiVersion] default
        // to v1.0 so no route changes are required for the initial release (edge case 2).
        // ReportApiVersions: adds api-supported-versions / api-deprecated-versions headers.
        // Version can be specified via the X-Api-Version header or ?api-version= query-string.
        // URL-segment reader is also registered so future versioned routes (/api/v1/...)
        // are supported without changing this configuration.
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion                   = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions                   = true;
                options.ApiVersionReader                    = ApiVersionReader.Combine(
                    new UrlSegmentApiVersionReader(),
                    new HeaderApiVersionReader("X-Api-Version"),
                    new QueryStringApiVersionReader("api-version"));
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat           = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        // ConfigureSwaggerOptions runs before SwaggerGen and adds one SwaggerDoc per discovered
        // API version (uses IApiVersionDescriptionProvider).
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

        // Ensures SupportedRequestFormats contains multipart/form-data for actions that have
        // [FromForm] IFormFile parameters, working around an Asp.Versioning + Swashbuckle
        // incompatibility where ConsumesAttribute is not propagated into SupportedRequestFormats.
        services.AddSingleton<IApiDescriptionProvider, FormFileApiDescriptionProvider>();

        // ── Swashbuckle ───────────────────────────────────────────────────────────────────────
        services.AddSwaggerGen(options =>
        {
            // XML documentation comments from both Api and Contracts assemblies.
            // Produces rich schema descriptions in Swagger UI (AC-2).
            var xmlApi = Path.Combine(AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xmlApi))
                options.IncludeXmlComments(xmlApi);

            var xmlContracts = Path.Combine(AppContext.BaseDirectory, "UPACIP.Contracts.xml");
            if (File.Exists(xmlContracts))
                options.IncludeXmlComments(xmlContracts);

            // JWT Bearer security definition — Authorize button in Swagger UI (AC-2, NFR-038).
            var jwtScheme = new OpenApiSecurityScheme
            {
                Name         = "Authorization",
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header,
                Description  = "Enter your JWT access token (without the 'Bearer' prefix).",
                Reference    = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            };
            options.AddSecurityDefinition("Bearer", jwtScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { jwtScheme, Array.Empty<string>() }
            });

            // Example values for DTO schemas (AC-2, "Try it out" feature).
            options.SchemaFilter<SwaggerExampleSchemaFilter>();

            // Idempotency-Key header + 409/422 responses on [IdempotentEndpoint] operations (US_102, AC-3).
            options.OperationFilter<IdempotencyKeyOperationFilter>();

            // Deprecation warning banner on Swagger documents for deprecated API versions (US_102, AC-3).
            options.DocumentFilter<DeprecatedVersionDocumentFilter>();

            // Enable [SwaggerOperation], [SwaggerResponse], etc. attributes.
            options.EnableAnnotations();

            // Tell Swashbuckle how to represent IFormFile in schemas.
            options.MapType<IFormFile>(() => new Microsoft.OpenApi.Models.OpenApiSchema
            {
                Type   = "string",
                Format = "binary"
            });

            // Move IFormFile parameters from the query-parameter list into the
            // multipart/form-data request body where they belong.
            options.OperationFilter<FormFileOperationFilter>();
        });

        return services;
    }

    /// <summary>
    /// Activates the Swagger JSON middleware and Swagger UI at <c>/swagger</c>.
    /// All API versions discovered by <see cref="IApiVersionDescriptionProvider"/> appear in the
    /// version dropdown in reverse order (newest first) — edge case 2.
    /// </summary>
    public static WebApplication UseSwaggerDocumentation(this WebApplication app)
    {
        app.UseSwagger();

        app.UseSwaggerUI(options =>
        {
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

            // Reverse so the highest version appears first in the dropdown.
            // Deprecated versions are labeled [DEPRECATED] in the UI (US_102, AC-2, AC-3).
            foreach (var description in provider.ApiVersionDescriptions.Reverse())
            {
                var label = description.IsDeprecated
                    ? $"UPACIP API {description.GroupName.ToUpperInvariant()} [DEPRECATED]"
                    : $"UPACIP API {description.GroupName.ToUpperInvariant()}";

                options.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    label);
            }

            options.RoutePrefix            = "swagger";
            options.DocumentTitle          = "UPACIP API Documentation";
            options.DefaultModelsExpandDepth(2);
            options.DocExpansion(DocExpansion.List);
            options.EnableDeepLinking();
            options.EnableFilter();
            options.ShowExtensions();
        });

        return app;
    }
}
