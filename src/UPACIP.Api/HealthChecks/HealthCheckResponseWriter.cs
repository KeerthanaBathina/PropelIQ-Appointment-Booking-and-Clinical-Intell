using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Custom health check response writer that serializes a <see cref="HealthReport"/> to a
/// structured JSON object consumed by monitoring tools and load balancers (US_099, AC-1).
///
/// Response shape:
/// <code>
/// {
///   "status": "Healthy" | "Degraded" | "Unhealthy",
///   "totalDurationMs": 142.5,
///   "timestamp": "2026-04-17T10:30:00.000Z",
///   "entries": [
///     {
///       "name": "postgresql",
///       "status": "Healthy",
///       "durationMs": 12.3,
///       "description": "PostgreSQL connection successful",
///       "data": { "server": "PostgreSQL 16" }
///     }
///   ]
/// }
/// </code>
///
/// The <c>exception</c> and <c>data</c> fields are omitted when null / empty (AC-1).
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented              = true,
        PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
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
            timestamp       = DateTime.UtcNow.ToString("o"),
            entries         = report.Entries.Select(e => new
            {
                name        = e.Key,
                status      = e.Value.Status.ToString(),
                durationMs  = Math.Round(e.Value.Duration.TotalMilliseconds, 2),
                description = e.Value.Description,
                data        = e.Value.Data.Count > 0 ? e.Value.Data : null,
                exception   = e.Value.Exception?.Message,
            }),
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions));
    }
}

