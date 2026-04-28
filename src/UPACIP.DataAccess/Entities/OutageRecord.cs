namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Lifecycle record for a detected system outage (US_083 task_001, AC-3, NFR-019).
///
/// <para>
/// A new <see cref="OutageRecord"/> is created whenever any monitored dependency transitions
/// from <c>Healthy</c> to <c>Unhealthy</c>.  The <see cref="ResolvedAt"/> field is populated
/// when the dependency recovers.  Only unresolved records (null <see cref="ResolvedAt"/>)
/// represent active outages.
/// </para>
///
/// <para>
/// Impact classification reflects the criticality of the affected dependency:
/// <c>Critical</c> (database), <c>Major</c> (redis), <c>Minor</c> (all other dependencies).
/// </para>
///
/// <para>
/// <see cref="AlertSentAt"/> is set when the <c>OUTAGE_DETECTED</c> Serilog structured-log
/// event is emitted, providing an audit trail of notification timing.
/// </para>
/// </summary>
public sealed class OutageRecord
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC instant when the outage was first detected.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC instant when all affected dependencies recovered; null while still active.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Comma-separated list of affected dependency names (e.g. <c>database,redis</c>).</summary>
    public string AffectedServices { get; set; } = string.Empty;

    /// <summary>
    /// Impact classification: <c>Critical</c>, <c>Major</c>, or <c>Minor</c>.
    /// Determined by the highest-severity affected dependency.
    /// </summary>
    public string ImpactLevel { get; set; } = "Minor";

    /// <summary>UTC instant when the outage alert was emitted via Serilog structured logging.</summary>
    public DateTime? AlertSentAt { get; set; }
}
