namespace UPACIP.Service.Monitoring.Models;

/// <summary>
/// Strongly-typed configuration for the uptime monitoring and alerting framework
/// (US_083 task_001, AC-1, AC-3, AC-4).
///
/// <para>Bound from the <c>Monitoring</c> section in <c>appsettings.json</c>.</para>
/// </summary>
public sealed class MonitoringOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Interval between health probe cycles in seconds.
    /// Default: 30 s. Worst-case alert latency = one probe interval (AC-3: alert within 1 min).
    /// </summary>
    public int ProbeIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Rolling window for uptime percentage computation in days.
    /// Default: 30 days (NFR-019 AC-1 measurement period).
    /// </summary>
    public int UptimeWindowDays { get; set; } = 30;

    /// <summary>
    /// Error rate threshold that triggers an alert, expressed as a percentage.
    /// Default: 0.1% (NFR-031 AC-4).
    /// </summary>
    public double ErrorRateThresholdPercent { get; set; } = 0.1;

    /// <summary>
    /// Sliding window duration for error rate computation in seconds.
    /// Default: 300 s (5 minutes).
    /// </summary>
    public int ErrorRateWindowSeconds { get; set; } = 300;

    /// <summary>Pre-configured maintenance windows during which outage alerts are suppressed.</summary>
    public List<MaintenanceWindow> MaintenanceWindows { get; set; } = [];
}

/// <summary>
/// Defines a recurring weekly maintenance window in UTC time (edge case: planned maintenance).
/// </summary>
public sealed class MaintenanceWindow
{
    /// <summary>Day of the week for this maintenance window.</summary>
    public DayOfWeek Day { get; set; }

    /// <summary>Start time in UTC (e.g. <c>02:00:00</c>).</summary>
    public TimeSpan StartUtc { get; set; }

    /// <summary>End time in UTC (e.g. <c>04:00:00</c>).</summary>
    public TimeSpan EndUtc { get; set; }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="utcNow"/> falls within this window.
    /// </summary>
    public bool IsActive(DateTime utcNow) =>
        utcNow.DayOfWeek == Day
        && utcNow.TimeOfDay >= StartUtc
        && utcNow.TimeOfDay < EndUtc;
}
