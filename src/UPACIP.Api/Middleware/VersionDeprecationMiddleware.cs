using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core convention-based middleware that appends RFC 8594 deprecation headers to
/// responses served from deprecated API versions (US_102, AC-2, edge case 2).
///
/// Headers added for deprecated versions:
/// <list type="bullet">
///   <item>
///     <c>Sunset</c> (RFC 8594) — HTTP-date giving the exact removal date.
///     Allows API clients and monitoring tools to detect impending version retirement.
///   </item>
///   <item>
///     <c>Deprecation</c> — literal string <c>true</c> per the IETF deprecation header draft.
///     Consumers can check this header to surface migration warnings automatically.
///   </item>
///   <item>
///     <c>Link</c> — points to the versioned migration guide document.
///   </item>
/// </list>
///
/// Structured logging:
///   Every request to a deprecated version is logged at <c>Information</c> level with the
///   event name <c>DEPRECATED_API_VERSION_USED</c>, enabling dashboards to track migration
///   progress by counting deprecated-version call volume over time.
///
/// Deprecation schedule:
///   Populate <see cref="DeprecationSchedule"/> when a new major version is released.
///   The sunset date must be at least
///   <see cref="ApiVersioningConfiguration.MinDeprecationPeriodMonths"/> months in the future
///   per organizational policy (TR-035).
/// </summary>
public sealed class VersionDeprecationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<VersionDeprecationMiddleware> _logger;

    /// <summary>
    /// Maps deprecated API version strings to their RFC 8594 sunset dates.
    /// Updated each time a new major version is released.
    ///
    /// Example (uncomment when v2.0 ships):
    /// <code>
    /// { "1.0", new DateTimeOffset(2027, 10, 29, 0, 0, 0, TimeSpan.Zero) }
    /// </code>
    /// </summary>
    private static readonly Dictionary<string, DateTimeOffset> DeprecationSchedule = new()
    {
        // No deprecated versions in Phase 1 — add entries here when v2.0 is released.
        // { "1.0", new DateTimeOffset(2027, 10, 29, 0, 0, 0, TimeSpan.Zero) }
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
        if (apiVersion is null)
            return;

        var versionString = apiVersion.ToString();

        if (!DeprecationSchedule.TryGetValue(versionString, out var sunsetDate))
            return;

        // RFC 8594 — Sunset header (HTTP-date format per RFC 7231)
        context.Response.Headers.Append("Sunset", sunsetDate.ToString("R"));

        // IETF Deprecation Header draft
        context.Response.Headers.Append("Deprecation", "true");

        // Link header pointing to the version-specific migration guide
        context.Response.Headers.Append(
            "Link",
            $"</api/docs/migration/v{versionString}>; rel=\"deprecation\", " +
            $"</api/docs/migration/v{versionString}>; rel=\"sunset\"");

        _logger.LogInformation(
            "DEPRECATED_API_VERSION_USED: Version={Version}, SunsetDate={SunsetDate}, " +
            "Method={Method}, Path={Path}",
            versionString,
            sunsetDate.ToString("o"),
            context.Request.Method,
            context.Request.Path);
    }
}
