using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace UPACIP.Service.Coding;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis CPT coding queue
/// and dispatches jobs to <see cref="ICptGenerationService"/> (US_048, AC-1, AC-3, NFR-029, AIR-O07).
///
/// Dispatch loop:
/// <list type="number">
///   <item>Poll the Redis list key <c>upacip:cpt-coding-queue</c> every 5 seconds.</item>
///   <item>LPOP one job at a time — coding is LLM-bound; serialising jobs avoids
///         token-budget exhaustion (AIR-O07).</item>
///   <item>Deserialise the <see cref="CptCodingJob"/> payload and forward to
///         <see cref="ICptGenerationService.GenerateCptCodesAsync"/>.</item>
///   <item>On failure, log a structured error and continue — no re-enqueue in this
///         iteration; production hardening would add Polly and a dead-letter queue.</item>
/// </list>
///
/// A fresh DI scope is created per job so scoped services (<c>ApplicationDbContext</c>,
/// <c>ICptGenerationService</c>) are correctly isolated and disposed — matching the
/// <c>Icd10CodingWorker</c> pattern.
///
/// Redis degraded path: when Redis is unavailable the worker logs a warning and waits
/// for the next polling interval rather than crashing the host process.
/// </summary>
public sealed class CptCodingWorker : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Redis list key — used by the controller to enqueue and by this worker to dequeue.</summary>
    public const string QueueKey = "upacip:cpt-coding-queue";

    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer    _redis;
    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly ILogger<CptCodingWorker>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptCodingWorker(
        IConnectionMultiplexer   redis,
        IServiceScopeFactory     scopeFactory,
        ILogger<CptCodingWorker> logger)
    {
        _redis        = redis;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService.ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CptCodingWorker: started. Polling interval={Interval}s.", PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            RedisValue raw;

            // ── Poll Redis ───────────────────────────────────────────────────
            try
            {
                var db = _redis.GetDatabase();
                raw    = await db.ListLeftPopAsync(QueueKey);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
            {
                _logger.LogWarning(ex,
                    "CptCodingWorker: Redis unavailable. Will retry after {Interval}s.",
                    PollingInterval.TotalSeconds);

                await Task.Delay(PollingInterval, stoppingToken);
                continue;
            }

            if (raw.IsNullOrEmpty)
            {
                await Task.Delay(PollingInterval, stoppingToken);
                continue;
            }

            // ── Deserialise job ──────────────────────────────────────────────
            CptCodingJob? job;
            try
            {
                job = JsonSerializer.Deserialize<CptCodingJob>(raw!, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "CptCodingWorker: failed to deserialise job payload. Raw={Raw}", (string?)raw);
                continue;
            }

            if (job is null)
            {
                _logger.LogWarning("CptCodingWorker: null job after deserialisation. Skipping.");
                continue;
            }

            // ── Execute in a fresh DI scope ──────────────────────────────────
            await using var scope   = _scopeFactory.CreateAsyncScope();
            var generationService   = scope.ServiceProvider.GetRequiredService<ICptGenerationService>();

            try
            {
                _logger.LogInformation(
                    "CptCodingWorker: processing job. JobId={JobId} PatientId={PatientId} " +
                    "ProcedureCount={Count} CorrelationId={CorrelationId}",
                    job.JobId, job.PatientId, job.ProcedureIds.Count, job.CorrelationId);

                await generationService.GenerateCptCodesAsync(
                    job.PatientId,
                    job.ProcedureIds,
                    job.CorrelationId,
                    stoppingToken);

                _logger.LogInformation(
                    "CptCodingWorker: job completed. JobId={JobId} CorrelationId={CorrelationId}",
                    job.JobId, job.CorrelationId);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "CptCodingWorker: job failed. JobId={JobId} PatientId={PatientId} " +
                    "CorrelationId={CorrelationId}",
                    job.JobId, job.PatientId, job.CorrelationId);
                // Continue to next job — no re-enqueue in this iteration.
            }
        }

        _logger.LogInformation("CptCodingWorker: stopped.");
    }
}
