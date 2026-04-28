namespace UPACIP.Service.Infrastructure;

/// <summary>
/// Contract for Npgsql connection pool utilization monitoring
/// (US_082 task_001, AC-2, edge case 1 — pool exhaustion).
/// </summary>
public interface IConnectionPoolMonitor
{
    /// <summary>
    /// Returns a snapshot of current connection pool statistics.
    /// </summary>
    /// <returns>
    /// <see cref="PoolStatistics"/> snapshot; returns zeroed snapshot when
    /// pool statistics are unavailable (e.g., first request before any connections opened).
    /// </returns>
    PoolStatistics GetStatistics();

    /// <summary>
    /// Returns <see langword="true"/> when the connection pool utilization
    /// has exceeded the configured exhaustion threshold.
    /// </summary>
    bool IsPoolExhausted();
}

/// <summary>
/// Immutable snapshot of Npgsql connection pool statistics.
/// </summary>
/// <param name="Active">Number of connections currently checked out.</param>
/// <param name="Idle">Number of open but available connections in the pool.</param>
/// <param name="Total">Active + Idle.</param>
/// <param name="MaxConnections">Configured <c>Maximum Pool Size</c>.</param>
public sealed record PoolStatistics(
    int   Active,
    int   Idle,
    int   Total,
    int   MaxConnections)
{
    /// <summary>Active connections as a percentage of MaxConnections.</summary>
    public float UtilizationPercent =>
        MaxConnections > 0 ? (float)Active / MaxConnections * 100f : 0f;
}
