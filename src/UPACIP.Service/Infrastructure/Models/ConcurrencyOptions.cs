namespace UPACIP.Service.Infrastructure.Models;

/// <summary>
/// Concurrency and connection pool configuration (US_082 task_001, AC-2, AC-3).
///
/// <para>Bind from the <c>"Concurrency"</c> configuration section in <c>appsettings.json</c>.</para>
///
/// <code>
/// "Concurrency": {
///   "MaxDbConnections": 100,
///   "PoolExhaustionThresholdPercent": 90,
///   "PoolQueueTimeoutSeconds": 30,
///   "AiQueueConcurrency": 10,
///   "AiQueueBackPressureThreshold": 100
/// }
/// </code>
/// </summary>
public sealed class ConcurrencyOptions
{
    public const string SectionName = "Concurrency";

    /// <summary>
    /// Maximum number of concurrent PostgreSQL connections in the Npgsql pool.
    /// Must match the <c>Maximum Pool Size</c> value in the connection string. Default: 100.
    /// </summary>
    public int MaxDbConnections { get; set; } = 100;

    /// <summary>
    /// Pool utilization percentage above which the pool is considered exhausted
    /// and a warning is emitted (not a 503 — Npgsql's 30s Timeout handles the actual wait).
    /// Default: 90 (90%).
    /// </summary>
    public int PoolExhaustionThresholdPercent { get; set; } = 90;

    /// <summary>
    /// Seconds a new connection request waits in Npgsql's internal queue when
    /// all pool slots are in use.  Must match <c>Timeout=N</c> in the connection string.
    /// Default: 30.
    /// </summary>
    public int PoolQueueTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of simultaneous AI workload jobs processed by
    /// <c>BackgroundAiQueueProcessor</c>.
    /// Default: 10.
    /// </summary>
    public int AiQueueConcurrency { get; set; } = 10;

    /// <summary>
    /// Redis queue depth at which back-pressure is activated.
    /// When the queue contains more than this many pending items, new enqueue
    /// attempts receive HTTP 429 Retry-After.
    /// Default: 100.
    /// </summary>
    public int AiQueueBackPressureThreshold { get; set; } = 100;
}
