using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis FIFO parsing queue and
/// dispatches jobs to <see cref="IDocumentParserWorker"/> with configurable concurrency
/// and Polly-backed retry (US_039 AC-2, AC-4, AC-5, EC-2).
///
/// Dispatch loop:
/// <list type="number">
///   <item>Tick every <see cref="DocumentParsingDispatcherSettings.PollingIntervalSeconds"/> seconds.</item>
///   <item>LPOP up to (<see cref="DocumentParsingDispatcherSettings.MaxConcurrentJobs"/> − active) jobs.</item>
///   <item>Acquire a <see cref="SemaphoreSlim"/> slot per job — enforces the concurrency ceiling (EC-2).</item>
///   <item>
///     Run each job through a Polly <see cref="ResiliencePipeline"/> with exponential-backoff
///     retry (up to <see cref="DocumentParsingDispatcherSettings.MaxRetryAttempts"/> attempts).
///   </item>
///   <item>
///     On permanent failure (all retries exhausted), mark the document <c>Failed</c> and log
///     structured telemetry (AC-5).
///   </item>
/// </list>
///
/// A fresh DI scope is created per job so scoped services (ApplicationDbContext,
/// IDocumentParserWorker) are isolated and correctly disposed after each parse run — matching
/// the WaitlistOfferProcessor and ReminderBatchWorker patterns.
/// </summary>
public sealed class DocumentParsingDispatcher : BackgroundService
{
    // ── Constants ─────────────────────────────────────────────────────────────────

    /// <summary>Redis list key — must match <see cref="DocumentParsingQueueService.QueueKey"/>.</summary>
    private const string QueueKey = DocumentParsingQueueService.QueueKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer                     _redis;
    private readonly IServiceScopeFactory                       _scopeFactory;
    private readonly DocumentParsingDispatcherSettings          _settings;
    private readonly ILogger<DocumentParsingDispatcher>         _logger;

    /// <summary>Concurrency gate — limits simultaneous active parsing jobs (EC-2).</summary>
    private SemaphoreSlim _semaphore = null!;

    /// <summary>Polly resilience pipeline with exponential-backoff retry (AC-4).</summary>
    private ResiliencePipeline _retryPipeline = null!;

    public DocumentParsingDispatcher(
        IConnectionMultiplexer                  redis,
        IServiceScopeFactory                    scopeFactory,
        IOptions<DocumentParsingDispatcherSettings> settings,
        ILogger<DocumentParsingDispatcher>      logger)
    {
        _redis        = redis;
        _scopeFactory = scopeFactory;
        _settings     = settings.Value;
        _logger       = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _semaphore = new SemaphoreSlim(_settings.MaxConcurrentJobs, _settings.MaxConcurrentJobs);

        // Build Polly retry pipeline: exponential backoff 2^attempt seconds (AC-4).
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _settings.MaxRetryAttempts,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry          = args =>
                {
                    _logger.LogWarning(
                        "Parsing retry #{Attempt} after delay {Delay:g}. Exception={Message}",
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message);
                    return default;
                },
            })
            .Build();

        _logger.LogInformation(
            "DocumentParsingDispatcher started. MaxConcurrentJobs={MaxJobs} PollingInterval={Interval}s MaxRetries={Retries}",
            _settings.MaxConcurrentJobs,
            _settings.PollingIntervalSeconds,
            _settings.MaxRetryAttempts);

        var interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DrainQueueAsync(stoppingToken);
        }

        _logger.LogInformation("DocumentParsingDispatcher stopped.");
    }

    // ── Drain loop ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dequeues available jobs up to the available semaphore capacity and dispatches
    /// each on a background task (fire-and-forget within the host lifetime).
    /// </summary>
    private async Task DrainQueueAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Dequeue up to the number of free semaphore slots (never exceed ceiling).
            int available = _semaphore.CurrentCount;
            while (available > 0 && !ct.IsCancellationRequested)
            {
                RedisValue raw = await db.ListLeftPopAsync(QueueKey);
                if (raw.IsNullOrEmpty) break; // Queue is empty.

                DocumentParsingQueueJob? job;
                try
                {
                    job = JsonSerializer.Deserialize<DocumentParsingQueueJob>(raw!, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "DocumentParsingDispatcher: failed to deserialize job payload. Raw={Raw}", (string?)raw);
                    available--;
                    continue;
                }

                if (job is null)
                {
                    _logger.LogWarning("DocumentParsingDispatcher: null job deserialized, skipping.");
                    available--;
                    continue;
                }

                // Acquire semaphore before launching task so available count stays accurate.
                await _semaphore.WaitAsync(ct);
                available--;

                // Dispatch as a background task — exception handling happens inside.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(job, ct);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }, ct);
            }
        }
        catch (RedisException ex) when (!ct.IsCancellationRequested)
        {
            // Redis transient failure during polling — log and wait for next tick (EC-1 resilience).
            _logger.LogWarning(ex,
                "DocumentParsingDispatcher: Redis error during queue drain. Will retry on next tick.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — expected.
        }
    }

    // ── Job processing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single parsing job through the Polly retry pipeline.
    /// On permanent failure marks the document <c>Failed</c> (US_039 AC-5).
    /// </summary>
    private async Task ProcessJobAsync(DocumentParsingQueueJob job, CancellationToken ct)
    {
        _logger.LogInformation(
            "DocumentParsingDispatcher: processing job. DocumentId={DocumentId} Attempt={Attempt}",
            job.DocumentId, job.AttemptNumber);

        try
        {
            await _retryPipeline.ExecuteAsync(async token =>
            {
                using var scope  = _scopeFactory.CreateScope();
                var worker       = scope.ServiceProvider.GetRequiredService<IDocumentParserWorker>();
                await worker.ParseAsync(job.DocumentId, token);
            }, ct);

            _logger.LogInformation(
                "DocumentParsingDispatcher: job completed successfully. DocumentId={DocumentId}",
                job.DocumentId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down — re-queue the job so it is not lost across restarts.
            await RequeueJobAsync(job);
        }
        catch (Exception ex)
        {
            // All retry attempts exhausted — mark document as Failed (AC-5).
            _logger.LogError(ex,
                "DocumentParsingDispatcher: parsing permanently failed after {MaxRetries} retries. " +
                "DocumentId={DocumentId}",
                _settings.MaxRetryAttempts, job.DocumentId);

            await MarkDocumentFailedAsync(job.DocumentId);
        }
    }

    /// <summary>
    /// Re-pushes a job back to the queue tail when the host is shutting down mid-parse,
    /// preserving FIFO ordering for the next startup (AC-2 durability across restarts).
    /// </summary>
    private async Task RequeueJobAsync(DocumentParsingQueueJob job)
    {
        try
        {
            var jobJson = JsonSerializer.Serialize(job, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(QueueKey, jobJson);

            _logger.LogWarning(
                "DocumentParsingDispatcher: re-queued job due to host shutdown. DocumentId={DocumentId}",
                job.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingDispatcher: failed to re-queue job on shutdown. DocumentId={DocumentId}",
                job.DocumentId);
        }
    }

    /// <summary>
    /// Persists <c>ProcessingStatus.Failed</c> for the document after all retry attempts
    /// are exhausted (US_039 AC-5).
    /// </summary>
    private async Task MarkDocumentFailedAsync(Guid documentId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var document = await db.ClinicalDocuments.FindAsync(documentId);
            if (document is not null)
            {
                document.ProcessingStatus = ProcessingStatus.Failed;
                document.UpdatedAt        = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingDispatcher: failed to persist Failed status. DocumentId={DocumentId}",
                documentId);
        }
    }
}
