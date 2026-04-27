using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.Documents;

/// <summary>
/// Checks the health of the Redis document parsing queue and emits structured Serilog
/// log events for operational monitoring and admin escalation (US_071 TASK_003, AC-4).
///
/// <para>
/// Stale item detection: if the oldest queued item has been waiting longer than
/// <see cref="AiQueueOptions.StaleWarningMinutes"/>, a <c>LogWarning</c> event is emitted.
/// </para>
///
/// <para>
/// Depth escalation: if queue depth exceeds <see cref="AiQueueOptions.EscalationDepthThreshold"/>,
/// a <c>LogCritical</c> structured event is emitted so Serilog/Seq can surface it as an
/// admin alert without requiring an external notification system (AC-4).
/// </para>
///
/// <para>
/// Singleton lifetime — depends only on <see cref="IDocumentParsingQueue"/> (singleton)
/// and <see cref="IOptions{TOptions}"/> (singleton).
/// </para>
/// </summary>
public sealed class QueueMonitorService : IQueueMonitorService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IDocumentParsingQueue       _queue;
    private readonly AiQueueOptions              _options;
    private readonly ILogger<QueueMonitorService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public QueueMonitorService(
        IDocumentParsingQueue        queue,
        IOptions<AiQueueOptions>     options,
        ILogger<QueueMonitorService> logger)
    {
        _queue   = queue;
        _options = options.Value;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IQueueMonitorService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CheckQueueHealthAsync(CancellationToken ct = default)
    {
        // Read both queue metrics in parallel — both are read-only Redis calls.
        var depthTask   = _queue.GetDepthAsync(ct);
        var oldestTask  = _queue.GetOldestItemAgeAsync(ct);

        await Task.WhenAll(depthTask, oldestTask);

        var depth      = depthTask.Result;
        var oldestAge  = oldestTask.Result;
        var ageSeconds = oldestAge.HasValue ? (long)oldestAge.Value.TotalSeconds : 0L;

        // Emit structured metrics on every run for monitoring dashboards.
        _logger.LogInformation(
            "QUEUE_MONITOR: DocumentParsingQueue health check. " +
            "Depth={Depth} OldestAgeSeconds={OldestAgeSeconds} " +
            "StaleThresholdSeconds={StaleThreshold} EscalationThreshold={EscalationThreshold}",
            depth, ageSeconds,
            _options.StaleWarningMinutes * 60,
            _options.EscalationDepthThreshold);

        // ── Stale item check (AC-4: warn when item queued >5 minutes) ─────────────
        if (oldestAge.HasValue && oldestAge.Value.TotalMinutes > _options.StaleWarningMinutes)
        {
            _logger.LogWarning(
                "QUEUE_STALE_ITEM: Document parsing queue has a stale item. " +
                "OldestAgeSeconds={OldestAgeSeconds} StaleThresholdMinutes={Threshold}.",
                ageSeconds, _options.StaleWarningMinutes);
        }

        // ── Depth escalation (AC-4: escalate to admins when depth >50) ───────────
        if (depth > _options.EscalationDepthThreshold)
        {
            _logger.LogCritical(
                "QUEUE_DEPTH_ESCALATION: Document parsing queue depth has exceeded the escalation " +
                "threshold. Depth={Depth} EscalationThreshold={Threshold}. " +
                "Manual review required — AI provider may be rate-limited or offline.",
                depth, _options.EscalationDepthThreshold);
        }
    }
}
