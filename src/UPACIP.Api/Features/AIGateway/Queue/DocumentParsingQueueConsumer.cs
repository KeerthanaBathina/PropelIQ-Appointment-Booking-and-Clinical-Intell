using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Resilience;

namespace UPACIP.Api.Features.AIGateway.Queue;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis document parsing queue
/// and dispatches jobs to <see cref="AIProviderFallbackHandler"/> with configurable
/// concurrency (US_067 TASK_003, AC-4, TR-012, NFR-029).
///
/// Dispatch loop per tick:
/// <list type="number">
///   <item>LPOP one message from the Redis FIFO queue.</item>
///   <item>Acquire a <see cref="SemaphoreSlim"/> slot — enforces the concurrency ceiling.</item>
///   <item>Process the job on a background task via <see cref="AIProviderFallbackHandler"/>.</item>
///   <item>On success: log completion latency (now − EnqueuedAt).</item>
///   <item>
///     On failure: increment <see cref="QueueMessage.RetryCount"/>, re-enqueue if under
///     <see cref="QueueOptions.MaxRetries"/>, otherwise route to <see cref="DeadLetterHandler"/>.
///   </item>
/// </list>
///
/// Background workers run in a system context — they call <see cref="AIProviderFallbackHandler"/>
/// directly, bypassing the HTTP-layer authentication step which is not applicable to queued jobs.
///
/// A periodic monitoring timer (default: every 30 s) logs queue depth, dead-letter depth,
/// and consumer throughput for operational observability.
///
/// Graceful shutdown: <see cref="StopAsync"/> waits up to
/// <see cref="QueueOptions.ShutdownDrainTimeoutSeconds"/> for in-flight jobs to complete.
/// </summary>
public sealed class DocumentParsingQueueConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConnectionMultiplexer             _redis;
    private readonly QueueOptions                       _options;
    private readonly AIProviderFallbackHandler          _fallbackHandler;
    private readonly DeadLetterHandler                  _deadLetterHandler;
    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly ILogger<DocumentParsingQueueConsumer> _logger;

    // Concurrency gate — limits simultaneous active jobs (AC-4).
    private SemaphoreSlim _semaphore = null!;

    // Throughput counters for monitoring log.
    private int _processedCount;
    private int _failedCount;
    private long _totalProcessingMs;

    public DocumentParsingQueueConsumer(
        IConnectionMultiplexer                   redis,
        IOptions<QueueOptions>                   options,
        AIProviderFallbackHandler                fallbackHandler,
        DeadLetterHandler                        deadLetterHandler,
        IServiceScopeFactory                     scopeFactory,
        ILogger<DocumentParsingQueueConsumer>    logger)
    {
        _redis             = redis;
        _options           = options.Value;
        _fallbackHandler   = fallbackHandler;
        _deadLetterHandler = deadLetterHandler;
        _scopeFactory      = scopeFactory;
        _logger            = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _semaphore = new SemaphoreSlim(
            _options.MaxConcurrentWorkers,
            _options.MaxConcurrentWorkers);

        _logger.LogInformation(
            "AI Queue consumer started. MaxConcurrentWorkers={Workers} " +
            "PollingIntervalMs={Interval} QueueKey={Key}",
            _options.MaxConcurrentWorkers,
            _options.PollingIntervalMs,
            _options.QueueKey);

        // Start background monitoring timer.
        using var monitoringTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.MonitoringIntervalSeconds));
        var monitoringTask = RunMonitoringLoopAsync(monitoringTimer, stoppingToken);

        var pollInterval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
        using var pollingTimer = new PeriodicTimer(pollInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await pollingTimer.WaitForNextTickAsync(stoppingToken))
        {
            await DrainAvailableSlotsAsync(stoppingToken);
        }

        // Wait for the monitoring loop to finish.
        await monitoringTask;

        _logger.LogInformation(
            "AI Queue consumer stopped. ProcessedTotal={Processed} FailedTotal={Failed}",
            _processedCount, _failedCount);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Signals in-flight jobs via the <paramref name="cancellationToken"/> and waits up to
    /// <see cref="QueueOptions.ShutdownDrainTimeoutSeconds"/> for all slots to be released.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AI Queue consumer: graceful shutdown initiated. " +
            "DrainTimeoutSeconds={Timeout} ActiveSlots={Active}",
            _options.ShutdownDrainTimeoutSeconds,
            _options.MaxConcurrentWorkers - (_semaphore?.CurrentCount ?? _options.MaxConcurrentWorkers));

        await base.StopAsync(cancellationToken);

        // Wait for all semaphore slots to be returned (all in-flight jobs complete).
        if (_semaphore is not null)
        {
            var drainDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(_options.ShutdownDrainTimeoutSeconds)).Token);

            try
            {
                while (_semaphore.CurrentCount < _options.MaxConcurrentWorkers)
                {
                    await Task.Delay(50, drainDeadline.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "AI Queue consumer: drain timeout exceeded. Some jobs may still be in-flight. " +
                    "RemainingSlots={Slots}",
                    _semaphore.CurrentCount);
            }
        }

        var db         = _redis.GetDatabase();
        var queueDepth = await db.ListLengthAsync(_options.QueueKey);

        _logger.LogInformation(
            "AI Queue consumer: shutdown complete. RemainingQueueDepth={Depth}",
            queueDepth);
    }

    // ── Private implementation ────────────────────────────────────────────────

    private async Task DrainAvailableSlotsAsync(CancellationToken stoppingToken)
    {
        // Graceful degradation — if Redis is unavailable, skip this tick (NFR-023).
        if (!_redis.IsConnected)
        {
            _logger.LogDebug("AI Queue consumer: Redis not connected. Skipping drain tick.");
            return;
        }

        IDatabase db;
        try
        {
            db = _redis.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI Queue consumer: Redis unavailable. Skipping drain tick.");
            return;
        }

        // Pop as many jobs as there are available worker slots.
        while (_semaphore.CurrentCount > 0 && !stoppingToken.IsCancellationRequested)
        {
            RedisValue rawValue;
            try
            {
                rawValue = await db.ListLeftPopAsync(_options.QueueKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Queue consumer: Redis error during queue pop. Skipping drain tick.");
                return;
            }

            if (!rawValue.HasValue)
                break; // Queue is empty — stop popping until next tick.

            QueueMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<QueueMessage>(rawValue!, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "AI Queue consumer: failed to deserialize queue message. Payload discarded.");
                continue;
            }

            if (message is null)
                continue;

            // Acquire a concurrency slot before spawning the task.
            await _semaphore.WaitAsync(stoppingToken);

            // Fire-and-forget with slot release in finally.
            _ = ProcessJobAsync(message, stoppingToken)
                .ContinueWith(_ => _semaphore.Release(), TaskScheduler.Default);
        }
    }

    private async Task ProcessJobAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        var processingStart = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "AI Queue: processing job. JobId={JobId} DocumentId={DocumentId} " +
            "RetryCount={Retry} CorrelationId={CorrelationId}",
            message.JobId, message.DocumentId, message.RetryCount, message.CorrelationId);

        try
        {
            var response = await _fallbackHandler.ExecuteAsync(
                message.Request, stoppingToken);

            var latencyMs = (long)(DateTimeOffset.UtcNow - message.EnqueuedAt).TotalMilliseconds;

            if (response.Success)
            {
                Interlocked.Increment(ref _processedCount);
                Interlocked.Add(ref _totalProcessingMs, latencyMs);

                _logger.LogInformation(
                    "AI Queue: job completed successfully. JobId={JobId} DocumentId={DocumentId} " +
                    "Provider={Provider} TotalLatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                    message.JobId, message.DocumentId,
                    response.ProviderName, latencyMs, message.CorrelationId);
            }
            else
            {
                await HandleJobFailureAsync(
                    message,
                    response.ErrorMessage ?? "Provider returned non-success response.",
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown in progress — re-enqueue the job so it is not lost.
            _logger.LogWarning(
                "AI Queue: job interrupted by shutdown, re-enqueuing. " +
                "JobId={JobId} DocumentId={DocumentId}",
                message.JobId, message.DocumentId);

            var db = _redis.GetDatabase();
            var payload = System.Text.Json.JsonSerializer.Serialize(message, JsonOptions);
            await db.ListLeftPushAsync(_options.QueueKey, payload); // Push back to front (priority)
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI Queue: unhandled exception processing job. " +
                "JobId={JobId} DocumentId={DocumentId} CorrelationId={CorrelationId}",
                message.JobId, message.DocumentId, message.CorrelationId);

            await HandleJobFailureAsync(message, ex.Message, stoppingToken);
        }
    }

    private async Task HandleJobFailureAsync(
        QueueMessage      message,
        string            failureReason,
        CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _failedCount);
        message.RetryCount++;

        if (!message.IsExhausted)
        {
            // Re-enqueue for retry — push to the tail (RPUSH) to maintain FIFO fairness.
            var db      = _redis.GetDatabase();
            var payload = JsonSerializer.Serialize(message, JsonOptions);
            await db.ListRightPushAsync(_options.QueueKey, payload);

            _logger.LogWarning(
                "AI Queue: job failed, re-enqueued for retry. " +
                "JobId={JobId} DocumentId={DocumentId} RetryCount={Retry}/{MaxRetries} " +
                "Reason={Reason} CorrelationId={CorrelationId}",
                message.JobId, message.DocumentId,
                message.RetryCount, message.MaxRetries,
                failureReason, message.CorrelationId);
        }
        else
        {
            // Retry budget exhausted — route to dead-letter queue.
            await _deadLetterHandler.HandleAsync(message, failureReason, stoppingToken);
        }
    }

    private async Task RunMonitoringLoopAsync(
        PeriodicTimer     timer,
        CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(stoppingToken))
            {
                var db             = _redis.GetDatabase();
                var queueDepth     = await db.ListLengthAsync(_options.QueueKey);
                var dlDepth        = await _deadLetterHandler.GetDepthAsync(stoppingToken);
                var processed      = _processedCount;
                var failed         = _failedCount;
                var avgLatencyMs   = processed > 0
                    ? (double)_totalProcessingMs / processed
                    : 0d;

                var saturationPct  = _options.MaxQueueDepth > 0
                    ? (double)queueDepth / _options.MaxQueueDepth * 100
                    : 0d;

                _logger.LogInformation(
                    "AI Queue monitor: QueueDepth={Depth} DeadLetterDepth={DLDepth} " +
                    "SaturationPct={Saturation:F1}% ProcessedTotal={Processed} " +
                    "FailedTotal={Failed} AvgLatencyMs={AvgLatency:F1}",
                    queueDepth, dlDepth, saturationPct,
                    processed, failed, avgLatencyMs);

                if (saturationPct >= 80)
                {
                    _logger.LogWarning(
                        "AI Queue monitor: high saturation warning. " +
                        "QueueDepth={Depth} MaxQueueDepth={Max} SaturationPct={Saturation:F1}%",
                        queueDepth, _options.MaxQueueDepth, saturationPct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — expected.
        }
    }
}
