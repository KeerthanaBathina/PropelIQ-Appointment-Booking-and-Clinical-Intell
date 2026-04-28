using System.Text.Json;
using Microsoft.Extensions.Options;
using UPACIP.Service.Monitoring;
using UPACIP.Service.Monitoring.Models;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core middleware that short-circuits requests for AI-dependent features
/// when the system is in a degraded state (US_083 task_002, AC-1, AC-2, AC-3).
///
/// <para>
/// <b>Route-to-feature mapping</b>:
/// <list type="bullet">
///   <item><c>/api/intake/conversational*</c> → <c>ai_intake</c></item>
///   <item><c>/api/documents/parse*</c>, <c>/api/documents/upload*</c> → <c>ai_parsing</c></item>
///   <item><c>/api/coding/suggest*</c>, <c>/api/coding/auto*</c> → <c>ai_coding</c></item>
/// </list>
/// All other routes pass through unconditionally.
/// </para>
///
/// <para>
/// When a feature is unavailable the middleware returns HTTP 503 with a structured JSON body:
/// <code>
/// {
///   "error":           "service_degraded",
///   "message":         "...",
///   "fallbackAction":  "manual_intake" | "manual_upload" | "manual_coding",
///   "degradedSince":   "&lt;ISO-8601&gt;" | null,
///   "affectedFeature": "ai_intake" | "ai_parsing" | "ai_coding"
/// }
/// </code>
/// </para>
///
/// <para>
/// Placement in the pipeline: after <c>UseAuthentication()</c> and before
/// <c>UseRateLimiter()</c> so that the user context is available for logging
/// and the rate-limiter budget is not consumed for degraded-path requests.
/// </para>
/// </summary>
public sealed class GracefulDegradationMiddleware
{
    private readonly RequestDelegate          _next;
    private readonly IDegradationModeManager  _degradationManager;
    private readonly IOptions<DegradationOptions> _options;

    // Static JsonSerializerOptions instance — avoids per-request allocation.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    public GracefulDegradationMiddleware(
        RequestDelegate next,
        IDegradationModeManager degradationManager,
        IOptions<DegradationOptions> options)
    {
        _next               = next;
        _degradationManager = degradationManager;
        _options            = options;
    }

    /// <inheritdoc cref="IMiddleware.InvokeAsync"/>
    public async Task InvokeAsync(HttpContext context)
    {
        var (feature, fallback) = ResolveFeature(context.Request.Path);

        if (feature is not null && !_degradationManager.IsFeatureAvailable(feature))
        {
            var state = _degradationManager.GetCurrentState();
            await WriteDegradedResponseAsync(context, feature, fallback!, state);
            return;
        }

        await _next(context);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps the request path to a (featureName, fallbackAction) tuple.
    /// Returns (null, null) for paths that do not belong to AI-gated features.
    /// </summary>
    private static (string? feature, string? fallbackAction) ResolveFeature(PathString path)
    {
        var p = path.Value ?? string.Empty;

        if (p.StartsWith("/api/intake/conversational", StringComparison.OrdinalIgnoreCase))
            return ("ai_intake", "manual_intake");

        if (p.StartsWith("/api/documents/parse",  StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/api/documents/upload", StringComparison.OrdinalIgnoreCase))
            return ("ai_parsing", "manual_upload");

        if (p.StartsWith("/api/coding/suggest", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/api/coding/auto",    StringComparison.OrdinalIgnoreCase))
            return ("ai_coding", "manual_coding");

        return (null, null);
    }

    private static async Task WriteDegradedResponseAsync(
        HttpContext context,
        string featureName,
        string fallbackAction,
        DegradationState state)
    {
        context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        var body = new
        {
            error           = "service_degraded",
            message         = "The requested AI feature is temporarily unavailable. Please use the manual workflow.",
            fallbackAction,
            degradedSince   = state.DegradedSince?.ToString("O"),
            affectedFeature = featureName,
        };

        var json = JsonSerializer.Serialize(body, _jsonOptions);
        await context.Response.WriteAsync(json);
    }
}
