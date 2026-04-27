using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Audit;

/// <summary>
/// <see cref="BackgroundService"/> that drains the Redis audit failover queue and flushes
/// entries to PostgreSQL once DB connectivity restores (US_064 edge case, FR-093, NFR-012).
///
/// Flush loop:
/// <list type="number">
///   <item>Check queue depth via <see cref="IAuditQueueService.GetQueueDepthAsync"/>.</item>
///   <item>If depth &gt; 0, dequeue in batches of <see cref="AuditQueueSettings.FlushBatchSize"/>.</item>
///   <item>
///     For each entry: check for duplicate <c>LogId</c> (idempotent — NFR-034), then
///     insert and <c>SaveChangesAsync</c> wrapped in a Polly retry + circuit breaker.
///   </item>
///   <item>On batch DB failure, re-enqueue all un-flushed entries back to Redis for next cycle.</item>
///   <item>Delay between iterations — short when queue active, long when idle.</item>
/// </list>
///
/// Polly resilience (NFR-032):
///   - Retry: 3 attempts with exponential backoff (1 s, 2 s, 4 s).
///   - Circuit breaker: opens after 5 consecutive DB failures; stays open for 30 s.
///   - When circuit is open the worker skips the flush and waits the idle interval.
///
/// Scoped dependencies (<see cref="ApplicationDbContext"/>) are resolved per batch iteration
/// via <see cref="IServiceScopeFactory"/> — same pattern used by <c>ConsolidationWorker</c>.
///
/// PII (IpAddress, UserAgent) in queue entries is used to reconstruct the full
/// <see cref="AuditLog"/> entity but MUST NOT appear in structured application logs (NFR-017).
/// </summary>
public sealed class AuditQueueFlushWorker : BackgroundService
{
    private readonly IAuditQueueService               _queue;
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly AuditQueueSettings               _settings;
    private readonly ILogger<AuditQueueFlushWorker>   _logger;

    // Polly policies — built once in constructor so circuit-breaker state is shared
    // across all flush iterations (Singleton worker lifetime).
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
    private readonly IAsyncPolicy              _resilience;

    public AuditQueueFlushWorker(
        IAuditQueueService               queue,
        IServiceScopeFactory             scopeFactory,
        IOptions<AuditQueueSettings>     settings,
        ILogger<AuditQueueFlushWorker>   logger)
    {
        _queue        = queue;
        _scopeFactory = scopeFactory;
        _settings     = settings.Value;
        _logger       = logger;

        // ── Polly circuit breaker (NFR-032) ──────────────────────────────────────────────────
        _circuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: _settings.CircuitBreakerFailureThreshold,
                durationOfBreak:                 TimeSpan.FromSeconds(_settings.CircuitBreakerDurationSeconds),
                onBreak: (ex, duration) =>
                    _logger.LogWarning(
                        "AuditQueueFlushWorker: circuit breaker OPEN for {DurationSeconds}s — DB unavailable.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("AuditQueueFlushWorker: circuit breaker CLOSED — DB recovered."),
                onHalfOpen: () =>
                    _logger.LogInformation("AuditQueueFlushWorker: circuit breaker HALF-OPEN — probing DB."));

        // ── Polly retry with exponential backoff (NFR-032) ───────────────────────────────────
        var retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException
                                  && ex is not BrokenCircuitException)
            .WaitAndRetryAsync(
                retryCount: _settings.MaxRetryAttempts,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(
                        "AuditQueueFlushWorker: DB write retry {Attempt}/{Max} after {Delay}ms — {ExType}.",
                        attempt, _settings.MaxRetryAttempts, (int)delay.TotalMilliseconds,
                        ex.GetType().Name));

        // Retry is the outer policy; circuit breaker is the inner policy (standard Polly wrap).
        _resilience = retryPolicy.WrapAsync(_circuitBreaker);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AuditQueueFlushWorker: started. FlushBatch={Batch} FlushInterval={FlushMs}ms IdleInterval={IdleMs}ms.",
            _settings.FlushBatchSize, _settings.FlushIntervalMs, _settings.IdleIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            // ── Check circuit breaker state ───────────────────────────────────────────────────
            if (_circuitBreaker.CircuitState == CircuitState.Open)
            {
                _logger.LogWarning(
                    "AuditQueueFlushWorker: circuit breaker OPEN — skipping flush, waiting {IdleMs}ms.",
                    _settings.IdleIntervalMs);
                await Task.Delay(_settings.IdleIntervalMs, stoppingToken);
                continue;
            }

            // ── Check queue depth ─────────────────────────────────────────────────────────────
            var depth = await _queue.GetQueueDepthAsync(stoppingToken);

            if (depth <= 0)
            {
                await Task.Delay(_settings.IdleIntervalMs, stoppingToken);
                continue;
            }

            _logger.LogInformation(
                "AuditQueueFlushWorker: {Depth} entries pending — flushing batch of {Batch}.",
                depth, _settings.FlushBatchSize);

            await FlushBatchAsync(stoppingToken);

            await Task.Delay(_settings.FlushIntervalMs, stoppingToken);
        }

        _logger.LogInformation("AuditQueueFlushWorker: stopping token signalled — exiting.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────

    private async Task FlushBatchAsync(CancellationToken ct)
    {
        // Dequeue up to FlushBatchSize entries into a local list.
        var batch = new List<AuditLogQueueEntry>(_settings.FlushBatchSize);

        for (var i = 0; i < _settings.FlushBatchSize; i++)
        {
            var entry = await _queue.DequeueAsync(ct);
            if (entry is null) break;
            batch.Add(entry);
        }

        if (batch.Count == 0) return;

        // Attempt to flush the batch with Polly resilience.
        var reEnqueueList = new List<AuditLogQueueEntry>();

        try
        {
            await _resilience.ExecuteAsync(async token =>
            {
                using var scope   = _scopeFactory.CreateScope();
                var       context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                foreach (var queueEntry in batch)
                {
                    // ── Idempotent flush: skip if LogId already exists (NFR-034) ─────────────
                    var exists = await context.AuditLogs
                        .AnyAsync(a => a.LogId == queueEntry.LogId, token);

                    if (exists)
                    {
                        _logger.LogInformation(
                            "AuditQueueFlushWorker: entry {LogId} already in DB — skipping duplicate.",
                            queueEntry.LogId);
                        continue;
                    }

                    // ── Parse AuditAction enum from string ───────────────────────────────────
                    if (!Enum.TryParse<AuditAction>(queueEntry.Action, ignoreCase: true, out var action))
                    {
                        _logger.LogWarning(
                            "AuditQueueFlushWorker: unknown AuditAction '{Action}' for entry {LogId} — skipping.",
                            queueEntry.Action, queueEntry.LogId);
                        continue;
                    }

                    var log = new AuditLog
                    {
                        LogId        = queueEntry.LogId,
                        UserId       = queueEntry.UserId,
                        Action       = action,
                        ResourceType = queueEntry.ResourceType,
                        ResourceId   = queueEntry.ResourceId,
                        Timestamp    = queueEntry.Timestamp,
                        IpAddress    = queueEntry.IpAddress,
                        UserAgent    = queueEntry.UserAgent,
                    };

                    context.AuditLogs.Add(log);
                }

                await context.SaveChangesAsync(token);

            }, ct);

            _logger.LogInformation(
                "AuditQueueFlushWorker: flushed {Count} entries to PostgreSQL.",
                batch.Count);
        }
        catch (BrokenCircuitException)
        {
            // Circuit opened during this batch — re-enqueue all entries.
            _logger.LogWarning(
                "AuditQueueFlushWorker: circuit breaker opened during batch flush. Re-enqueuing {Count} entries.",
                batch.Count);
            reEnqueueList.AddRange(batch);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // All retries exhausted — re-enqueue the batch so it is not lost.
            _logger.LogError(ex,
                "AuditQueueFlushWorker: batch flush failed after retries. Re-enqueuing {Count} entries.",
                batch.Count);
            reEnqueueList.AddRange(batch);
        }

        // Re-enqueue any entries that could not be flushed.
        foreach (var entry in reEnqueueList)
            await _queue.EnqueueAsync(entry, ct);
    }
}
