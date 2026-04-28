using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Performance;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core convention-based middleware that auto-instruments every incoming
/// HTTP request with an <see cref="Activity"/> span and records end-to-end latency
/// for SLA evaluation (US_081 task_001, AC-4, edge case — span-level APM tracing).
///
/// <para>
/// <b>Route classification (operation type):</b>
/// <list type="table">
///   <item><c>/api/appointments/book*</c> → <c>Booking</c></item>
///   <item><c>/api/documents/parse*</c>  → <c>DocumentParsing</c></item>
///   <item><c>/api/coding/*</c>          → <c>MedicalCoding</c></item>
///   <item>All other routes              → <c>General</c> (not SLA-tracked)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Response headers added on every response:</b>
/// <list type="bullet">
///   <item><c>X-Request-Duration-Ms: {duration}</c></item>
///   <item><c>X-Correlation-Id: {activityId}</c></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Pipeline position:</b> registered after <c>UseAuthorization</c> so JWT claims
/// are available for optional tagging, and before <c>MapControllers()</c>.
/// </para>
/// </summary>
public sealed class PerformanceInstrumentationMiddleware
{
    // ── Route prefixes and their operation type labels ────────────────────────

    private static readonly (string Prefix, string OperationType)[] RouteMap =
    [
        ("/api/appointments/book", "Booking"),
        ("/api/documents/parse",  "DocumentParsing"),
        ("/api/coding/",          "MedicalCoding"),
    ];

    private const string FallbackOperationType = "General";

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly RequestDelegate                                _next;
    private readonly IPerformanceTracker                            _tracker;
    private readonly ILogger<PerformanceInstrumentationMiddleware>  _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public PerformanceInstrumentationMiddleware(
        RequestDelegate                               next,
        IPerformanceTracker                           tracker,
        ILogger<PerformanceInstrumentationMiddleware> logger)
    {
        _next    = next;
        _tracker = tracker;
        _logger  = logger;
    }

    // ── Middleware invocation ─────────────────────────────────────────────────

    public async Task InvokeAsync(HttpContext context)
    {
        var operationType = ClassifyOperation(context.Request.Path);

        // Start the root operation span.
        var activity = _tracker.StartOperation(operationType, new Dictionary<string, string>
        {
            ["http.method"]      = context.Request.Method,
            ["http.path"]        = context.Request.Path.Value ?? "/",
            ["http.scheme"]      = context.Request.Scheme,
        });

        // Capture activity ID for the correlation header BEFORE awaiting next.
        var activityId = activity?.Id ?? Activity.Current?.Id ?? Guid.NewGuid().ToString();

        var sw = Stopwatch.StartNew();
        bool success = false;

        try
        {
            await _next(context);
            success = context.Response.StatusCode < 500;
        }
        catch (Exception ex)
        {
            success = false;
            _logger.LogError(ex,
                "PerformanceInstrumentationMiddleware: unhandled exception. " +
                "OperationType={OperationType} Path={Path}",
                operationType, context.Request.Path);
            throw;
        }
        finally
        {
            sw.Stop();
            var latencyMs = sw.ElapsedMilliseconds;

            // Record latency into the sliding-window histogram for P95 computation.
            _tracker.RecordLatency(operationType, latencyMs);

            // Stop the Activity span and emit the Meter measurement.
            _tracker.CompleteOperation(activity, success);

            // Add observability headers to the response.
            // Use try/catch so header write failures never mask the actual response.
            try
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers["X-Request-Duration-Ms"] = latencyMs.ToString();
                    context.Response.Headers["X-Correlation-Id"]      = activityId;
                }
            }
            catch (InvalidOperationException)
            {
                // Response already started (e.g. streaming) — headers cannot be set.
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps a request path to an operation type string using the route prefix table.
    /// Matching is case-insensitive.
    /// </summary>
    private static string ClassifyOperation(PathString path)
    {
        if (!path.HasValue) return FallbackOperationType;

        var pathLower = path.Value!.ToLowerInvariant();

        foreach (var (prefix, operationType) in RouteMap)
        {
            if (pathLower.StartsWith(prefix, StringComparison.Ordinal))
                return operationType;
        }

        return FallbackOperationType;
    }
}
