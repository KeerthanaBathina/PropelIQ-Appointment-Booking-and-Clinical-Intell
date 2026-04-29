using Serilog;
using Serilog.Context;

namespace UPACIP.Api.Logging;

/// <summary>
/// Extension methods that wire the Serilog logging pipeline into the ASP.NET Core host
/// (US_098, AC-1, TR-031, NFR-035).
///
/// Two extension points are provided:
/// <list type="bullet">
///   <item>
///     <see cref="AddSerilogLogging"/> — call on <see cref="WebApplicationBuilder"/> during
///     service registration to replace the default <see cref="ILoggerFactory"/> with Serilog.
///     Configuration is read from the "Serilog" section in <c>appsettings.json</c> so log
///     levels, sinks, and enrichers are configurable without code changes.
///   </item>
///   <item>
///     <see cref="UseSerilogRequestLogging(WebApplication)"/> — call after
///     <c>app.UseRouting()</c> to register Serilog's HTTP request logging middleware.
///     Emits one structured log event per request instead of the default multi-line output,
///     enriched with host, scheme, user-agent, and client IP.
///   </item>
/// </list>
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Replaces the default ASP.NET Core logging provider with Serilog, configured from
    /// the "Serilog" section in <c>appsettings.json</c> / <c>appsettings.{env}.json</c>.
    ///
    /// The three-parameter <c>UseSerilog</c> overload receives the <see cref="IServiceProvider"/>
    /// after all services are registered, which is required for DI-based enrichers and
    /// destructuring policies (e.g., <c>PiiRedactionEnricher</c>).
    /// </summary>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services);
        });

        return builder;
    }

    /// <summary>
    /// Registers Serilog's HTTP request logging middleware in the ASP.NET Core pipeline.
    ///
    /// Emits a single structured log event per HTTP request instead of the default verbose
    /// ASP.NET Core request log, including:
    /// <list type="bullet">
    ///   <item>Request method, path, status code, and elapsed time.</item>
    ///   <item>Request host and scheme for reverse-proxy diagnostics.</item>
    ///   <item>User-agent for client classification.</item>
    ///   <item>Client IP address for security correlation.</item>
    /// </list>
    ///
    /// Position in the middleware pipeline: place after <c>UseRouting</c> but before
    /// <c>UseAuthentication</c> so the path is already resolved but the identity is not
    /// yet populated (avoids logging PII in the URL before authentication runs).
    /// </summary>
    public static WebApplication UseSerilogRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost",   httpContext.Request.Host.Value ?? string.Empty);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("UserAgent",     httpContext.Request.Headers.UserAgent.ToString());
                diagnosticContext.Set("ClientIp",
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            };
        });

        return app;
    }
}

/// <summary>
/// Structured log context helpers for pushing operation-scoped properties into the Serilog
/// <see cref="LogContext"/> (US_098, AC-1).
///
/// Usage:
/// <code>
/// using (LogContextExtensions.PushOperationContext("PatientExport", patientId))
/// {
///     _logger.LogInformation("Starting patient data export");
/// }
/// </code>
/// All log events emitted inside the <c>using</c> block carry the <c>OperationName</c>
/// and (optionally) <c>EntityId</c> properties.
/// </summary>
public static class LogContextExtensions
{
    /// <summary>
    /// Pushes <c>OperationName</c> and optionally <c>EntityId</c> into the Serilog
    /// <see cref="LogContext"/> for the lifetime of the returned disposable.
    /// </summary>
    /// <param name="operationName">
    /// Human-readable name of the current operation (e.g. "PatientExport", "AppointmentCreate").
    /// </param>
    /// <param name="entityId">Optional primary key of the entity being processed.</param>
    /// <returns>
    /// A composite <see cref="IDisposable"/> that removes all pushed properties when disposed.
    /// </returns>
    public static IDisposable PushOperationContext(string operationName, Guid? entityId = null)
    {
        var stack = new List<IDisposable>
        {
            LogContext.PushProperty("OperationName", operationName),
        };

        if (entityId.HasValue)
            stack.Add(LogContext.PushProperty("EntityId", entityId.Value));

        return new CompositeDisposable(stack);
    }
}

/// <summary>
/// Disposes multiple <see cref="IDisposable"/> instances in order.
/// Used internally by <see cref="LogContextExtensions.PushOperationContext"/>.
/// </summary>
internal sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables;

    internal CompositeDisposable(List<IDisposable> disposables)
        => _disposables = disposables;

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
    }
}
