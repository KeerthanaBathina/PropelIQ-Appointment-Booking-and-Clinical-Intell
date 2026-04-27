using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.AiCost;

/// <summary>
/// Daily background job that aggregates yesterday's AI request log entries into
/// <c>AiCostDailySummary</c> rows and checks all provider budgets for threshold
/// breaches (US_071 AC-1, AC-2, NFR-032).
///
/// <para>
/// Scheduling: <see cref="PeriodicTimer"/> fires every 24 hours.  On first start the job
/// runs immediately so a host restart does not skip the current day's aggregation.
/// </para>
///
/// <para>
/// Retry policy (NFR-032):
///   Up to 3 attempts on transient failures with exponential backoff:
///   1st retry after 5 s, 2nd after 25 s, 3rd after 125 s.
///   Permanent failures are logged and the job continues its 24-hour schedule.
/// </para>
///
/// <para>
/// DI: Resolves a fresh <see cref="IServiceScope"/> per execution so the scoped
/// <see cref="IAiCostAggregationService"/> and <see cref="IAiCostAlertService"/>
/// (and their scoped <c>ApplicationDbContext</c>) are correctly isolated and disposed.
/// </para>
/// </summary>
public sealed class AiCostAggregationJob : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan ExecutionInterval = TimeSpan.FromHours(24);
    private const int MaxRetries = 3;

    // Exponential backoff delays: 5 s, 25 s, 125 s
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(25),
        TimeSpan.FromSeconds(125),
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory              _scopeFactory;
    private readonly ILogger<AiCostAggregationJob>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiCostAggregationJob(
        IServiceScopeFactory          scopeFactory,
        ILogger<AiCostAggregationJob> logger)
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
            "AiCostAggregationJob: started. Interval={Interval}h.", ExecutionInterval.TotalHours);

        // Run once on start so a host restart does not skip the current day's aggregation.
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
    /// Runs cost aggregation and budget-threshold checks for yesterday with
    /// up to <see cref="MaxRetries"/> retries on transient failures (NFR-032).
    /// </summary>
    private async Task RunAggregationWithRetryAsync(CancellationToken ct)
    {
        // Daily jobs summarise completed calendar days — always use yesterday.
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var aggregationService = scope.ServiceProvider
                    .GetRequiredService<IAiCostAggregationService>();
                var alertService = scope.ServiceProvider
                    .GetRequiredService<IAiCostAlertService>();

                await aggregationService.AggregateDailyCostsAsync(targetDate, ct);
                await alertService.CheckBudgetThresholdsAsync(targetDate, ct);

                _logger.LogInformation(
                    "AiCostAggregationJob: completed aggregation and threshold check for {Date}.",
                    targetDate);

                return; // success — exit retry loop
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host is shutting down — do not retry.
                _logger.LogInformation("AiCostAggregationJob: cancelled during shutdown.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt < MaxRetries)
                {
                    var delay = RetryDelays[attempt];
                    _logger.LogWarning(ex,
                        "AiCostAggregationJob: transient failure on attempt {Attempt}/{Max} for {Date}. " +
                        "Retrying after {Delay}s.",
                        attempt + 1, MaxRetries, targetDate, delay.TotalSeconds);

                    await Task.Delay(delay, ct);
                }
                else
                {
                    _logger.LogError(ex,
                        "AiCostAggregationJob: all {Max} retry attempts exhausted for {Date}. " +
                        "Aggregation will be retried on the next 24-hour tick.",
                        MaxRetries, targetDate);
                }
            }
        }
    }
}
