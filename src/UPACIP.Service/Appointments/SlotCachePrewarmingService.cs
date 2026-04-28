using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.Performance;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that pre-warms the Redis slot availability cache
/// on application startup and on a periodic 5-minute schedule (US_081 task_002, AC-1).
///
/// <para>
/// <b>Why pre-warm?</b>
/// Without pre-warming, the first booking request after a cache expiry incurs a full
/// PostgreSQL round-trip — adding ~100-200ms to the P95 hot path. Pre-warming loads
/// available slots for all active providers × next 7 days into Redis so the first
/// booking request is served from cache at sub-millisecond cost (>80% hit ratio, NFR-004).
/// </para>
///
/// <para>
/// <b>Implementation:</b>
/// <list type="number">
///   <item>Query all distinct active provider IDs from <c>provider_availability_templates</c>.</item>
///   <item>For each provider, call <see cref="IAppointmentSlotService.GetAvailableSlotsAsync"/>
///     for the next 7 days (the method populates the cache as a cache-aside side effect).</item>
///   <item>Repeat every 5 minutes via <see cref="PeriodicTimer"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Exceptions during a pre-warm cycle are swallowed — cache failures never affect bookings
/// because <see cref="AppointmentBookingService"/> falls back to the DB on cache miss.
/// </para>
/// </summary>
public sealed class SlotCachePrewarmingService : BackgroundService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly TimeSpan PrewarmInterval = TimeSpan.FromMinutes(5);
    private const int PrewarmWindowDays = 7;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly IPerformanceTracker                _tracker;
    private readonly ILogger<SlotCachePrewarmingService> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SlotCachePrewarmingService(
        IServiceScopeFactory                scopes,
        IPerformanceTracker                 tracker,
        ILogger<SlotCachePrewarmingService> logger)
    {
        _scopeFactory = scopes;
        _tracker      = tracker;
        _logger       = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SlotCachePrewarmingService started. Interval={Interval}min Window={Window}days",
            PrewarmInterval.TotalMinutes, PrewarmWindowDays);

        // Run once immediately on startup so the cache is warm before the first request.
        await RunPrewarmCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(PrewarmInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPrewarmCycleAsync(stoppingToken);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunPrewarmCycleAsync(CancellationToken ct)
    {
        try
        {
            using var scope       = _scopeFactory.CreateScope();
            var db                = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var slotService       = scope.ServiceProvider.GetRequiredService<IAppointmentSlotService>();

            // Fetch distinct active provider IDs.
            var providerIds = await db.ProviderAvailabilityTemplates
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => t.ProviderId)
                .Distinct()
                .ToListAsync(ct);

            if (providerIds.Count == 0)
            {
                _logger.LogDebug("SlotCachePrewarmingService: no active providers found — skipping cycle.");
                return;
            }

            var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var endDate   = startDate.AddDays(PrewarmWindowDays);
            int populated = 0;

            foreach (var providerId in providerIds)
            {
                if (ct.IsCancellationRequested) break;

                using var span = _tracker.StartSpan("booking.cache_prewarm");
                try
                {
                    var parameters = new SlotQueryParameters
                    {
                        StartDate  = startDate,
                        EndDate    = endDate,
                        ProviderId = providerId,
                    };

                    // GetAvailableSlotsAsync populates Redis as a side effect (cache-aside).
                    await slotService.GetAvailableSlotsAsync(parameters, ct);
                    populated++;
                    _tracker.CompleteOperation(span, success: true);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _tracker.CompleteOperation(span, success: false);
                    _logger.LogWarning(ex,
                        "SlotCachePrewarmingService: failed to pre-warm provider {ProviderId}.",
                        providerId);
                }
            }

            _logger.LogInformation(
                "SlotCachePrewarmingService: cycle complete. Providers={Total} Populated={Populated}",
                providerIds.Count, populated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown — exit silently.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SlotCachePrewarmingService: pre-warm cycle failed. Will retry on next tick.");
        }
    }
}
