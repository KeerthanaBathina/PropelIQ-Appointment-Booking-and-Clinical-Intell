using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Logging.Models;

namespace UPACIP.Service.Logging;

/// <summary>
/// Monitors Seq availability and manages the in-memory fallback log buffer
/// (US_095, edge case 1: "System falls back to console logging and queues structured
/// log entries for later flush").
///
/// Strategy:
///   - Serilog's WriteTo.Seq() sink is configured non-throwing (default) — it silently
///     drops events when Seq is unreachable while the File and Console sinks continue
///     writing (always-on backup).
///   - This service periodically checks the Seq health endpoint and logs a structured
///     warning/recovery message so operations staff are aware of the outage.
///   - A bounded <see cref="Channel{T}"/> with capacity <see cref="LoggingOptions.FallbackQueueCapacity"/>
///     can be used by application code to buffer important log payloads for deferred
///     submission to Seq once it reconnects. Oldest entries are dropped when full.
///
/// The file sink is the durable backup — Seq can ingest historical file logs via its
/// import feature once connectivity is restored.
/// </summary>
public interface IFallbackLogQueue
{
    /// <summary>
    /// Enqueues a log payload for deferred Seq submission.
    /// Returns <see langword="true"/> when successfully enqueued; <see langword="false"/>
    /// when the queue is full (oldest entry was already dropped).
    /// </summary>
    bool TryEnqueue(string logPayload);

    /// <summary>
    /// Whether the last known Seq health check succeeded.
    /// </summary>
    bool IsSeqAvailable { get; }
}

/// <inheritdoc/>
public sealed class FallbackLogQueue : IFallbackLogQueue, IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(60);

    private readonly Channel<string>          _channel;
    private readonly IHttpClientFactory       _httpClientFactory;
    private readonly string                   _seqHealthUrl;
    private readonly ILogger<FallbackLogQueue> _logger;
    private readonly CancellationTokenSource  _cts = new();
    private Task?                             _healthCheckTask;
    private volatile bool                     _isSeqAvailable = true;

    public bool IsSeqAvailable => _isSeqAvailable;

    public FallbackLogQueue(
        IOptions<LoggingOptions>   options,
        IHttpClientFactory         httpClientFactory,
        ILogger<FallbackLogQueue>  logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;

        var capacity  = options.Value.FallbackQueueCapacity;
        var seqUrl    = options.Value.SeqServerUrl.TrimEnd('/');
        _seqHealthUrl = $"{seqUrl}/api";

        // BoundedChannelFullMode.DropOldest: when capacity is exceeded the oldest
        // entry is silently dropped so new entries are always accepted (edge case 1).
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <inheritdoc/>
    public bool TryEnqueue(string logPayload) =>
        _channel.Writer.TryWrite(logPayload);

    // ── IHostedService — periodic Seq health polling ──────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _healthCheckTask = Task.Run(() => RunHealthCheckLoopAsync(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();

        if (_healthCheckTask is not null)
        {
            try { await _healthCheckTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Dispose();

        if (_healthCheckTask is not null)
        {
            try { await _healthCheckTask; }
            catch { /* swallow — already cancelled */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunHealthCheckLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HealthCheckInterval, ct);
                await CheckSeqHealthAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the health-check loop crash the host process.
                _logger.LogWarning(ex,
                    "LOGGING_HEALTH_CHECK_ERROR: Unexpected error during Seq health check.");
            }
        }
    }

    private async Task CheckSeqHealthAsync(CancellationToken ct)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient("seq-health");
            using var response = await client.GetAsync(_seqHealthUrl, ct);
            var isHealthy      = response.IsSuccessStatusCode;

            if (!isHealthy && _isSeqAvailable)
            {
                // Seq just became unavailable.
                _isSeqAvailable = false;
                _logger.LogWarning(
                    "LOGGING_FALLBACK: Seq unavailable at {Url} (HTTP {StatusCode}). " +
                    "Using file and console sinks. Structured events will be buffered.",
                    _seqHealthUrl, (int)response.StatusCode);
            }
            else if (isHealthy && !_isSeqAvailable)
            {
                // Seq just came back online.
                _isSeqAvailable = true;
                _logger.LogInformation(
                    "LOGGING_RESTORED: Seq connection re-established at {Url}.", _seqHealthUrl);
            }
        }
        catch (HttpRequestException ex)
        {
            if (_isSeqAvailable)
            {
                _isSeqAvailable = false;
                _logger.LogWarning(ex,
                    "LOGGING_FALLBACK: Seq unreachable at {Url}. " +
                    "Using file and console sinks.", _seqHealthUrl);
            }
        }
    }
}
