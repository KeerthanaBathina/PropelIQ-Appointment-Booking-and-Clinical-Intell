using Serilog.Context;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Reads an incoming X-Correlation-ID header or generates a new GUID when absent.
/// Stores the value in HttpContext.Items and echoes it back in the response header
/// to enable distributed tracing across services.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        context.Items[ItemsKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push the correlation ID into Serilog's LogContext so every log entry
        // emitted during this request automatically carries the CorrelationId
        // property — enabling distributed tracing via Seq (TR-028, US_066 AC-3).
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
