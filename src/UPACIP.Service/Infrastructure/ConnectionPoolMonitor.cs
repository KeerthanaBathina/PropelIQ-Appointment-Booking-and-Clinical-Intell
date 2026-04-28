using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using UPACIP.Service.Infrastructure.Models;
using UPACIP.Service.Performance;

namespace UPACIP.Service.Infrastructure;

/// <summary>
/// <see cref="IConnectionPoolMonitor"/> implementation that samples the PostgreSQL
/// <c>pg_stat_activity</c> system view to report live pool utilization (US_082 task_001, AC-2).
///
/// <para>
/// <b>Sampling strategy:</b> A background <see cref="System.Threading.Timer"/> polls the
/// database every 10 seconds.  <see cref="GetStatistics"/> and <see cref="IsPoolExhausted"/>
/// always return the last cached snapshot so they never block an HTTP request thread.
/// </para>
///
/// <para>
/// <b>Exhaustion detection:</b> If a monitoring connection cannot be obtained within 3 seconds
/// (all pool slots busy), the monitor conservatively treats the pool as fully loaded and sets
/// <c>Active = MaxDbConnections</c> in the cached snapshot.
/// </para>
///
/// <para>
/// <b>Exhaustion threshold:</b> Configurable via <c>Concurrency:PoolExhaustionThresholdPercent</c>
/// (default 90%).  At ≥80% a Warning is logged; at ≥threshold <see cref="IsPoolExhausted"/>
/// returns <see langword="true"/>.
/// </para>
///
/// <para>Singleton lifetime — thread-safe via <see langword="volatile"/> snapshot field.</para>
/// </summary>
public sealed class ConnectionPoolMonitor : IConnectionPoolMonitor, IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const float WarnThresholdPercent       = 80f;
    private const int   MonitoringConnectTimeoutMs = 3_000;
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(10);

    // SQL: count active backend connections in this database, excluding this query's own PID.
    private const string StatsSql =
        "SELECT count(*)::int " +
        "FROM pg_stat_activity " +
        "WHERE datname = current_database() AND pid <> pg_backend_pid()";

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly NpgsqlDataSource                   _dataSource;
    private readonly ConcurrencyOptions                 _options;
    private readonly IPerformanceTracker                _tracker;
    private readonly ILogger<ConnectionPoolMonitor>     _logger;
    private readonly Timer                              _timer;

    // Cached snapshot — written by the background timer, read by GetStatistics().
    private volatile PoolStatistics _snapshot;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ConnectionPoolMonitor(
        NpgsqlDataSource                  dataSource,
        IOptions<ConcurrencyOptions>      options,
        IPerformanceTracker               tracker,
        ILogger<ConnectionPoolMonitor>    logger)
    {
        _dataSource = dataSource;
        _options    = options.Value;
        _tracker    = tracker;
        _logger     = logger;
        _snapshot   = new PoolStatistics(0, 0, 0, _options.MaxDbConnections);

        // Start background sampling.  First tick fires after SamplingInterval so startup
        // is not blocked; subsequent ticks repeat on the same interval.
        _timer = new Timer(RefreshCallback, null, SamplingInterval, SamplingInterval);
    }

    // ── IConnectionPoolMonitor ────────────────────────────────────────────────

    /// <inheritdoc/>
    public PoolStatistics GetStatistics() => _snapshot;

    /// <inheritdoc/>
    public bool IsPoolExhausted() =>
        _snapshot.UtilizationPercent >= _options.PoolExhaustionThresholdPercent;

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _timer.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Timer callback — runs on a thread-pool thread every 10 seconds.</summary>
    private async void RefreshCallback(object? state)
    {
        PoolStatistics updated;

        using var cts = new CancellationTokenSource(MonitoringConnectTimeoutMs);
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cts.Token);
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText      = StatsSql;
            var active           = (int)(await cmd.ExecuteScalarAsync(cts.Token) ?? 0);
            var idle             = Math.Max(0, _options.MaxDbConnections - active);

            updated = new PoolStatistics(
                Active:         active,
                Idle:           idle,
                Total:          active + idle,
                MaxConnections: _options.MaxDbConnections);
        }
        catch (OperationCanceledException)
        {
            // Could not obtain a monitoring connection within 3 s → pool is under heavy pressure.
            updated = new PoolStatistics(
                Active:         _options.MaxDbConnections,
                Idle:           0,
                Total:          _options.MaxDbConnections,
                MaxConnections: _options.MaxDbConnections);

            _logger.LogWarning(
                "ConnectionPoolMonitor: could not obtain a monitoring connection within {TimeoutMs} ms " +
                "— treating pool as exhausted. MaxConnections={Max}",
                MonitoringConnectTimeoutMs, _options.MaxDbConnections);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ConnectionPoolMonitor: pool stats query failed — retaining previous snapshot.");
            return; // Keep stale snapshot rather than zeroing out.
        }

        _snapshot = updated;

        // Emit utilization as a latency metric (range 0–100) for P95 tracking.
        _tracker.RecordLatency("db.pool_utilization", (long)updated.UtilizationPercent);

        if (updated.UtilizationPercent >= WarnThresholdPercent)
        {
            _logger.LogWarning(
                "Connection pool utilization {Utilization:F1}% — approaching limit of " +
                "{MaxConnections} connections. Active={Active} Idle={Idle}",
                updated.UtilizationPercent, updated.MaxConnections,
                updated.Active, updated.Idle);
        }
    }
}
