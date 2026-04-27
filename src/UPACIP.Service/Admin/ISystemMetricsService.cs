namespace UPACIP.Service.Admin;

/// <summary>
/// Contract for aggregating system-wide metrics for the Admin Dashboard (US_058 AC-1, AC-2).
///
/// Both methods cache their results in Redis with a 5-minute TTL (NFR-030).
/// On DB computation failure the last cached snapshot is returned with <c>IsStale = true</c>.
/// </summary>
public interface ISystemMetricsService
{
    /// <summary>
    /// Returns the current system metrics snapshot (active users, daily appointments,
    /// no-show rate, AI agreement rate, uptime percentage).
    ///
    /// Cache key: <c>admin:metrics:current</c>  TTL: 5 minutes.
    /// </summary>
    Task<AdminMetricsResponse> GetCurrentMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns rolling trend data for the requested period.
    /// Each element in <see cref="AdminMetricsTrendsResponse.DataPoints"/> covers one calendar day.
    ///
    /// Cache key: <c>admin:metrics:trends:{period}</c>  TTL: 5 minutes.
    /// </summary>
    /// <param name="period">Number of days to look back — <c>7</c> or <c>30</c>.</param>
    Task<AdminMetricsTrendsResponse> GetTrendDataAsync(int period, CancellationToken ct = default);
}
