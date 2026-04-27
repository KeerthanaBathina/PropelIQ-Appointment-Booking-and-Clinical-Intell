using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Daily background service that drives hallucination rate aggregation and threshold-based
/// alert generation (US_074 task_002, AC-2, AIR-Q06).
///
/// <para>Scheduling: <see cref="PeriodicTimer"/> fires every 24 hours.  On first start the
/// job runs immediately so a host restart does not produce a data gap.</para>
///
/// <para>Retry policy (NFR-032): up to 3 attempts on transient failures with exponential
/// backoff — 5 s, 25 s, 125 s.  Permanent failures are logged at Error level and the
/// job continues to the next 24-hour interval.</para>
///
/// <para>DI: resolves a fresh <c>IServiceScope</c> per execution so the scoped
/// <see cref="IHallucinationTrackingService"/> (and its <c>ApplicationDbContext</c>) are
/// correctly isolated and disposed after each run.</para>
/// </summary>
public sealed class HallucinationAggregationJob : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const int MaxRetries = 3;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(25),
        TimeSpan.FromSeconds(125),
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory         _scopeFactory;
    private readonly ILogger<HallucinationAggregationJob> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public HallucinationAggregationJob(
        IServiceScopeFactory                   scopeFactory,
        ILogger<HallucinationAggregationJob>   logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService.ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HallucinationAggregationJob: started. Interval=24 hours.");

        // Run once immediately on startup to avoid a gap after a host restart.
        await RunAggregationWithRetryAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunAggregationWithRetryAsync(stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <see cref="IHallucinationTrackingService.RunDailyAggregationAsync"/> with
    /// exponential-backoff retries (NFR-032).
    /// </summary>
    private async Task RunAggregationWithRetryAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var scope   = _scopeFactory.CreateAsyncScope();
                var             service = scope.ServiceProvider
                    .GetRequiredService<IHallucinationTrackingService>();

                await service.RunDailyAggregationAsync(ct);

                _logger.LogInformation("HallucinationAggregationJob: aggregation run succeeded.");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("HallucinationAggregationJob: aggregation run cancelled.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "HallucinationAggregationJob: aggregation run failed after {Attempts} attempts.",
                        MaxRetries + 1);
                    return;
                }

                TimeSpan delay = RetryDelays[attempt];
                _logger.LogWarning(
                    ex,
                    "HallucinationAggregationJob: attempt {Attempt} failed; retrying in {Delay}.",
                    attempt + 1, delay);

                await Task.Delay(delay, ct);
            }
        }
    }
}
