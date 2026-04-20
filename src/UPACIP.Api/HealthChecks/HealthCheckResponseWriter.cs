using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Custom health check response writer that serializes a <see cref="HealthReport"/> to a
/// structured JSON object consumed by monitoring tools and load balancers.
///
/// Response shape:
/// <code>
/// {
///   "status": "Healthy" | "Degraded" | "Unhealthy",
///   "totalDurationMs": 12.34,
///   "entries": [
///     {
///       "name": "database",
///       "status": "Healthy",
///       "durationMs": 10.12,
///       "description": null
///     }
///   ]
/// }
/// </code>
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes the <paramref name="report"/> as JSON to the HTTP response.
    /// Compatible with the <c>HealthCheckOptions.ResponseWriter</c> delegate signature.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status          = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            entries         = report.Entries.Select(e => new
            {
                name        = e.Key,
                status      = e.Value.Status.ToString(),
                durationMs  = Math.Round(e.Value.Duration.TotalMilliseconds, 2),
                description = e.Value.Description,
            }),
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions));
    }
}
