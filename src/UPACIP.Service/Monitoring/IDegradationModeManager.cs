using UPACIP.Service.Monitoring.Models;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Manages the system degradation state and per-feature availability flags
/// (US_083 task_002, AC-1, AC-2, AC-3).
///
/// <para>
/// The manager tracks health per <see cref="DependencyCategory"/> and derives feature
/// availability from the configured <c>FeatureDependencyMap</c>.  It is the single source
/// of truth consulted by <see cref="GracefulDegradationMiddleware"/> on every request.
/// </para>
/// </summary>
public interface IDegradationModeManager
{
    /// <summary>
    /// Returns a point-in-time snapshot of the current degradation state.
    /// Never throws; always returns a valid <see cref="DegradationState"/>.
    /// </summary>
    DegradationState GetCurrentState();

    /// <summary>
    /// Marks <paramref name="category"/> as unhealthy and transitions the system to
    /// <see cref="SystemMode.Degraded"/> if it was previously <see cref="SystemMode.Normal"/>.
    /// Emits <c>DEGRADATION_ACTIVATED</c> and optionally <c>STAFF_NOTIFICATION</c> log events.
    /// </summary>
    void ActivateDegradation(DependencyCategory category);

    /// <summary>
    /// Marks <paramref name="category"/> as healthy.  If all categories are healthy,
    /// transitions the system back to <see cref="SystemMode.Normal"/> and emits a recovery
    /// <c>STAFF_NOTIFICATION</c> log event.
    /// </summary>
    void DeactivateDegradation(DependencyCategory category);

    /// <summary>
    /// Returns <c>true</c> when all dependency categories required by
    /// <paramref name="featureName"/> (as configured in <c>FeatureDependencyMap</c>) are healthy.
    /// Unknown features default to <c>true</c> (fail-open).
    /// </summary>
    bool IsFeatureAvailable(string featureName);

    /// <summary>Returns <c>true</c> when <paramref name="category"/> is currently healthy.</summary>
    bool GetDependencyStatus(DependencyCategory category);
}
