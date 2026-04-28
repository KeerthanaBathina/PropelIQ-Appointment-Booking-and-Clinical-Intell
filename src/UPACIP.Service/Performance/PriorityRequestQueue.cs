using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// <see cref="IPriorityRequestQueue"/> implementation using three bounded
/// <see cref="Channel{T}"/> instances with <see cref="SemaphoreSlim"/>-guarded consumer
/// loops (US_081 task_002, edge case — priority queuing under peak load).
///
/// <para>
/// <b>Architecture:</b>
/// <list type="number">
///   <item>Three bounded channels — one per <see cref="RequestPriority"/>.</item>
///   <item>Consumer pool: each channel has a fixed-size <see cref="Task.Run"/> consumer
///     pool capped at the configured concurrency limit via a <see cref="SemaphoreSlim"/>.</item>
///   <item>
///     <c>ExecuteAsync</c> writes a <see cref="WorkItem{T}"/> to the channel and awaits a
///     <see cref="TaskCompletionSource{T}"/> — the caller's thread is released while the
///     work item waits in the channel and the result is delivered asynchronously.
///   </item>
///   <item>Back-pressure: when a channel is full (capacity reached), the write blocks until
///     a consumer frees a slot.</item>
/// </list>
/// </para>
///
/// <para>Singleton lifetime — started by <see cref="IHostedService"/>.</para>
/// </summary>
public sealed class PriorityRequestQueue : BackgroundService, IPriorityRequestQueue
{
    // ── Constants ─────────────────────────────────────────────────────────────

    // BoundedChannelFullMode.Wait — WriterAsync blocks when channel is at capacity (back-pressure).
    private static readonly BoundedChannelOptions ChannelOptions = new(capacity: 1)
    {
        FullMode     = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    };

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly PriorityQueueOptions               _options;
    private readonly ILogger<PriorityRequestQueue>      _logger;

    // One channel + semaphore per priority level.
    private readonly Channel<IWorkItem>    _criticalChannel;
    private readonly Channel<IWorkItem>    _normalChannel;
    private readonly Channel<IWorkItem>    _backgroundChannel;

    private readonly SemaphoreSlim _criticalSemaphore;
    private readonly SemaphoreSlim _normalSemaphore;
    private readonly SemaphoreSlim _backgroundSemaphore;

    // ── Constructor ───────────────────────────────────────────────────────────

    public PriorityRequestQueue(
        IOptions<PriorityQueueOptions>  options,
        ILogger<PriorityRequestQueue>   logger)
    {
        _options = options.Value;
        _logger  = logger;

        var cap = _options.ChannelCapacity;
        var channelOptions = new BoundedChannelOptions(cap)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        };

        _criticalChannel    = Channel.CreateBounded<IWorkItem>(channelOptions);
        _normalChannel      = Channel.CreateBounded<IWorkItem>(channelOptions);
        _backgroundChannel  = Channel.CreateBounded<IWorkItem>(channelOptions);

        _criticalSemaphore   = new SemaphoreSlim(_options.CriticalConcurrency,   _options.CriticalConcurrency);
        _normalSemaphore     = new SemaphoreSlim(_options.NormalConcurrency,     _options.NormalConcurrency);
        _backgroundSemaphore = new SemaphoreSlim(_options.BackgroundConcurrency, _options.BackgroundConcurrency);
    }

    // ── IPriorityRequestQueue ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> work,
        RequestPriority                  priority,
        CancellationToken                cancellationToken = default)
    {
        var tcs    = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item   = new WorkItem<T>(work, tcs);

        var channel = priority switch
        {
            RequestPriority.Critical   => _criticalChannel,
            RequestPriority.Normal     => _normalChannel,
            RequestPriority.Background => _backgroundChannel,
            _                          => _normalChannel,
        };

        // WriteAsync blocks when the channel is full (back-pressure).
        await channel.Writer.WriteAsync(item, cancellationToken);

        // Await the result produced by the consumer loop.
        return await tcs.Task.WaitAsync(cancellationToken);
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PriorityRequestQueue started. Critical={C} Normal={N} Background={B} ChannelCap={Cap}",
            _options.CriticalConcurrency,
            _options.NormalConcurrency,
            _options.BackgroundConcurrency,
            _options.ChannelCapacity);

        // Start consumer loops for each priority in parallel.
        await Task.WhenAll(
            ConsumeAsync(_criticalChannel.Reader,   _criticalSemaphore,   "Critical",   stoppingToken),
            ConsumeAsync(_normalChannel.Reader,     _normalSemaphore,     "Normal",     stoppingToken),
            ConsumeAsync(_backgroundChannel.Reader, _backgroundSemaphore, "Background", stoppingToken));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Drains a single priority channel, dispatching each work item with the
    /// associated <see cref="SemaphoreSlim"/> as the concurrency gate.
    /// </summary>
    private async Task ConsumeAsync(
        ChannelReader<IWorkItem> reader,
        SemaphoreSlim            semaphore,
        string                   name,
        CancellationToken        ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            // Wait for a concurrency slot — this throttles active inflight calls.
            await semaphore.WaitAsync(ct);

            // Execute work on a thread pool thread so the consumer loop is not blocked.
            _ = Task.Run(async () =>
            {
                try
                {
                    await item.ExecuteAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "PriorityRequestQueue: unhandled exception in {Priority} work item.", name);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct);
        }
    }

    // ── Work item types ───────────────────────────────────────────────────────

    /// <summary>Non-generic channel envelope for type-erased queueing.</summary>
    private interface IWorkItem
    {
        Task ExecuteAsync(CancellationToken ct);
    }

    /// <summary>Generic work item that carries the delegate and completion source.</summary>
    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<CancellationToken, Task<T>> _work;
        private readonly TaskCompletionSource<T>           _tcs;

        public WorkItem(
            Func<CancellationToken, Task<T>> work,
            TaskCompletionSource<T>           tcs)
        {
            _work = work;
            _tcs  = tcs;
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(ct);
                return;
            }

            try
            {
                var result = await _work(ct);
                _tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                _tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }
    }
}
