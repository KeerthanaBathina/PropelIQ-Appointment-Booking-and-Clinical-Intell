using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UPACIP.DataAccess;
using UPACIP.Service.Validation;

namespace UPACIP.Service.Consolidation;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drains the Redis consolidation queue and
/// dispatches jobs to <see cref="IConsolidationService"/> (US_043, FR-052, FR-056).
///
/// Dispatch loop:
/// <list type="number">
///   <item>Poll the Redis list key <c>upacip:consolidation-queue</c> every 5 seconds.</item>
///   <item>LPOP one job at a time (single-threaded — consolidation is CPU/DB intensive).</item>
///   <item>
///     Deserialize the <see cref="ConsolidationQueueJob"/> payload and route to
///     <see cref="IConsolidationService.IncrementalConsolidateAsync"/> when
///     <c>NewDocumentIds</c> is populated, or <see cref="IConsolidationService.ConsolidatePatientProfileAsync"/>
///     for full consolidation jobs.
///   </item>
///   <item>
///     On failure, log a structured error and continue to the next job — no re-enqueue.
///     A production implementation would add Polly retry and a dead-letter queue (US_043 NFR-035).
///   </item>
/// </list>
///
/// A fresh DI scope is created per job so scoped services (<c>ApplicationDbContext</c>,
/// <c>IConsolidationService</c>) are correctly isolated and disposed — matching the
/// <c>DocumentParsingDispatcher</c> pattern used throughout the project.
///
/// Redis degraded path: when Redis is unavailable the worker logs a warning and waits
/// for the next polling interval rather than crashing the host process.
/// </summary>
public sealed class ConsolidationWorker : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Redis list key — must match <see cref="ConsolidationService.QueueKey"/>.</summary>
    private const string QueueKey = ConsolidationService.QueueKey;

    /// <summary>How long to wait between queue polls when the queue is empty.</summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer         _redis;
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly ILogger<ConsolidationWorker>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConsolidationWorker(
        IConnectionMultiplexer       redis,
        IServiceScopeFactory         scopeFactory,
        ILogger<ConsolidationWorker> logger)
    {
        _redis        = redis;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService.ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConsolidationWorker: started. Polling interval={Interval}s.", PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            RedisValue raw;

            // ── Poll Redis ───────────────────────────────────────────────────
            try
            {
                var db  = _redis.GetDatabase();
                raw     = await db.ListLeftPopAsync(QueueKey);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
            {
                _logger.LogWarning(ex,
                    "ConsolidationWorker: Redis unavailable. Will retry after {Interval}s.",
                    PollingInterval.TotalSeconds);

                await Task.Delay(PollingInterval, stoppingToken);
                continue;
            }

            if (raw.IsNullOrEmpty)
            {
                await Task.Delay(PollingInterval, stoppingToken);
                continue;
            }

            // ── Deserialize job ──────────────────────────────────────────────
            ConsolidationQueueJob? job;
            try
            {
                job = JsonSerializer.Deserialize<ConsolidationQueueJob>(raw!, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "ConsolidationWorker: failed to deserialize job payload. Raw={Raw}", (string?)raw);
                continue;
            }

            if (job is null)
            {
                _logger.LogError("ConsolidationWorker: deserialized null job. Skipping.");
                continue;
            }

            // ── Dispatch in a fresh DI scope ─────────────────────────────────
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IConsolidationService>();

            try
            {
                ConsolidationResult result;

                bool isIncremental = job.NewDocumentIds is { Count: > 0 };

                if (isIncremental)
                {
                    result = await service.IncrementalConsolidateAsync(
                        job.PatientId,
                        job.NewDocumentIds!,
                        job.TriggeredByUserId,
                        stoppingToken);
                }
                else
                {
                    result = await service.ConsolidatePatientProfileAsync(
                        job.PatientId,
                        job.TriggeredByUserId,
                        stoppingToken);
                }

                _logger.LogInformation(
                    "ConsolidationWorker: job complete. " +
                    "PatientId={PatientId}, Version={Version}, Merged={Merged}, DurationMs={Duration}",
                    job.PatientId, result.NewVersionNumber, result.TotalMergedCount, result.DurationMs);

                // ── Post-consolidation date validation (US_046 AC-2, edge case) ─────────
                // Run chronological plausibility checks and partial-date flagging on all
                // extracted data for the patient. Non-blocking — a failure here does not
                // fail the completed consolidation result.
                try
                {
                    var db             = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var dateValidator  = scope.ServiceProvider.GetRequiredService<IDateValidationService>();

                    var trackedRows = await db.ExtractedData
                        .Include(ed => ed.Document)
                        .Where(ed => ed.Document.PatientId == job.PatientId && !ed.IsArchived)
                        .ToListAsync(stoppingToken);

                    var violations = await dateValidator.ValidateAndAnnotateAsync(trackedRows, stoppingToken);

                    if (violations.Count > 0)
                        await db.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation(
                        "ConsolidationWorker: date validation complete. PatientId={PatientId}, Violations={Violations}",
                        job.PatientId, violations.Count);
                }
                catch (Exception dateEx)
                {
                    _logger.LogError(dateEx,
                        "ConsolidationWorker: date validation step failed (non-fatal). PatientId={PatientId}",
                        job.PatientId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — exit the loop cleanly.
                break;
            }
            catch (Exception ex)
            {
                // Log and continue — do not crash the worker for a single job failure.
                // A production implementation would re-enqueue with back-off or push to a DLQ.
                _logger.LogError(ex,
                    "ConsolidationWorker: job failed. PatientId={PatientId}, EnqueuedAt={EnqueuedAt}",
                    job.PatientId, job.EnqueuedAt);
            }
        }

        _logger.LogInformation("ConsolidationWorker: stopped.");
    }
}
