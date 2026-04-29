using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Tracks the last known health state for a single dependency (US_099, AC-4).
///
/// Used by <see cref="HealthStateMonitorService"/> to detect transitions between
/// <see cref="HealthStatus"/> values.  One record is held per dependency name and an
/// additional synthetic <c>"_overall"</c> record tracks the aggregate report status.
/// </summary>
public sealed class HealthStateRecord
{
    /// <summary>The dependency name as registered with the health-check framework.</summary>
    public string DependencyName { get; init; } = string.Empty;

    /// <summary>Status recorded on the previous polling cycle.</summary>
    public HealthStatus PreviousStatus { get; set; }

    /// <summary>Status recorded on the most-recent polling cycle.</summary>
    public HealthStatus CurrentStatus { get; set; }

    /// <summary>UTC timestamp when the current status was first observed.</summary>
    public DateTime LastChangedUtc { get; set; }

    /// <summary>Human-readable description returned by the health check, if any.</summary>
    public string? Description { get; set; }
}
