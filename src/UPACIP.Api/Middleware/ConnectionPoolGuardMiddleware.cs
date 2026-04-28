using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using UPACIP.Service.Infrastructure;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core middleware that provides graceful degradation when the PostgreSQL
/// connection pool is exhausted (US_082 task_001, AC-2, edge case 1).
///
/// <para>
/// <b>Strategy:</b>
/// <list type="number">
///   <item>Pass the request through to the downstream pipeline normally — Npgsql's
///     internal 30-second Timeout handles the actual waiting.</item>
///   <item>If a <see cref="NpgsqlException"/> with a connection-pool-exhaustion message
///     bubbles up (i.e., the 30s queue timeout expired), return HTTP 503 with a
///     <c>Retry-After: 5</c> header and a structured JSON error body.</item>
///   <item>Log the 503 at Warning level with the pool utilization snapshot and
///     the request correlation ID for operational visibility.</item>
/// </list>
/// </para>
///
/// <para>
/// This is placed early in the pipeline (after HTTPS redirect, before authentication)
/// so pool-exhaustion errors are caught regardless of which controller triggered them.
/// </para>
/// </summary>
public sealed class ConnectionPoolGuardMiddleware
{
    private const int    RetryAfterSeconds = 5;
    private const string ErrorPayload      =
        "{\"error\":\"Service temporarily unavailable \u2014 database capacity exceeded\"," +
        "\"retryAfterSeconds\":5}";

    private readonly RequestDelegate                              _next;
    private readonly IConnectionPoolMonitor                       _poolMonitor;
    private readonly ILogger<ConnectionPoolGuardMiddleware>       _logger;

    public ConnectionPoolGuardMiddleware(
        RequestDelegate                             next,
        IConnectionPoolMonitor                      poolMonitor,
        ILogger<ConnectionPoolGuardMiddleware>      logger)
    {
        _next        = next;
        _poolMonitor = poolMonitor;
        _logger      = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NpgsqlException ex)
            when (ex.Message.Contains("connection pool", StringComparison.OrdinalIgnoreCase))
        {
            // Npgsql's internal queue timed out (Timeout=30 in connection string).
            // Capture a utilization snapshot for the warning log.
            var stats         = _poolMonitor.GetStatistics();
            var correlationId = context.TraceIdentifier;

            _logger.LogWarning(
                "ConnectionPoolGuardMiddleware: pool exhaustion timeout for request " +
                "{Path}. CorrelationId={CorrelationId} " +
                "Active={Active}/{MaxConnections} ({Utilization:F1}%) — returning HTTP 503.",
                context.Request.Path,
                correlationId,
                stats.Active, stats.MaxConnections, stats.UtilizationPercent);

            context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = RetryAfterSeconds.ToString();

            await context.Response.WriteAsync(ErrorPayload, context.RequestAborted);
        }
    }
}
