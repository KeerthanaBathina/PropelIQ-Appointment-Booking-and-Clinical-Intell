using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using UPACIP.Api.Middleware;
using UPACIP.Service.Security;
using UPACIP.Service.Security.Models;

namespace UPACIP.Api.Filters;

/// <summary>
/// Action filter that inspects model-bound input arguments for SQL injection, XSS, and
/// command injection patterns after model binding but before the controller action executes
/// (US_093 task_002, AC-3, NFR-018, OWASP A03 — Injection).
///
/// Detection strategy:
///   - Iterates over all <see cref="ActionExecutingContext.ActionArguments"/>.
///   - For string arguments: runs all three detection methods directly.
///   - For object arguments: reflects up to depth 3 to inspect public string properties.
///   - Returns 400 ProblemDetails on any detected threat.
///
/// Security notes:
///   - Raw malicious payload values are NEVER logged (AC-3, OWASP A09).
///   - Only the threat type and matched pattern name are logged.
///   - Disabled when <see cref="SecurityOptions.EnableInputSanitization"/> is <c>false</c>.
///
/// Registered globally via Program.cs:
/// <code>options.Filters.AddService&lt;SecurityValidationFilter&gt;()</code>
/// </summary>
public sealed class SecurityValidationFilter : IAsyncActionFilter
{
    private readonly IInputSanitizer                  _sanitizer;
    private readonly IOptionsMonitor<SecurityOptions> _options;
    private readonly ILogger<SecurityValidationFilter> _logger;

    public SecurityValidationFilter(
        IInputSanitizer                    sanitizer,
        IOptionsMonitor<SecurityOptions>   options,
        ILogger<SecurityValidationFilter>  logger)
    {
        _sanitizer = sanitizer;
        _options   = options;
        _logger    = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!_options.CurrentValue.EnableInputSanitization)
        {
            await next();
            return;
        }

        var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? context.HttpContext.TraceIdentifier;

        foreach (var (argumentName, argumentValue) in context.ActionArguments)
        {
            if (argumentValue is null) continue;

            var detected = InspectValue(argumentName, argumentValue, depth: 0);
            if (detected is not null)
            {
                if (_options.CurrentValue.LogBlockedRequests)
                {
                    _logger.LogWarning(
                        "SECURITY_THREAT_BLOCKED: ThreatType={ThreatType}, Pattern={Pattern}, " +
                        "Argument={Argument}, CorrelationId={CorrelationId}, Path={Path}",
                        detected.ThreatType,
                        detected.MatchedPattern,
                        argumentName,
                        correlationId,
                        context.HttpContext.Request.Path.Value);
                }

                context.Result = new ObjectResult(new ProblemDetails
                {
                    Type   = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Title  = "Invalid Input",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "The request contains potentially unsafe content.",
                })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };

                return;
            }
        }

        await next();
    }

    // ── Inspection helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Recursively inspects <paramref name="value"/> for threats.
    /// Max reflection depth is 3 to avoid performance issues with deeply nested objects.
    /// Returns the first detected threat result, or <c>null</c> if clean.
    /// </summary>
    private ThreatDetectionResult? InspectValue(string path, object? value, int depth)
    {
        if (value is null || depth > 3)
            return null;

        if (value is string str)
            return InspectString(str);

        // Avoid inspecting primitives, value types, and .NET framework types
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return null;

        // Reflect over public readable string properties (max depth 3)
        foreach (var property in type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object? propValue;
            try { propValue = property.GetValue(value); }
            catch { continue; }

            var result = propValue is string propStr
                ? InspectString(propStr)
                : InspectValue($"{path}.{property.Name}", propValue, depth + 1);

            if (result is not null)
                return result;
        }

        return null;
    }

    private ThreatDetectionResult? InspectString(string input)
    {
        var sqlResult = _sanitizer.DetectSqlInjection(input);
        if (sqlResult.ThreatDetected) return sqlResult;

        var xssResult = _sanitizer.DetectXss(input);
        if (xssResult.ThreatDetected) return xssResult;

        var cmdResult = _sanitizer.DetectCommandInjection(input);
        if (cmdResult.ThreatDetected) return cmdResult;

        return null;
    }
}
