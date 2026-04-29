using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using UPACIP.Service.Logging;
using UPACIP.Service.Resilience.Models;

namespace UPACIP.Service.Resilience;

/// <summary>
/// Builds the three named Polly V8 resilience pipelines for UPACIP (US_095, AC-2,
/// NFR-032, NFR-023):
///
///   - <see cref="PipelineNames.DatabaseRetry"/>      — DB transient retry, 3 attempts, [1s, 5s, 15s].
///   - <see cref="PipelineNames.HttpRetry"/>           — HTTP transient retry + circuit breaker.
///   - <see cref="PipelineNames.ExternalServiceRetry"/>— IO/socket retry + circuit breaker.
///
/// Each pipeline follows the existing project pattern (see <c>ExternalServiceResilienceProvider</c>):
/// outermost CircuitBreaker → middle Retry → innermost Timeout (HTTP/External pipelines only).
/// The database pipeline omits circuit breaker since connection pooling handles DB recovery.
///
/// Retry logging emits structured Serilog events with attempt number, delay, exception type,
/// and correlation ID for Seq dashboards and alerts (AC-4, NFR-035).
/// </summary>
public static class ResiliencePipelineConfigurator
{
    /// <summary>Named pipeline identifiers for use with the resilience pipeline registry.</summary>
    public static class PipelineNames
    {
        public const string DatabaseRetry       = "database-retry";
        public const string HttpRetry           = "http-retry";
        public const string ExternalServiceRetry = "external-service-retry";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pipeline builders
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="PipelineNames.DatabaseRetry"/> pipeline (AC-2, NFR-032).
    ///
    /// Strategy stack (innermost first):
    ///   Retry — 3 attempts with fixed delays [1s, 5s, 15s]; only transient DB exceptions.
    ///
    /// No circuit breaker: Npgsql connection pooling handles pool exhaustion internally.
    /// The EF Core <c>EnableRetryOnFailure</c> execution strategy provides an additional
    /// database-level retry layer for transactional operations.
    /// </summary>
    public static ResiliencePipeline BuildDatabaseRetryPipeline(
        ResilienceOptions          options,
        ITransientFaultClassifier  classifier,
        ICorrelationIdAccessor     correlationIdAccessor,
        ILogger                    logger)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,

                // Fixed delays [1s, 5s, 15s] as specified in AC-2.
                DelayGenerator = args =>
                {
                    var delay = options.GetRetryDelay(args.AttemptNumber);
                    return new ValueTask<TimeSpan?>(delay);
                },

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => classifier.IsTransientDatabaseException(ex)),

                OnRetry = args =>
                {
                    if (options.EnableRetryLogging)
                    {
                        logger.LogWarning(
                            "DB_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={DelayS:F1}s, " +
                            "ExceptionType={ExceptionType}, Message={Message}, CorrelationId={CorrelationId}",
                            args.AttemptNumber + 1,
                            options.MaxRetries,
                            args.RetryDelay.TotalSeconds,
                            args.Outcome.Exception?.GetType().Name ?? "unknown",
                            args.Outcome.Exception?.Message ?? string.Empty,
                            correlationIdAccessor.CorrelationId);
                    }
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Builds the <see cref="PipelineNames.HttpRetry"/> pipeline (AC-2, NFR-023).
    ///
    /// Strategy stack (outermost → innermost):
    ///   CircuitBreaker — opens after <see cref="ResilienceOptions.CircuitBreakerFailureThreshold"/>
    ///                    consecutive failures; breaks for <see cref="ResilienceOptions.CircuitBreakerBreakDurationSeconds"/>.
    ///   Retry          — 3 attempts, fixed delays [1s, 5s, 15s]; skips <see cref="BrokenCircuitException"/>.
    ///   Timeout        — <see cref="ResilienceOptions.HttpTimeoutSeconds"/> per attempt.
    /// </summary>
    public static ResiliencePipeline BuildHttpRetryPipeline(
        ResilienceOptions          options,
        ITransientFaultClassifier  classifier,
        ICorrelationIdAccessor     correlationIdAccessor,
        ILogger                    logger)
    {
        return new ResiliencePipelineBuilder()

            // ── Circuit Breaker (outermost) ───────────────────────────────────
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = 1.0,
                MinimumThroughput = options.CircuitBreakerFailureThreshold,
                SamplingDuration  = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration     = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException
                                            && classifier.IsTransientHttpException(ex)),

                OnOpened = args =>
                {
                    logger.LogWarning(
                        "CIRCUIT_BREAKER_OPENED: Pipeline={Pipeline}, FailureCount={Threshold}, " +
                        "BreakDuration={BreakSecs}s, Reason={Reason}",
                        PipelineNames.HttpRetry,
                        options.CircuitBreakerFailureThreshold,
                        options.CircuitBreakerBreakDurationSeconds,
                        args.Outcome.Exception?.Message ?? "failure ratio exceeded");
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    logger.LogInformation(
                        "CIRCUIT_BREAKER_HALF_OPEN: Pipeline={Pipeline} probing service recovery.",
                        PipelineNames.HttpRetry);
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    logger.LogInformation(
                        "CIRCUIT_BREAKER_CLOSED: Pipeline={Pipeline} service recovered.",
                        PipelineNames.HttpRetry);
                    return ValueTask.CompletedTask;
                },
            })

            // ── Retry (middle layer) ──────────────────────────────────────────
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,

                DelayGenerator = args =>
                {
                    var delay = options.GetRetryDelay(args.AttemptNumber);
                    return new ValueTask<TimeSpan?>(delay);
                },

                // BrokenCircuitException must never be retried — it escapes to the
                // outer circuit breaker strategy which is responsible for it.
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException
                                            && classifier.IsTransientHttpException(ex)),

                OnRetry = args =>
                {
                    if (options.EnableRetryLogging)
                    {
                        logger.LogWarning(
                            "HTTP_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={DelayS:F1}s, " +
                            "ExceptionType={ExceptionType}, CorrelationId={CorrelationId}",
                            args.AttemptNumber + 1,
                            options.MaxRetries,
                            args.RetryDelay.TotalSeconds,
                            args.Outcome.Exception?.GetType().Name ?? "unknown",
                            correlationIdAccessor.CorrelationId);
                    }
                    return ValueTask.CompletedTask;
                },
            })

            // ── Timeout (innermost — per-attempt) ─────────────────────────────
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds),
            })

            .Build();
    }

    /// <summary>
    /// Builds the <see cref="PipelineNames.ExternalServiceRetry"/> pipeline for non-HTTP
    /// external integrations: file system, SMTP direct, socket connections (AC-2, NFR-023).
    ///
    /// Strategy stack (outermost → innermost):
    ///   CircuitBreaker — same thresholds as HttpRetryPipeline.
    ///   Retry          — 3 attempts, fixed delays [1s, 5s, 15s].
    ///   Timeout        — <see cref="ResilienceOptions.HttpTimeoutSeconds"/> per attempt.
    /// </summary>
    public static ResiliencePipeline BuildExternalServiceRetryPipeline(
        ResilienceOptions          options,
        ITransientFaultClassifier  classifier,
        ICorrelationIdAccessor     correlationIdAccessor,
        ILogger                    logger)
    {
        return new ResiliencePipelineBuilder()

            // ── Circuit Breaker (outermost) ───────────────────────────────────
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = 1.0,
                MinimumThroughput = options.CircuitBreakerFailureThreshold,
                SamplingDuration  = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration     = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException
                                            && classifier.IsTransient(ex)),

                OnOpened = args =>
                {
                    logger.LogWarning(
                        "CIRCUIT_BREAKER_OPENED: Pipeline={Pipeline}, BreakDuration={BreakSecs}s, " +
                        "Reason={Reason}",
                        PipelineNames.ExternalServiceRetry,
                        options.CircuitBreakerBreakDurationSeconds,
                        args.Outcome.Exception?.Message ?? "failure ratio exceeded");
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    logger.LogInformation(
                        "CIRCUIT_BREAKER_HALF_OPEN: Pipeline={Pipeline} probing service recovery.",
                        PipelineNames.ExternalServiceRetry);
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    logger.LogInformation(
                        "CIRCUIT_BREAKER_CLOSED: Pipeline={Pipeline} service recovered.",
                        PipelineNames.ExternalServiceRetry);
                    return ValueTask.CompletedTask;
                },
            })

            // ── Retry (middle layer) ──────────────────────────────────────────
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,

                DelayGenerator = args =>
                {
                    var delay = options.GetRetryDelay(args.AttemptNumber);
                    return new ValueTask<TimeSpan?>(delay);
                },

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException
                                            && classifier.IsTransient(ex)),

                OnRetry = args =>
                {
                    if (options.EnableRetryLogging)
                    {
                        logger.LogWarning(
                            "SERVICE_RETRY: Attempt={Attempt}/{MaxRetries}, Delay={DelayS:F1}s, " +
                            "ExceptionType={ExceptionType}, CorrelationId={CorrelationId}",
                            args.AttemptNumber + 1,
                            options.MaxRetries,
                            args.RetryDelay.TotalSeconds,
                            args.Outcome.Exception?.GetType().Name ?? "unknown",
                            correlationIdAccessor.CorrelationId);
                    }
                    return ValueTask.CompletedTask;
                },
            })

            // ── Timeout (innermost — per-attempt) ─────────────────────────────
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds),
            })

            .Build();
    }
}
