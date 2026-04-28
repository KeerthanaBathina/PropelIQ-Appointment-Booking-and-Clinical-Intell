using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.Service.Infrastructure.Models;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core middleware that applies a Polly V8 circuit-breaker per
/// <see cref="EndpointClassification"/> (US_082 task_001, AC-4, edge case 2).
///
/// <para>
/// <b>Classification rules (path-prefix, case-insensitive):</b>
/// <list type="table">
///   <item><b>Critical</b> — <c>CriticalPaths</c> in config. Always passed through.</item>
///   <item><b>NonCritical</b> — <c>/api/admin</c>, <c>/api/dashboard</c>, <c>/api/reports</c>,
///     <c>/api/history</c>. Low-threshold circuit.</item>
///   <item><b>Standard</b> — all other <c>/api/…</c> routes. High-threshold circuit.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Circuit state transitions</b> are logged at Warning level.  When a circuit opens,
/// the middleware returns HTTP 503 with a structured JSON body and a
/// <c>Retry-After</c> header matching the configured break duration.
/// </para>
///
/// <para>
/// <b>Polly V8 note:</b> Two <see cref="ResiliencePipeline"/> instances are built at
/// construction time and shared across all requests (thread-safe).
/// </para>
/// </summary>
public sealed class EndpointCircuitBreakerMiddleware
{
    // ── Non-critical prefixes ─────────────────────────────────────────────────
    private static readonly string[] NonCriticalPrefixes =
    [
        "/api/admin",
        "/api/dashboard",
        "/api/reports",
        "/api/history",
    ];

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly RequestDelegate                              _next;
    private readonly CircuitBreakerOptions                        _config;
    private readonly ILogger<EndpointCircuitBreakerMiddleware>    _logger;

    /// <summary>Shared, thread-safe Polly pipeline for Standard endpoints.</summary>
    private readonly ResiliencePipeline _standardPipeline;

    /// <summary>Shared, thread-safe Polly pipeline for NonCritical endpoints.</summary>
    private readonly ResiliencePipeline _nonCriticalPipeline;

    // ── Constructor ───────────────────────────────────────────────────────────

    public EndpointCircuitBreakerMiddleware(
        RequestDelegate                             next,
        IOptions<CircuitBreakerOptions>             options,
        ILogger<EndpointCircuitBreakerMiddleware>   logger)
    {
        _next   = next;
        _config = options.Value;
        _logger = logger;

        _standardPipeline    = BuildPipeline("Standard",    _config.StandardFailureThreshold,    _config.StandardBreakDurationSeconds);
        _nonCriticalPipeline = BuildPipeline("NonCritical", _config.NonCriticalFailureThreshold, _config.NonCriticalBreakDurationSeconds);
    }

    // ── Middleware entry ──────────────────────────────────────────────────────

    public async Task InvokeAsync(HttpContext context)
    {
        var path           = context.Request.Path.Value ?? string.Empty;
        var classification = Classify(path);

        if (classification == EndpointClassification.Critical)
        {
            await _next(context);
            return;
        }

        var pipeline      = classification == EndpointClassification.NonCritical
                            ? _nonCriticalPipeline
                            : _standardPipeline;
        var breakDuration = classification == EndpointClassification.NonCritical
                            ? _config.NonCriticalBreakDurationSeconds
                            : _config.StandardBreakDurationSeconds;

        try
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await _next(context);
            }, context.RequestAborted);
        }
        catch (BrokenCircuitException)
        {
            // Catches both BrokenCircuitException and its subclass IsolatedCircuitException.
            await WriteCircuitOpenResponseAsync(context, path, classification, breakDuration);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private EndpointClassification Classify(string path)
    {
        foreach (var criticalPrefix in _config.CriticalPaths)
        {
            if (path.StartsWith(criticalPrefix, StringComparison.OrdinalIgnoreCase))
                return EndpointClassification.Critical;
        }

        foreach (var nonCriticalPrefix in NonCriticalPrefixes)
        {
            if (path.StartsWith(nonCriticalPrefix, StringComparison.OrdinalIgnoreCase))
                return EndpointClassification.NonCritical;
        }

        return EndpointClassification.Standard;
    }

    private ResiliencePipeline BuildPipeline(
        string pipelineName,
        int    failureThreshold,
        int    breakDurationSeconds)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // FailureRatio=1.0 + MinimumThroughput = failureThreshold approximates
                // "break after N consecutive failures" in a sliding sampling window.
                FailureRatio      = 0.8,
                SamplingDuration  = TimeSpan.FromSeconds(30),
                MinimumThroughput = failureThreshold,
                BreakDuration     = TimeSpan.FromSeconds(breakDurationSeconds),

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex =>
                        ex is not OperationCanceledException &&
                        ex is not BrokenCircuitException    &&
                        ex is not IsolatedCircuitException),

                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "Circuit breaker {Pipeline} OPENED — endpoint protection active. " +
                        "BreakDuration={BreakDuration}s. Reason: {Outcome}",
                        pipelineName, breakDurationSeconds,
                        args.Outcome.Exception?.GetType().Name ?? "unknown");
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker {Pipeline} CLOSED — service recovered.", pipelineName);
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker {Pipeline} HALF-OPEN — probing service health.",
                        pipelineName);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    private async Task WriteCircuitOpenResponseAsync(
        HttpContext            context,
        string                 path,
        EndpointClassification classification,
        int                    breakDurationSeconds)
    {
        _logger.LogWarning(
            "EndpointCircuitBreakerMiddleware: {Classification} circuit open — returning HTTP 503. " +
            "Path={Path} RetryAfter={RetryAfterSeconds}s",
            classification, path, breakDurationSeconds);

        context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = breakDurationSeconds.ToString();

        var body =
            $"{{\"error\":\"Service temporarily throttled to protect critical operations\"," +
            $"\"retryAfterSeconds\":{breakDurationSeconds}}}";

        await context.Response.WriteAsync(body, context.RequestAborted);
    }
}
