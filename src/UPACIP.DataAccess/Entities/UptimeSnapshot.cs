namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Rolling 30-day availability snapshot recorded by the uptime monitoring probe every
/// 30 seconds (US_083 task_001, AC-1, NFR-019).
///
/// <para>
/// Each row captures the aggregate health state at the probe instant, a JSON serialization
/// of per-dependency statuses (database, redis, tls-certificate, audit-queue), and a flag
/// indicating whether the probe occurred inside a pre-configured maintenance window.
/// Maintenance-window rows are excluded from the uptime percentage computation so planned
/// downtime does not count against the 99.9% SLA target.
/// </para>
///
/// <para>
/// Retention: rows older than 90 days are pruned by the monitoring service every 100th
/// probe cycle to bound table growth (86 400 rows / 30-day window at 30-second intervals).
/// The <see cref="Timestamp"/> index supports efficient date-range scans for rolling-window
/// queries.
/// </para>
/// </summary>
public sealed class UptimeSnapshot
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC instant when the probe was recorded.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <see langword="true"/> when all probed dependencies reported <c>Healthy</c>; otherwise
    /// <see langword="false"/> (includes <c>Degraded</c> and <c>Unhealthy</c>).
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// JSON-serialized <c>Dictionary&lt;string, string&gt;</c> of per-dependency statuses
    /// (e.g. <c>{"database":"Healthy","redis":"Unhealthy"}</c>).
    /// </summary>
    public string DependencyStatusesJson { get; set; } = "{}";

    /// <summary>
    /// <see langword="true"/> when the probe occurred inside a pre-configured maintenance window.
    /// Maintenance rows are excluded from uptime percentage calculations.
    /// </summary>
    public bool IsMaintenanceWindow { get; set; }
}
