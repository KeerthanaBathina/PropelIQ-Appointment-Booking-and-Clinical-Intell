using Microsoft.Extensions.Options;
using Serilog.Context;
using UPACIP.Service.Logging;
using UPACIP.Service.Logging.Models;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Reads an incoming X-Correlation-ID header or generates a new GUID when absent.
/// Stores the value via <see cref="ICorrelationIdAccessor"/> (AsyncLocal — flows into
/// background jobs, AC-1, edge case 2), enriches Serilog LogContext, sets
/// HttpContext.TraceIdentifier, and echoes the ID back in the response header
/// to enable distributed tracing across services (US_095, AC-1, TR-028, TR-031).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey   = "CorrelationId";

    private readonly RequestDelegate        _next;
    private readonly string                 _headerName;

    public CorrelationIdMiddleware(
        RequestDelegate              next,
        IOptions<LoggingOptions>     options)
    {
        _next       = next;
        _headerName = options.Value.CorrelationIdHeader;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor accessor)
    {
        // (a) Read incoming header or generate new compact GUID (32-char lowercase hex).
        var correlationId = context.Request.Headers[_headerName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        // (b) Store in ICorrelationIdAccessor — flows into awaited continuations and
        //     background jobs spawned from this async context (edge case 2).
        accessor.SetCorrelationId(correlationId);

        // (c) Also store in HttpContext.Items for synchronous code that cannot inject
        //     ICorrelationIdAccessor (backward compatibility with existing controllers).
        context.Items[ItemsKey] = correlationId;

        // (d) Integrate with ASP.NET Core built-in tracing (Activity.TraceId etc.).
        context.TraceIdentifier = correlationId;

        // (e) Echo back in response — enables clients to trace their requests.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[_headerName] = correlationId;
            return Task.CompletedTask;
        });

        // (f) Push into Serilog LogContext — every log entry within this request scope
        //     automatically carries the CorrelationId property (AC-1).
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
