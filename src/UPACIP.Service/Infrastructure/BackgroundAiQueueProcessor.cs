using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.Service.Documents;
using UPACIP.Service.Infrastructure.Models;
using UPACIP.Service.Performance;

namespace UPACIP.Service.Infrastructure;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis AI workload queue
/// (<c>ai:workload:queue</c>) asynchronously, ensuring AI inference jobs never block
/// HTTP request threads (US_082 task_001, AC-3).
///
/// <para>
/// <b>Queue key:</b> <c>ai:workload:queue</c> — a Redis FIFO list containing serialised
/// <see cref="AiWorkloadJob"/> payloads pushed by REST endpoints at enqueue time.
/// </para>
///
/// <para>
/// <b>Concurrency:</b> A <see cref="SemaphoreSlim"/> limits simultaneous jobs to
/// <c>Concurrency:AiQueueConcurrency</c> (default 10). Each job runs on a thread-pool
/// thread via <c>Task.Run</c>; the drain loop stays unblocked.
/// </para>
///
/// <para>
/// <b>Back-pressure:</b> When the queue depth exceeds
/// <c>Concurrency:AiQueueBackPressureThreshold</c>, <see cref="IsBackPressureActive"/>
/// returns <see langword="true"/>. Upstream REST endpoints should check this flag before
/// enqueuing and return HTTP 429 if active.
/// </para>
///
/// <para>
/// <b>Job routing:</b> Jobs with <c>JobType == "DocumentParsing"</c> are dispatched to
/// <see cref="IDocumentParserWorker"/>. Unknown job types are logged and discarded.
/// </para>
///
/// <para>Singleton lifetime — thread-safe back-pressure flag via Interlocked.</para>
/// </summary>
public sealed class BackgroundAiQueueProcessor : BackgroundService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Redis list key for the AI workload queue.</summary>
    public const string QueueKey = "ai:workload:queue";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer                    _redis;
    private readonly IServiceScopeFactory                      _scopeFactory;
    private readonly ConcurrencyOptions                        _options;
    private readonly IPerformanceTracker                       _tracker;
    private readonly ILogger<BackgroundAiQueueProcessor>       _logger;

    private SemaphoreSlim _semaphore = null!;

    // 1 = back-pressure active; 0 = normal.
    private int _backPressureActive;

    // ── Constructor ───────────────────────────────────────────────────────────

    public BackgroundAiQueueProcessor(
        IConnectionMultiplexer               redis,
        IServiceScopeFactory                 scopeFactory,
        IOptions<ConcurrencyOptions>         options,
        IPerformanceTracker                  tracker,
        ILogger<BackgroundAiQueueProcessor>  logger)
    {
        _redis        = redis;
        _scopeFactory = scopeFactory;
        _options      = options.Value;
        _tracker      = tracker;
        _logger       = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the AI workload queue depth exceeds the
    /// configured back-pressure threshold. REST endpoints should return HTTP 429 when active.
    /// </summary>
    public bool IsBackPressureActive => Interlocked.CompareExchange(ref _backPressureActive, 0, 0) == 1;

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Min(_options.AiQueueConcurrency, 20);
        _semaphore = new SemaphoreSlim(concurrency, concurrency);

        _logger.LogInformation(
            "BackgroundAiQueueProcessor started. QueueKey={QueueKey} Concurrency={Concurrency} " +
            "BackPressureThreshold={Threshold}",
            QueueKey, concurrency, _options.AiQueueBackPressureThreshold);

        using var timer = new PeriodicTimer(PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DrainQueueAsync(stoppingToken);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Check queue depth and update back-pressure signal.
            var depth = await db.ListLengthAsync(QueueKey);
            _tracker.RecordLatency("ai.queue_depth", (long)depth);

            var shouldApplyBackPressure = depth > _options.AiQueueBackPressureThreshold;
            var wasActive               = Interlocked.Exchange(
                ref _backPressureActive, shouldApplyBackPressure ? 1 : 0);

            if (shouldApplyBackPressure && wasActive == 0)
            {
                _logger.LogWarning(
                    "BackgroundAiQueueProcessor: back-pressure ACTIVATED. " +
                    "QueueDepth={Depth} Threshold={Threshold}",
                    depth, _options.AiQueueBackPressureThreshold);
            }
            else if (!shouldApplyBackPressure && wasActive == 1)
            {
                _logger.LogInformation(
                    "BackgroundAiQueueProcessor: back-pressure RELEASED. QueueDepth={Depth}", depth);
            }

            // Dequeue up to the number of available semaphore slots.
            var available = _semaphore.CurrentCount;
            while (available > 0 && !ct.IsCancellationRequested)
            {
                var raw = await db.ListLeftPopAsync(QueueKey);
                if (raw.IsNullOrEmpty) break;

                AiWorkloadJob? job;
                try
                {
                    job = JsonSerializer.Deserialize<AiWorkloadJob>(raw!, JsonOpts);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex,
                        "BackgroundAiQueueProcessor: failed to deserialize job payload. Discarding.");
                    available--;
                    continue;
                }

                if (job is null)
                {
                    available--;
                    continue;
                }

                await _semaphore.WaitAsync(ct);
                available--;

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
        catch (StackExchange.Redis.RedisException ex)
        {
            _logger.LogWarning(ex,
                "BackgroundAiQueueProcessor: Redis error during queue drain. Retrying on next tick.");
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ProcessJobAsync(AiWorkloadJob job, CancellationToken ct)
    {
        _logger.LogDebug(
            "BackgroundAiQueueProcessor: processing job. JobType={JobType} DocumentId={DocumentId}",
            job.JobType, job.DocumentId);

        try
        {
            if (string.Equals(job.JobType, "DocumentParsing", StringComparison.OrdinalIgnoreCase))
            {
                using var scope  = _scopeFactory.CreateScope();
                var worker       = scope.ServiceProvider.GetRequiredService<IDocumentParserWorker>();
                await worker.ParseAsync(job.DocumentId, ct);

                _logger.LogInformation(
                    "BackgroundAiQueueProcessor: DocumentParsing job completed. DocumentId={DocumentId}",
                    job.DocumentId);
            }
            else
            {
                _logger.LogWarning(
                    "BackgroundAiQueueProcessor: unknown job type '{JobType}'. Discarding job.",
                    job.JobType);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "BackgroundAiQueueProcessor: job failed. JobType={JobType} DocumentId={DocumentId}",
                job.JobType, job.DocumentId);
        }
    }
}

/// <summary>AI workload job payload stored in the Redis queue.</summary>
public sealed class AiWorkloadJob
{
    /// <summary>Discriminator: <c>"DocumentParsing"</c> or future job types.</summary>
    public string JobType    { get; set; } = string.Empty;

    /// <summary>Primary key of the target resource (e.g., clinical_documents row).</summary>
    public Guid   DocumentId { get; set; }

    /// <summary>UTC timestamp when the job was enqueued (for age/SLA tracking).</summary>
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
}
