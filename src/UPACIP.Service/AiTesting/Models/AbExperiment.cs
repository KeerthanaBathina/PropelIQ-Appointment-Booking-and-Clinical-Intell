namespace UPACIP.Service.AiTesting.Models;

/// <summary>Lifecycle status of an A/B experiment (US_080 task_001, AC-1).</summary>
public enum AbExperimentStatus
{
    /// <summary>Requests are actively split between control and candidate models.</summary>
    Active = 0,

    /// <summary>All traffic temporarily routed to the control model; data retained.</summary>
    Paused = 1,

    /// <summary>Immediately stopped by an admin; all traffic reverted to control.</summary>
    Terminated = 2,

    /// <summary>Experiment ran to its natural end date.</summary>
    Completed = 3,
}

/// <summary>Which variant of the experiment a user was assigned to.</summary>
public enum AbVariant
{
    /// <summary>The current / existing model version.</summary>
    Control = 0,

    /// <summary>The new / candidate model version under evaluation.</summary>
    Candidate = 1,
}

/// <summary>
/// An A/B experiment definition routing a percentage of AI requests to a candidate model
/// (US_080 task_001, AC-1, AIR-O10).
/// </summary>
public sealed class AbExperiment
{
    /// <summary>Unique experiment identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Model identifier for the control (current) variant
    /// (e.g. <c>gpt-4o-mini-2024-07-18</c>).
    /// </summary>
    public string ControlModelId { get; init; } = string.Empty;

    /// <summary>
    /// Model identifier for the candidate (new) variant under evaluation
    /// (e.g. <c>gpt-4o-mini-2025-01-31</c>).
    /// Must differ from <see cref="ControlModelId"/>.
    /// </summary>
    public string CandidateModelId { get; init; } = string.Empty;

    /// <summary>
    /// Percentage of requests (1–99) routed to the <see cref="AbVariant.Candidate"/> model.
    /// The remainder are routed to <see cref="AbVariant.Control"/>.
    /// </summary>
    public int TrafficSplitPercentage { get; init; }

    /// <summary>Current lifecycle status of the experiment.</summary>
    public AbExperimentStatus Status { get; init; } = AbExperimentStatus.Active;

    /// <summary>UTC datetime when the experiment was activated.</summary>
    public DateTime StartDate { get; init; } = DateTime.UtcNow;

    /// <summary>UTC datetime when the experiment was or will be ended. Null while running.</summary>
    public DateTime? EndDate { get; init; }

    /// <summary>Human-readable description of what is being tested.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>User ID of the admin who created the experiment (for audit).</summary>
    public string CreatedByUserId { get; init; } = string.Empty;
}
