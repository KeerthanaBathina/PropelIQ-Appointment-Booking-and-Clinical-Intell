using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Contract for state-transition outage detection and alert generation (US_083 task_001, AC-3).
/// </summary>
public interface IOutageAlertService
{
    /// <summary>
    /// Evaluates the current dependency statuses against the previously known state,
    /// creates or resolves <see cref="OutageRecord"/> rows, and emits Serilog structured alerts.
    /// </summary>
    /// <param name="currentStatuses">
    /// Per-dependency status strings keyed by dependency name
    /// (e.g. <c>{"database":"Healthy","redis":"Unhealthy"}</c>).
    /// </param>
    /// <param name="isMaintenance">
    /// <see langword="true"/> when the current probe is inside a maintenance window;
    /// outage alerts are suppressed but state transitions are still tracked.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task EvaluateHealthTransitionAsync(
        Dictionary<string, string> currentStatuses,
        bool isMaintenance = false,
        CancellationToken ct = default);

    /// <summary>Returns all <see cref="OutageRecord"/> rows without a <c>ResolvedAt</c> timestamp.</summary>
    Task<IReadOnlyList<OutageRecord>> GetActiveOutagesAsync(CancellationToken ct = default);
}
