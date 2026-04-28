using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Contract for rolling 30-day uptime tracking against the 99.9% SLA target (US_083 task_001, AC-1, NFR-019).
/// </summary>
public interface IUptimeTracker
{
    /// <summary>
    /// Persists a new <see cref="UptimeSnapshot"/> for the current probe cycle.
    /// </summary>
    /// <param name="isHealthy">
    /// <see langword="true"/> when the aggregate health status is <c>Healthy</c>.
    /// </param>
    /// <param name="dependencyStatuses">
    /// Per-dependency status strings keyed by dependency name
    /// (e.g. <c>{"database":"Healthy","redis":"Unhealthy"}</c>).
    /// </param>
    /// <param name="isMaintenanceWindow">
    /// <see langword="true"/> when the probe occurred inside a pre-configured maintenance window.
    /// Maintenance rows are excluded from uptime percentage computation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordSnapshotAsync(
        bool isHealthy,
        Dictionary<string, string> dependencyStatuses,
        bool isMaintenanceWindow = false,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the uptime percentage over the last <paramref name="windowDays"/> days,
    /// excluding maintenance-window snapshots from both numerator and denominator.
    /// Returns 100.0 when no non-maintenance snapshots exist in the window.
    /// </summary>
    Task<double> GetUptimePercentageAsync(int windowDays = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recently recorded <see cref="UptimeSnapshot"/>, or
    /// <see langword="null"/> when no snapshots have been recorded yet.
    /// </summary>
    Task<UptimeSnapshot?> GetCurrentStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes snapshots older than <paramref name="retentionDays"/> to bound table growth.
    /// Called by the monitoring service every 100th probe cycle.
    /// </summary>
    Task PruneOldSnapshotsAsync(int retentionDays = 90, CancellationToken ct = default);
}
