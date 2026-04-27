using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using UPACIP.Service.Rag.Embedding.Models;

namespace UPACIP.Service.Rag.Embedding;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis document ingestion queue
/// (<c>queue:document-ingestion</c>) and delegates each job to
/// <see cref="IEmbeddingGenerationService.IngestDocumentAsync"/> (US_076, Implementation Plan §7).
///
/// Dispatch loop per tick (5-second <see cref="PeriodicTimer"/>):
/// <list type="number">
///   <item>LPOP one <see cref="IngestionJobMessage"/> from the FIFO Redis list.</item>
///   <item>Create a DI scope and resolve <see cref="IEmbeddingGenerationService"/>.</item>
///   <item>Execute the job through the Polly circuit breaker (AIR-O04).</item>
///   <item>
///     On success: log completion.
///   </item>
///   <item>
///     On failure: if <see cref="IngestionJobMessage.RetryCount"/> &lt; MaxJobRetries,
///     RPUSH the job back (incremented retry count) for later processing (AIR-O08).
///     On permanent failure (≥ MaxJobRetries), route to the dead-letter queue
///     <c>queue:document-ingestion:dead</c> and log an error.
///   </item>
///   <item>
///     On <see cref="BrokenCircuitException"/> (circuit open): re-queue the job immediately
///     without incrementing its retry count, then pause for the circuit break duration
///     before the next tick.
///   </item>
/// </list>
///
/// Circuit breaker (AIR-O04): opens after 5 consecutive job failures; half-open after 30s.
/// When open, all dequeued jobs are re-queued and the worker logs a warning.
///
/// Processes one job at a time to respect OpenAI rate limits (Implementation Plan §7).
/// </summary>
public sealed class DocumentIngestionWorker : BackgroundService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    internal const string QueueKey         = "queue:document-ingestion";
    internal const string DeadLetterKey    = "queue:document-ingestion:dead";
    private  const int    MaxJobRetries    = 3;
    private  const int    PollingSeconds   = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer                     _redis;
    private readonly IServiceScopeFactory                       _scopeFactory;
    private readonly ILogger<DocumentIngestionWorker>           _logger;

    /// <summary>
    /// Singleton circuit breaker — accumulates failure state across all jobs
    /// so that a sustained outage trips the breaker after 5 consecutive failures (AIR-O04).
    /// Uses Polly V7 API for exact consecutive-failure semantics.
    /// </summary>
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    // ── Constructor ───────────────────────────────────────────────────────────

    public DocumentIngestionWorker(
        IConnectionMultiplexer              redis,
        IServiceScopeFactory                scopeFactory,
        ILogger<DocumentIngestionWorker>    logger)
    {
        _redis        = redis;
        _scopeFactory = scopeFactory;
        _logger       = logger;

        _circuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "DocumentIngestionWorker: embedding API circuit OPEN for {DurationSeconds}s. " +
                        "Requeuing jobs until circuit recovers.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("DocumentIngestionWorker: embedding API circuit CLOSED."),
                onHalfOpen: () =>
                    _logger.LogInformation("DocumentIngestionWorker: embedding API circuit HALF-OPEN."));
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DocumentIngestionWorker started. Queue={QueueKey} PollingSeconds={Seconds}",
            QueueKey, PollingSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollingSeconds));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessNextJobAsync(stoppingToken);
        }

        _logger.LogInformation("DocumentIngestionWorker stopped.");
    }

    // ── Job dispatch ──────────────────────────────────────────────────────────

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        var db  = _redis.GetDatabase();
        var raw = await db.ListLeftPopAsync(QueueKey);

        if (!raw.HasValue)
            return; // queue is empty

        IngestionJobMessage? job;
        try
        {
            job = JsonSerializer.Deserialize<IngestionJobMessage>(raw.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "DocumentIngestionWorker: failed to deserialise job message. Message moved to dead-letter queue.");
            await db.ListRightPushAsync(DeadLetterKey, raw);
            return;
        }

        if (job is null)
        {
            _logger.LogError(
                "DocumentIngestionWorker: null job after deserialisation. Message discarded.");
            return;
        }

        try
        {
            await _circuitBreaker.ExecuteAsync(async () =>
            {
                await using var scope    = _scopeFactory.CreateAsyncScope();
                var embeddingService     = scope.ServiceProvider
                    .GetRequiredService<IEmbeddingGenerationService>();

                await embeddingService.IngestDocumentAsync(job.Request, ct);

                _logger.LogInformation(
                    "DocumentIngestionWorker: job completed. SourceDocumentId={SourceDocumentId} " +
                    "SourceName={SourceName}",
                    job.Request.SourceDocumentId, job.Request.SourceName);
            });
        }
        catch (BrokenCircuitException)
        {
            // Circuit is open — re-queue WITHOUT incrementing RetryCount and wait for recovery.
            _logger.LogWarning(
                "DocumentIngestionWorker: circuit breaker open. Re-queuing job. " +
                "SourceDocumentId={SourceDocumentId}",
                job.Request.SourceDocumentId);

            var requeued = JsonSerializer.Serialize(job, JsonOptions);
            await db.ListRightPushAsync(QueueKey, requeued);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — re-queue so the job survives the restart.
            var requeued = JsonSerializer.Serialize(job, JsonOptions);
            await db.ListRightPushAsync(QueueKey, requeued);
        }
        catch (Exception ex)
        {
            var nextRetryCount = job.RetryCount + 1;

            if (nextRetryCount <= MaxJobRetries)
            {
                _logger.LogWarning(ex,
                    "DocumentIngestionWorker: job failed (attempt {Attempt}/{Max}). Re-queuing. " +
                    "SourceDocumentId={SourceDocumentId}",
                    nextRetryCount, MaxJobRetries, job.Request.SourceDocumentId);

                var retried = JsonSerializer.Serialize(
                    job with { RetryCount = nextRetryCount }, JsonOptions);
                await db.ListRightPushAsync(QueueKey, retried);
            }
            else
            {
                _logger.LogError(ex,
                    "DocumentIngestionWorker: job permanently failed after {Max} retries. " +
                    "Moving to dead-letter queue. SourceDocumentId={SourceDocumentId}",
                    MaxJobRetries, job.Request.SourceDocumentId);

                var dead = JsonSerializer.Serialize(job, JsonOptions);
                await db.ListRightPushAsync(DeadLetterKey, dead);
            }
        }
    }

    // ── Queue message schema ──────────────────────────────────────────────────

    /// <summary>
    /// FIFO queue message wrapper. Serialised to JSON with camelCase naming.
    /// </summary>
    internal sealed record IngestionJobMessage(IngestionRequest Request, int RetryCount = 0);
}
