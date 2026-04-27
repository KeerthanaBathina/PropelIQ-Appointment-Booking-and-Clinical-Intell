namespace UPACIP.Service.Admin;

/// <summary>
/// Current-snapshot response returned by GET /api/admin/metrics (US_058 AC-1).
/// Wraps the metrics values with staleness metadata for the "Data as of [ts]"
/// edge-case banner on the frontend.
/// </summary>
public sealed record AdminMetricsResponse(
    AdminMetricsSnapshot Metrics,
    DateTime             GeneratedAt,
    bool                 IsStale);

/// <summary>
/// The five key platform metrics displayed on SCR-015.
/// </summary>
/// <param name="ActiveUsers">Users with a login in the last 15 minutes.</param>
/// <param name="DailyAppointments">Non-cancelled appointments scheduled for today.</param>
/// <param name="NoShowRate">No-show percentage over the last 30 days [0.0–100.0].</param>
/// <param name="AiAgreementRate">Percentage of AI-suggested codes that have been approved [0.0–100.0].</param>
/// <param name="UptimePercent">Estimated platform uptime percentage [0.0–100.0].</param>
public sealed record AdminMetricsSnapshot(
    int     ActiveUsers,
    int     DailyAppointments,
    decimal NoShowRate,
    decimal AiAgreementRate,
    decimal UptimePercent);

/// <summary>
/// One data point in the rolling trend response (US_058 AC-2).
/// Each row covers a single calendar day with all five metric values so the
/// frontend can plot multiple series from a single request.
/// </summary>
public sealed record MetricTrendPoint(
    DateOnly Date,
    int      ActiveUsers,
    int      DailyAppointments,
    decimal  NoShowRate,
    decimal  AiAgreementRate,
    decimal  UptimePercent);

/// <summary>
/// Full trend response wrapper (mirrors the <c>AdminMetricsTrendsResponse</c> FE type).
/// </summary>
public sealed record AdminMetricsTrendsResponse(
    string                    Period,
    IReadOnlyList<MetricTrendPoint> DataPoints,
    DateTime                  GeneratedAt);
