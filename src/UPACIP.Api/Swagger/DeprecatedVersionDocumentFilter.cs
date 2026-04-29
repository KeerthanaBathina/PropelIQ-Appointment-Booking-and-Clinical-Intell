using Asp.Versioning.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Swashbuckle <see cref="IDocumentFilter"/> that prepends a deprecation warning banner to the
/// OpenAPI document description when the document's API version is flagged as deprecated
/// (US_102, AC-3).
///
/// Detection strategy:
///   The filter looks up the <see cref="ApiVersionDescription"/> for the current document
///   via <see cref="IApiVersionDescriptionProvider"/> and checks <c>IsDeprecated</c>.
///   This is consistent with how <see cref="ConfigureSwaggerOptions"/> adds the deprecation
///   note to <c>OpenApiInfo.Description</c> — the filter supplements it with additional detail.
///
/// The filter is registered in <see cref="SwaggerConfiguration.AddSwaggerDocumentation"/> via
/// <c>options.DocumentFilter&lt;DeprecatedVersionDocumentFilter&gt;()</c>.
/// </summary>
public sealed class DeprecatedVersionDocumentFilter : IDocumentFilter
{
    private readonly IApiVersionDescriptionProvider _provider;

    public DeprecatedVersionDocumentFilter(IApiVersionDescriptionProvider provider)
        => _provider = provider;

    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Locate the ApiVersionDescription whose group name matches this document.
        var description = _provider.ApiVersionDescriptions
            .FirstOrDefault(d => d.GroupName == context.DocumentName);

        if (description is null || !description.IsDeprecated)
            return;

        var versionString = description.ApiVersion.ToString();
        var existing = swaggerDoc.Info.Description ?? string.Empty;

        // Prepend the deprecation warning only if not already present (idempotent).
        if (!existing.Contains("DEPRECATED", StringComparison.OrdinalIgnoreCase))
        {
            swaggerDoc.Info.Description =
                $"⚠️ **DEPRECATED** — API version {versionString} is deprecated and will be " +
                $"removed on the date shown in the `Sunset` response header. " +
                $"Migrate to the latest stable version. " +
                $"See `/api/docs/migration/v{versionString}` for the migration guide.\n\n" +
                existing;
        }
    }
}
