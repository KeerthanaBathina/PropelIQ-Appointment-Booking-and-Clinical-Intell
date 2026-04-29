using System.Diagnostics;
using Serilog;
using Serilog.Context;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Captures HTTP request duration and outcome (success/client-error/server-error) and
/// writes a structured log entry for every request, satisfying AC-4's "duration" and
/// "outcome" fields (US_095, AC-4, TR-028, NFR-035).
///
/// Log levels:
///   2xx → <see cref="Log.Information"/>  ("Success")
///   4xx → <see cref="Log.Warning"/>      ("ClientError")
///   5xx → <see cref="Log.Error"/>        ("ServerError")
///   Unhandled exception → <see cref="Log.Error"/>  ("UnhandledException")
///
/// Must be placed after <see cref="CorrelationIdMiddleware"/> in the pipeline so
/// <c>CorrelationId</c> is already in the LogContext when the request log is written.
/// </summary>
public sealed class OperationLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public OperationLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw     = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? "/";

        Exception? caughtException = null;
        var outcome = "Success";

        try
        {
            await _next(context);

            // Determine outcome from HTTP status code.
            outcome = context.Response.StatusCode switch
            {
                >= 500 => "ServerError",
                >= 400 => "ClientError",
                _      => "Success",
            };
        }
        catch (Exception ex)
        {
            outcome          = "UnhandledException";
            caughtException  = ex;
        }
        finally
        {
            sw.Stop();
            var durationMs = sw.Elapsed.TotalMilliseconds;
            var statusCode = context.Response.HasStarted ? context.Response.StatusCode : 0;

            // Push Duration and Outcome into LogContext so all sinks receive them as
            // structured properties (AC-4 — duration, outcome).
            using (LogContext.PushProperty("Duration", durationMs))
            using (LogContext.PushProperty("Outcome", outcome))
            {
                if (caughtException is not null)
                {
                    Log.Error(caughtException,
                        "HTTP_REQUEST_COMPLETED: {Method} {Path} {Outcome} in {Duration:F1}ms",
                        method, path, outcome, durationMs);
                }
                else if (outcome == "ServerError")
                {
                    Log.Error(
                        "HTTP_REQUEST_COMPLETED: {Method} {Path} responded {StatusCode} ({Outcome}) in {Duration:F1}ms",
                        method, path, statusCode, outcome, durationMs);
                }
                else if (outcome == "ClientError")
                {
                    Log.Warning(
                        "HTTP_REQUEST_COMPLETED: {Method} {Path} responded {StatusCode} ({Outcome}) in {Duration:F1}ms",
                        method, path, statusCode, outcome, durationMs);
                }
                else
                {
                    Log.Information(
                        "HTTP_REQUEST_COMPLETED: {Method} {Path} responded {StatusCode} ({Outcome}) in {Duration:F1}ms",
                        method, path, statusCode, outcome, durationMs);
                }
            }
        }

        // Re-throw so the global exception handler can produce the HTTP response.
        if (caughtException is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(caughtException)
                .Throw();
    }
}

public static class OperationLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseOperationLogging(this IApplicationBuilder app)
        => app.UseMiddleware<OperationLoggingMiddleware>();
}
