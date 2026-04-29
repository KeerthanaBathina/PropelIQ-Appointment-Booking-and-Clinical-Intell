using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Configures a separate OpenAPI document per API version so the Swagger UI version dropdown
/// lists every declared API version (US_098, AC-2, edge case 2).
///
/// Registered as <c>IConfigureNamedOptions&lt;SwaggerGenOptions&gt;</c> so it runs automatically
/// when Swashbuckle enumerates the API version descriptions discovered by
/// <see cref="IApiVersionDescriptionProvider"/>.
/// </summary>
public sealed class ConfigureSwaggerOptions : IConfigureNamedOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        => _provider = provider;

    /// <inheritdoc />
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
            options.SwaggerDoc(description.GroupName, CreateVersionInfo(description));
    }

    /// <inheritdoc />
    public void Configure(string? name, SwaggerGenOptions options) => Configure(options);

    private static OpenApiInfo CreateVersionInfo(ApiVersionDescription description)
    {
        var info = new OpenApiInfo
        {
            Title       = "UPACIP API",
            Version     = description.ApiVersion.ToString(),
            Description = "Unified Patient Access &amp; Clinical Intelligence Platform API",
            Contact     = new OpenApiContact
            {
                Name  = "UPACIP Development Team",
                Email = "dev@upacip.clinic"
            }
        };

        if (description.IsDeprecated)
            info.Description += " [DEPRECATED — This API version is no longer supported]";

        return info;
    }
}
