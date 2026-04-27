using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Queue;

/// <summary>
/// Background service that automatically marks appointments as no-show after they exceed
/// the 15-minute threshold past their scheduled time (US_052 AC-2).
///
/// Design:
///   - Runs every 60 seconds via <see cref="PeriodicTimer"/>.
///   - Creates a fresh DI scope per cycle to obtain a scoped <see cref="IQueueService"/>
///     (and its transitive dependency on <c>ApplicationDbContext</c>).
///   - On three consecutive cycle failures <see cref="IsHealthy"/> flips to false so
///     the /health endpoint can surface the issue (register via AddHealthChecks if desired).
///   - Failures are logged and retried on the next cycle (no exponential backoff needed
///     at this granularity — the 60-second timer provides natural back-pressure).
///   - Graceful shutdown is handled via the <c>CancellationToken</c> passed to
///     <see cref="ExecuteAsync"/>; the timer is disposed after the loop exits.
/// </summary>
public sealed class NoShowDetectionService : BackgroundService
{
    private static readonly TimeSpan PollingInterval       = TimeSpan.FromSeconds(60);
    private const           int      DegradedFailureCount  = 3;

    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<NoShowDetectionService>  _logger;

    // Consecutive failure counter for degraded-health detection (AC-2 edge case)
    private int _consecutiveFailures;

    /// <summary>
    /// Returns false after three consecutive cycle failures.
    /// Callers (e.g. a custom health check registered in Program.cs) can poll this
    /// property to surface service degradation at the /health endpoint.
    /// </summary>
    public bool IsHealthy => _consecutiveFailures < DegradedFailureCount;

    public NoShowDetectionService(
        IServiceScopeFactory            scopeFactory,
        ILogger<NoShowDetectionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService — main loop
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NoShowDetectionService started — polling every {Interval}.", PollingInterval);

        // Startup outage-recovery scan: process all overdue appointments that were missed
        // while the service was offline, flagging them with delayed_detection = true (AC-1 edge case).
        await RunCycleAsync(isDelayedDetection: true, stoppingToken);

        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(isDelayedDetection: false, stoppingToken);
        }

        _logger.LogInformation("NoShowDetectionService stopping.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — single detection cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunCycleAsync(bool isDelayedDetection, CancellationToken cancellationToken)
    {
        try
        {
            // Create a fresh scope per cycle — ApplicationDbContext is scoped (not singleton)
            await using var scope         = _scopeFactory.CreateAsyncScope();
            var             queueService  = scope.ServiceProvider.GetRequiredService<IQueueService>();

            await queueService.MarkNoShowsAsync(isDelayedDetection, cancellationToken);

            // Reset failure counter on a successful cycle
            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown — do not log as an error
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogError(ex,
                "NoShowDetectionService cycle failed (consecutive failures: {Count}). " +
                "Will retry on next cycle.",
                _consecutiveFailures);
        }
    }
}
