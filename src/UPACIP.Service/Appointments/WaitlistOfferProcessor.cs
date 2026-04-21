using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Singleton channel interface that allows the appointment cancellation flow to
/// enqueue a freed slot without depending on <see cref="IWaitlistService"/> directly.
///
/// This avoids a circular-dependency risk and keeps the cancellation hot-path non-blocking.
/// </summary>
public interface IWaitlistOfferQueue
{
    /// <summary>
    /// Queues a slot for waitlist-offer dispatch. Fire-and-forget from the caller's perspective.
    /// Returns <c>false</c> only if the queue is at full capacity (practically never under normal load).
    /// </summary>
    bool TryEnqueue(SlotItem slot);
}

/// <summary>
/// Background service that drains the <see cref="IWaitlistOfferQueue"/> channel and
/// dispatches waitlist offer notifications via <see cref="IWaitlistService"/> (AC-2, AC-4).
///
/// Design:
///   - Uses a bounded <see cref="Channel{T}"/> (capacity 512) so memory is bounded
///     even under burst cancellation load.
///   - Each dequeued slot is processed in an isolated try/catch so one failure never
///     prevents the next item from being processed.
///   - Uses <see cref="IServiceScopeFactory"/> to resolve the scoped
///     <see cref="IWaitlistService"/> per item — this matches the scoped EF DbContext lifecycle.
///   - Graceful shutdown: the loop honours the <see cref="CancellationToken"/> passed by the host.
/// </summary>
public sealed class WaitlistOfferProcessor : BackgroundService, IWaitlistOfferQueue
{
    private readonly Channel<SlotItem>         _channel;
    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly ILogger<WaitlistOfferProcessor> _logger;

    public WaitlistOfferProcessor(
        IServiceScopeFactory         scopeFactory,
        ILogger<WaitlistOfferProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        // Bounded channel: drop-newest under pressure (prefer processing already-queued slots)
        _channel = Channel.CreateBounded<SlotItem>(new BoundedChannelOptions(512)
        {
            FullMode          = BoundedChannelFullMode.DropNewest,
            SingleReader      = true,   // Only ExecuteAsync reads
            SingleWriter      = false,  // Multiple controllers may enqueue concurrently
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IWaitlistOfferQueue
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool TryEnqueue(SlotItem slot)
    {
        var written = _channel.Writer.TryWrite(slot);
        if (!written)
            _logger.LogWarning(
                "WaitlistOfferQueue full — dropped slot {SlotId}. Consider increasing queue capacity.",
                slot.SlotId);
        return written;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Long-running loop that dequeues slots and processes each one inside a fresh DI scope.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WaitlistOfferProcessor started.");

        try
        {
            await foreach (var slot in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessSlotAsync(slot, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — do not rethrow
        }

        _logger.LogInformation("WaitlistOfferProcessor stopped.");
    }

    /// <summary>
    /// Processes one slot: creates a scoped service provider, resolves
    /// <see cref="IWaitlistService"/>, and calls <see cref="IWaitlistService.DispatchOffersForSlotAsync"/>.
    /// </summary>
    private async Task ProcessSlotAsync(SlotItem slot, CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "WaitlistOfferProcessor: processing freed slot {SlotId}.", slot.SlotId);

        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var waitlistService = scope.ServiceProvider.GetRequiredService<IWaitlistService>();
            await waitlistService.DispatchOffersForSlotAsync(slot, stoppingToken);
        }
        catch (Exception ex)
        {
            // Log but do not crash the processor — next item must still be processed
            _logger.LogError(ex,
                "WaitlistOfferProcessor: unhandled error dispatching offers for slot {SlotId}.",
                slot.SlotId);
        }
    }
}
