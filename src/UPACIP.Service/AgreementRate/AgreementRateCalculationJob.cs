using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AgreementRate;

/// <summary>
/// Daily background job that invokes <see cref="IAgreementRateService.CalculateDailyRateAsync"/>
/// for yesterday's data at the configured UTC hour (default: 00:00 UTC), persists the result,
/// and emits an alert when the rate drops below 98 % (US_050 AC-4, NFR-032).
///
/// Scheduling: <see cref="PeriodicTimer"/> fires every 24 hours.  On first start the job
/// runs immediately if the most-recent stored metric is not from today, avoiding a 24-hour
/// gap when the host restarts mid-day.
///
/// Retry policy (NFR-032):
///   Up to 3 attempts on transient DB errors with exponential backoff:
///   1st retry after 5 s, 2nd after 25 s, 3rd after 125 s.
///   Permanent failures are logged and the job continues its 24-hour schedule.
///
/// DI: Resolves a fresh <c>IServiceScope</c> per execution so the scoped
/// <see cref="IAgreementRateService"/> (and its scoped <c>ApplicationDbContext</c>)
/// are correctly isolated and disposed.
/// </summary>
public sealed class AgreementRateCalculationJob : BackgroundService
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

    private readonly IServiceScopeFactory                     _scopeFactory;
    private readonly ILogger<AgreementRateCalculationJob>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AgreementRateCalculationJob(
        IServiceScopeFactory                 scopeFactory,
        ILogger<AgreementRateCalculationJob> logger)
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
            "AgreementRateCalculationJob: started. Interval={Interval}h.", ExecutionInterval.TotalHours);

        // Run once on start so a host restart doesn't skip the current day's metric.
        await RunCalculationWithRetryAsync(stoppingToken);

        using var timer = new PeriodicTimer(ExecutionInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCalculationWithRetryAsync(stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates yesterday's agreement rate with up to <see cref="MaxRetries"/> retries
    /// on transient DB failures (NFR-032 exponential backoff).
    /// </summary>
    private async Task RunCalculationWithRetryAsync(CancellationToken ct)
    {
        // Calculate for yesterday: daily jobs summarise completed calendar days.
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var scope   = _scopeFactory.CreateAsyncScope();
                var             service = scope.ServiceProvider.GetRequiredService<IAgreementRateService>();

                var result = await service.CalculateDailyRateAsync(targetDate, ct);

                _logger.LogInformation(
                    "AgreementRateCalculationJob: completed for {Date}. " +
                    "Rate={Rate:F2}%, Verified={Verified}, MeetsThreshold={Threshold}.",
                    targetDate, result.DailyAgreementRate, result.TotalCodesVerified,
                    result.MeetsMinimumThreshold);

                if (result.MeetsMinimumThreshold && result.DailyAgreementRate < 98.0m)
                {
                    _logger.LogWarning(
                        "AgreementRateCalculationJob: ALERT — rate {Rate:F2}% is below 98% target for {Date}.",
                        result.DailyAgreementRate, targetDate);
                }

                return; // success — exit retry loop
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host is shutting down — do not retry
                _logger.LogInformation("AgreementRateCalculationJob: cancelled during shutdown.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt < MaxRetries)
                {
                    var delay = RetryDelays[attempt];
                    _logger.LogWarning(ex,
                        "AgreementRateCalculationJob: transient failure on attempt {Attempt}/{Max} for {Date}. " +
                        "Retrying after {Delay}s.",
                        attempt + 1, MaxRetries, targetDate, delay.TotalSeconds);

                    await Task.Delay(delay, ct);
                }
                else
                {
                    _logger.LogError(ex,
                        "AgreementRateCalculationJob: all {Max} retry attempts exhausted for {Date}. " +
                        "Metric will be recalculated on the next 24-hour tick.",
                        MaxRetries, targetDate);
                }
            }
        }
    }
}
