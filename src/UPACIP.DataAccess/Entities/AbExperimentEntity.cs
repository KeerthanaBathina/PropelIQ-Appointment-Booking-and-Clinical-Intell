using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// EF Core entity backing the <c>ab_experiments</c> table.
/// Stores A/B experiment definitions for AI model version comparison
/// (US_080 task_001, AC-1, AIR-O10).
/// </summary>
public sealed class AbExperimentEntity : BaseEntity
{
    /// <summary>Model identifier for the control (current) variant.</summary>
    public string ControlModelId { get; set; } = string.Empty;

    /// <summary>Model identifier for the candidate (new) variant under evaluation.</summary>
    public string CandidateModelId { get; set; } = string.Empty;

    /// <summary>Percentage of requests (1–99) routed to the candidate model.</summary>
    public int TrafficSplitPercentage { get; set; }

    /// <summary>
    /// Lifecycle status stored as string for readability in raw SQL.
    /// Values: Active, Paused, Terminated, Completed.
    /// </summary>
    public string Status { get; set; } = "Active";

    /// <summary>UTC datetime when the experiment was activated.</summary>
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    /// <summary>UTC datetime when the experiment ended. Null while running.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Human-readable description of what is being tested.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>User ID of the admin who created the experiment.</summary>
    public string CreatedByUserId { get; set; } = string.Empty;
}
