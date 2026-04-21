using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Singleton channel queue that decouples the cancellation hot-path from the
/// preferred-slot swap evaluation (US_021 AC-1, AC-2).
///
/// The slot-release path (AppointmentCancellationService) calls TryEnqueue after commit
/// and cache invalidation; this interface ensures zero coupling to the processor concrete type.
/// </summary>
public interface IPreferredSlotSwapQueue
{
    /// <summary>
    /// Enqueues a slot for preferred-slot swap evaluation. Non-blocking.
    /// Returns false only when the internal channel is at capacity (bounded 512 items).
    /// </summary>
    bool TryEnqueue(SlotItem slot);
}

/// <summary>
/// Background service that drains the <see cref="IPreferredSlotSwapQueue"/> channel and
/// evaluates each opened slot via <see cref="IPreferredSlotSwapService"/> (US_021 AC-1–AC-5).
///
/// Design mirrors <c>WaitlistOfferProcessor</c>:
///   - Bounded channel (512) — memory-bounded under burst cancellation load.
///   - SingleReader = true — only ExecuteAsync reads.
///   - Per-item DI scope — resolves scoped <see cref="IPreferredSlotSwapService"/> for each slot.
///   - Isolated try/catch per item — one error never blocks subsequent evaluations.
///   - Graceful shutdown on host <see cref="CancellationToken"/>.
/// </summary>
public sealed class PreferredSlotSwapProcessor : BackgroundService, IPreferredSlotSwapQueue
{
    private readonly Channel<SlotItem>               _channel;
    private readonly IServiceScopeFactory            _scopeFactory;
    private readonly ILogger<PreferredSlotSwapProcessor> _logger;

    public PreferredSlotSwapProcessor(
        IServiceScopeFactory                 scopeFactory,
        ILogger<PreferredSlotSwapProcessor>  logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        _channel = Channel.CreateBounded<SlotItem>(new BoundedChannelOptions(512)
        {
            FullMode     = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPreferredSlotSwapQueue
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool TryEnqueue(SlotItem slot)
    {
        var written = _channel.Writer.TryWrite(slot);
        if (!written)
            _logger.LogWarning(
                "PreferredSlotSwapQueue full — dropped slot {SlotId}.", slot.SlotId);
        return written;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PreferredSlotSwapProcessor started.");

        try
        {
            await foreach (var slot in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessSlotAsync(slot, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown
        }

        _logger.LogInformation("PreferredSlotSwapProcessor stopped.");
    }

    private async Task ProcessSlotAsync(SlotItem slot, CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "PreferredSlotSwapProcessor: evaluating slot {SlotId}.", slot.SlotId);
        try
        {
            using var scope  = _scopeFactory.CreateScope();
            var swapService  = scope.ServiceProvider.GetRequiredService<IPreferredSlotSwapService>();
            var results      = await swapService.EvaluateAndSwapAsync(slot, stoppingToken);

            _logger.LogInformation(
                "PreferredSlotSwapProcessor: slot {SlotId} produced {Count} outcome(s).",
                slot.SlotId, results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PreferredSlotSwapProcessor: unhandled error for slot {SlotId}.", slot.SlotId);
        }
    }
}
