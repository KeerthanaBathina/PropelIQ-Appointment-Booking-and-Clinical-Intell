using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace UPACIP.Service.Coding;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis ICD-10 coding queue
/// and dispatches jobs to <see cref="IIcd10CodingService"/> (US_047, AC-1, NFR-029, AIR-O07).
///
/// Dispatch loop:
/// <list type="number">
///   <item>Poll the Redis list key <c>upacip:icd10-coding-queue</c> every 5 seconds.</item>
///   <item>LPOP one job at a time — coding is LLM-bound and serialising jobs avoids
///         token-budget exhaustion (AIR-O07).</item>
///   <item>Deserialise the <see cref="Icd10CodingJob"/> payload and forward to
///         <see cref="IIcd10CodingService.GenerateIcd10CodesAsync"/>.</item>
///   <item>On failure, log a structured error and continue — no re-enqueue in this
///         iteration; a production hardening adds Polly and a dead-letter queue.</item>
/// </list>
///
/// A fresh DI scope is created per job so scoped services (<c>ApplicationDbContext</c>,
/// <c>IIcd10CodingService</c>) are correctly isolated and disposed — matching the
/// <c>ConsolidationWorker</c> pattern used throughout the project.
///
/// Redis degraded path: when Redis is unavailable the worker logs a warning and waits
/// for the next polling interval rather than crashing the host process.
/// </summary>
public sealed class Icd10CodingWorker : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Redis list key — used by the controller to enqueue jobs and by this worker to dequeue them.</summary>
    public const string QueueKey = "upacip:icd10-coding-queue";

    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer     _redis;
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly ILogger<Icd10CodingWorker> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Icd10CodingWorker(
        IConnectionMultiplexer     redis,
        IServiceScopeFactory       scopeFactory,
        ILogger<Icd10CodingWorker> logger)
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
            "Icd10CodingWorker: started. Polling interval={Interval}s.", PollingInterval.TotalSeconds);

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
                    "Icd10CodingWorker: Redis unavailable. Will retry after {Interval}s.",
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
            Icd10CodingJob? job;
            try
            {
                job = JsonSerializer.Deserialize<Icd10CodingJob>(raw!, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Icd10CodingWorker: failed to deserialise job payload. Raw={Raw}", (string?)raw);
                continue;
            }

            if (job is null)
            {
                _logger.LogWarning("Icd10CodingWorker: null job after deserialisation. Skipping.");
                continue;
            }

            // ── Execute in a fresh DI scope ──────────────────────────────────
            await using var scope   = _scopeFactory.CreateAsyncScope();
            var codingService       = scope.ServiceProvider.GetRequiredService<IIcd10CodingService>();

            try
            {
                _logger.LogInformation(
                    "Icd10CodingWorker: processing job. JobId={JobId} PatientId={PatientId} " +
                    "DiagnosisCount={Count} CorrelationId={CorrelationId}",
                    job.JobId, job.PatientId, job.DiagnosisIds.Count, job.CorrelationId);

                await codingService.GenerateIcd10CodesAsync(
                    job.PatientId,
                    job.DiagnosisIds,
                    job.CorrelationId,
                    stoppingToken);

                _logger.LogInformation(
                    "Icd10CodingWorker: job completed. JobId={JobId} CorrelationId={CorrelationId}",
                    job.JobId, job.CorrelationId);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "Icd10CodingWorker: job failed. JobId={JobId} PatientId={PatientId} " +
                    "CorrelationId={CorrelationId}",
                    job.JobId, job.PatientId, job.CorrelationId);
                // Continue to next job — no re-enqueue in this iteration.
            }
        }

        _logger.LogInformation("Icd10CodingWorker: stopped.");
    }
}
