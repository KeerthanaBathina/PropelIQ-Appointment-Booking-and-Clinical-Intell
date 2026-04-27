using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AiMetrics;

/// <summary>
/// Daily background job that invokes <see cref="IAiMetricsService.RunDailyAggregationAsync"/>
/// for the previous calendar day, persists aggregated metrics, and generates threshold-based
/// alerts (US_072 AC-4, NFR-032).
///
/// <para>Scheduling: <see cref="PeriodicTimer"/> fires every 24 hours.  On first start the
/// job runs immediately so a host restart does not produce a 24-hour data gap.</para>
///
/// <para>Retry policy (NFR-032): up to 3 attempts on transient failures with exponential
/// backoff — 5 s, 25 s, 125 s.  Permanent failures are logged and the job continues.</para>
///
/// <para>DI: resolves a fresh <c>IServiceScope</c> per execution so the scoped
/// <see cref="IAiMetricsService"/> (and its <c>ApplicationDbContext</c>) are correctly
/// isolated and disposed after each run.</para>
/// </summary>
public sealed class AiMetricsCalculationJob : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan ExecutionInterval = TimeSpan.FromHours(24);
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

    private readonly IServiceScopeFactory                  _scopeFactory;
    private readonly ILogger<AiMetricsCalculationJob>      _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiMetricsCalculationJob(
        IServiceScopeFactory             scopeFactory,
        ILogger<AiMetricsCalculationJob> logger)
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
        _logger.LogInformation(
            "AiMetricsCalculationJob: started. Interval={Interval}h.", ExecutionInterval.TotalHours);

        // Run once on startup to avoid a 24-hour gap after a host restart.
        await RunAggregationWithRetryAsync(stoppingToken);

        using var timer = new PeriodicTimer(ExecutionInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunAggregationWithRetryAsync(stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the aggregation for yesterday with exponential-backoff retries (NFR-032).
    /// </summary>
    private async Task RunAggregationWithRetryAsync(CancellationToken ct)
    {
        // Aggregate completed calendar day — yesterday's UTC date at midnight.
        DateTime targetDate = DateTime.UtcNow.Date.AddDays(-1);

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var scope   = _scopeFactory.CreateAsyncScope();
                var             service = scope.ServiceProvider.GetRequiredService<IAiMetricsService>();

                await service.RunDailyAggregationAsync(targetDate, ct);

                _logger.LogInformation(
                    "AiMetricsCalculationJob: aggregation succeeded for {Date:yyyy-MM-dd}.", targetDate);

                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("AiMetricsCalculationJob: aggregation cancelled.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "AiMetricsCalculationJob: aggregation failed after {Attempts} attempts for {Date:yyyy-MM-dd}.",
                        MaxRetries + 1, targetDate);
                    return;
                }

                TimeSpan delay = RetryDelays[attempt];
                _logger.LogWarning(
                    ex,
                    "AiMetricsCalculationJob: attempt {Attempt}/{Max} failed for {Date:yyyy-MM-dd}. Retrying in {Delay}s.",
                    attempt + 1, MaxRetries + 1, targetDate, delay.TotalSeconds);

                await Task.Delay(delay, ct);
            }
        }
    }
}
