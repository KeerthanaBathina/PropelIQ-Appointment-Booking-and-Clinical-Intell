using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace UPACIP.Api.Logging;

/// <summary>
/// Serilog <see cref="ILogEventEnricher"/> that adds per-request contextual fields to
/// every log entry for full AC-4 structured log compliance (US_095, AC-4, TR-031).
///
/// Fields added:
///   <list type="bullet">
///     <item><description><c>UserId</c> — JWT NameIdentifier claim; "anonymous" for unauthenticated requests.</description></item>
///     <item><description><c>OperationName</c> — HTTP method + path (e.g. "POST /api/appointments"). AC-4: operation name.</description></item>
///     <item><description><c>ClientIp</c> — Remote IP address for audit and abuse detection.</description></item>
///     <item><description><c>UserAgent</c> — User-Agent header, truncated to 200 chars.</description></item>
///   </list>
///
/// Combined with Serilog's built-in <c>Timestamp</c> and <c>Level</c> properties, and
/// the <c>CorrelationId</c> pushed by <see cref="UPACIP.Api.Middleware.CorrelationIdMiddleware"/>,
/// this satisfies AC-4's requirement for: timestamp, level, correlation ID, user ID,
/// operation name. Duration and outcome are added by
/// <see cref="UPACIP.Api.Middleware.OperationLoggingMiddleware"/>.
///
/// Registered as Singleton (same pattern as <see cref="PiiRedactionEnricher"/>). The
/// <see cref="IHttpContextAccessor"/> is thread-safe and accesses the current request
/// via <c>AsyncLocal</c> internally, so singleton registration is safe.
/// </summary>
public sealed class StructuredLogEnricher : ILogEventEnricher
{
    private const int MaxUserAgentLength = 200;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public StructuredLogEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return; // Background thread — no HTTP context

        // UserId — from JWT NameIdentifier claim (AC-4: user ID if authenticated).
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("UserId", userId));

        // OperationName — method + path (AC-4: operation name).
        var operationName = $"{context.Request.Method} {context.Request.Path}";
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("OperationName", operationName));

        // ClientIp — for audit and abuse detection.
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("ClientIp", clientIp));

        // UserAgent — truncated to prevent oversized log entries.
        var rawUserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;
        var userAgent = rawUserAgent.Length > MaxUserAgentLength
            ? rawUserAgent[..MaxUserAgentLength]
            : rawUserAgent;
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("UserAgent", userAgent));
    }
}
