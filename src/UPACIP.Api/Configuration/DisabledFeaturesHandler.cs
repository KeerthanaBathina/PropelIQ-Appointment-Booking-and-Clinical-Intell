using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.FeatureManagement.Mvc;
using System.Text.Json;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Returns a structured JSON error response when a feature-gated endpoint is
/// accessed while the corresponding flag is disabled, instead of the default
/// bare 404.
///
/// Response body:
/// <code>
/// { "error": "Feature is currently disabled", "feature": "FeatureName" }
/// </code>
/// </summary>
public sealed class DisabledFeaturesHandler : IDisabledFeaturesHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task HandleDisabledFeatures(IEnumerable<string> features, ActionExecutingContext context)
    {
        var featureName = features.FirstOrDefault() ?? "Unknown";

        context.HttpContext.Response.StatusCode  = StatusCodes.Status404NotFound;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        var payload = new { error = "Feature is currently disabled", feature = featureName };

        return context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions));
    }
}
