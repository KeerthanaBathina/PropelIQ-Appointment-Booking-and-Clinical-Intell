using UPACIP.Service.Monitoring;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core convention middleware that records every request outcome into the
/// sliding-window <see cref="IErrorRateMonitor"/> for 0.1% threshold alerting
/// (US_083 task_001, AC-4, NFR-031).
///
/// <para>
/// Placed early in the pipeline (after <c>GlobalExceptionHandlerMiddleware</c>) so all
/// response codes — including those produced by other middleware — are captured.
/// </para>
///
/// <para>
/// Health check endpoints (<c>/health</c>, <c>/ready</c>) are excluded because they are
/// infrastructure probes rather than user workflows and would skew the error rate metric.
/// </para>
///
/// <para>
/// Workflow category is derived from the request path:
/// <list type="bullet">
///   <item><c>/api/appointments/*</c> → <c>booking</c></item>
///   <item><c>/api/intake/*</c> → <c>intake</c></item>
///   <item><c>/api/coding/*</c> → <c>coding</c></item>
///   <item>all other paths → <c>other</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class ErrorRateTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IErrorRateMonitor _errorRateMonitor;

    public ErrorRateTrackingMiddleware(RequestDelegate next, IErrorRateMonitor errorRateMonitor)
    {
        _next             = next;
        _errorRateMonitor = errorRateMonitor;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Skip infrastructure probe endpoints to avoid skewing the error rate metric.
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsHealthCheckPath(path))
            return;

        var isError  = context.Response.StatusCode is >= 400 and <= 599;
        var category = DetermineCategory(path);

        _errorRateMonitor.RecordRequestOutcome(isError, category);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsHealthCheckPath(string path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/ready",  StringComparison.OrdinalIgnoreCase);

    private static string DetermineCategory(string path)
    {
        if (path.StartsWith("/api/appointments", StringComparison.OrdinalIgnoreCase))
            return "booking";
        if (path.StartsWith("/api/intake", StringComparison.OrdinalIgnoreCase))
            return "intake";
        if (path.StartsWith("/api/coding", StringComparison.OrdinalIgnoreCase))
            return "coding";
        return "other";
    }
}
