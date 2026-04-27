using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Calibration;

/// <summary>
/// Weekly background service that drives the confidence score calibration workflow (US_073 AC-3, AC-4).
///
/// <para>Scheduling: <see cref="PeriodicTimer"/> fires every <c>Calibration:IntervalDays</c>
/// days (default: 7).  On first start the job runs immediately so a host restart does not
/// produce a 7-day data gap.</para>
///
/// <para>Retry policy (NFR-032): up to 3 attempts on transient failures with exponential
/// backoff — 5 s, 25 s, 125 s.  Permanent failures are logged at Error level and the
/// job continues to the next interval.</para>
///
/// <para>DI: resolves a fresh <c>IServiceScope</c> per execution so the scoped
/// <see cref="ICalibrationService"/> (and its <c>ApplicationDbContext</c>) are correctly
/// isolated and disposed after each run.</para>
/// </summary>
public sealed class CalibrationJob : BackgroundService
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

    private readonly IServiceScopeFactory    _scopeFactory;
    private readonly ILogger<CalibrationJob> _logger;
    private readonly TimeSpan                _executionInterval;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CalibrationJob(
        IServiceScopeFactory    scopeFactory,
        ILogger<CalibrationJob> logger,
        IConfiguration          configuration)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        int intervalDays = configuration.GetValue<int>("Calibration:IntervalDays", defaultValue: 7);
        _executionInterval = TimeSpan.FromDays(intervalDays);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService.ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CalibrationJob: started. Interval={Interval} days.", _executionInterval.TotalDays);

        // Run once immediately on startup to avoid a gap after a host restart.
        await RunCalibrationWithRetryAsync(stoppingToken);

        using var timer = new PeriodicTimer(_executionInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCalibrationWithRetryAsync(stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <see cref="ICalibrationService.RunWeeklyCalibrationAsync"/> with
    /// exponential-backoff retries (NFR-032).
    /// </summary>
    private async Task RunCalibrationWithRetryAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var scope   = _scopeFactory.CreateAsyncScope();
                var             service = scope.ServiceProvider.GetRequiredService<ICalibrationService>();

                await service.RunWeeklyCalibrationAsync(ct);

                _logger.LogInformation("CalibrationJob: calibration run succeeded.");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("CalibrationJob: calibration run cancelled.");
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "CalibrationJob: calibration run failed after {Attempts} attempts.",
                        MaxRetries + 1);
                    return;
                }

                TimeSpan delay = RetryDelays[attempt];
                _logger.LogWarning(
                    ex,
                    "CalibrationJob: attempt {Attempt} failed; retrying in {Delay}.",
                    attempt + 1, delay);

                await Task.Delay(delay, ct);
            }
        }
    }
}
