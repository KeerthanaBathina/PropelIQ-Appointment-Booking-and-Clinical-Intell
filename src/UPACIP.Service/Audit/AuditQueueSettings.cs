using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Audit;

/// <summary>
/// Configuration for the Redis audit failover queue and flush worker (US_064 edge case).
/// Bound from the <c>AuditQueue</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class AuditQueueSettings
{
    /// <summary>appsettings.json configuration section key.</summary>
    public const string SectionName = "AuditQueue";

    /// <summary>Redis list key for the failover queue.</summary>
    public string RedisQueueKey { get; init; } = "audit:failover:queue";

    /// <summary>Maximum number of entries flushed to PostgreSQL per worker iteration.</summary>
    [Range(1, 10000)]
    public int FlushBatchSize { get; init; } = 100;

    /// <summary>Worker polling interval in milliseconds when the queue has pending entries.</summary>
    [Range(100, 300_000)]
    public int FlushIntervalMs { get; init; } = 5_000;

    /// <summary>Worker idle delay in milliseconds when the queue is empty (reduces Redis polling).</summary>
    [Range(100, 600_000)]
    public int IdleIntervalMs { get; init; } = 30_000;

    /// <summary>
    /// Number of consecutive DB failures that cause the Polly circuit breaker to open (NFR-032).
    /// </summary>
    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    /// <summary>Duration in seconds the circuit breaker stays open before transitioning to half-open (NFR-032).</summary>
    [Range(1, 3600)]
    public int CircuitBreakerDurationSeconds { get; init; } = 30;

    /// <summary>Number of exponential-backoff retries per batch before re-enqueuing (NFR-032).</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Local file path for the last-resort fallback when both PostgreSQL and Redis are unavailable.
    /// The <c>{date}</c> token is replaced with <c>yyyy-MM-dd</c> at runtime.
    /// </summary>
    public string LocalFallbackPath { get; init; } = "logs/audit-failover-{date}.json";
}
