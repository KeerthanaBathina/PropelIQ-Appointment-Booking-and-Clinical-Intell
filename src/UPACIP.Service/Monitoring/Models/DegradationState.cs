namespace UPACIP.Service.Monitoring.Models;

/// <summary>
/// Represents the operating mode of the system (US_083 task_002, AC-1).
/// </summary>
public enum SystemMode
{
    /// <summary>All dependencies healthy — full feature set available.</summary>
    Normal,

    /// <summary>One or more dependencies degraded — AI features may be unavailable.</summary>
    Degraded,

    /// <summary>Scheduled maintenance window is active.</summary>
    MaintenanceMode,
}

/// <summary>
/// Snapshot of the current degradation state returned by
/// <see cref="IDegradationModeManager.GetCurrentState"/> (US_083 task_002, AC-1).
/// </summary>
public sealed record DegradationState
{
    /// <summary>Current operating mode.</summary>
    public SystemMode Mode { get; init; }

    /// <summary>Per-category health flags.  <c>true</c> = healthy, <c>false</c> = degraded.</summary>
    public IReadOnlyDictionary<DependencyCategory, bool> DependencyHealth { get; init; }
        = new Dictionary<DependencyCategory, bool>();

    /// <summary>
    /// Per-feature availability flags derived from <see cref="DependencyHealth"/>
    /// and the configured <c>FeatureDependencyMap</c>.
    /// </summary>
    public IReadOnlyDictionary<string, bool> FeatureAvailability { get; init; }
        = new Dictionary<string, bool>();

    /// <summary>UTC instant when the system first entered <see cref="SystemMode.Degraded"/>; <c>null</c> when <see cref="SystemMode.Normal"/>.</summary>
    public DateTime? DegradedSince { get; init; }
}
