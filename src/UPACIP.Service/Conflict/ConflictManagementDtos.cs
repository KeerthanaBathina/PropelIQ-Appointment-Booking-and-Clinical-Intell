using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Conflict;

/// <summary>
/// DTO containing the escalation results after processing a batch of newly detected conflicts
/// for urgent triage (US_044, AC-3, FR-053).
///
/// Returned by <see cref="IConflictManagementService.EscalateUrgentConflictsAsync"/> to inform
/// the caller how many conflicts were promoted to urgent status.
/// </summary>
public sealed record ConflictEscalationResult
{
    /// <summary>Total number of conflicts evaluated during the escalation pass.</summary>
    public int EvaluatedCount { get; init; }

    /// <summary>Number of conflicts promoted to <c>is_urgent = true</c> during this call.</summary>
    public int EscalatedCount { get; init; }

    /// <summary>IDs of the conflicts that were marked urgent.</summary>
    public IReadOnlyList<Guid> EscalatedConflictIds { get; init; } = [];
}

/// <summary>
/// Request DTO for resolving or dismissing a clinical conflict (US_044, FR-053).
///
/// Consumed by <see cref="IConflictManagementService.ResolveConflictAsync"/> and
/// <see cref="IConflictManagementService.DismissConflictAsync"/>.
/// </summary>
public sealed record ConflictResolutionRequest
{
    /// <summary>ID of the <c>ClinicalConflict</c> row being resolved or dismissed.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>
    /// Staff member closing this conflict.
    /// Must match a valid <c>ApplicationUser.Id</c>.
    /// </summary>
    public Guid ResolvedByUserId { get; init; }

    /// <summary>
    /// Free-text explanation recorded as the resolution or dismissal rationale.
    /// Required — staff must document why the conflict is being closed.
    /// </summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}

/// <summary>
/// A single entry in the conflict review queue (US_044, AC-3, FR-053).
///
/// Returned as a page of results by <see cref="IConflictManagementService.GetReviewQueueAsync"/>.
/// The queue is ordered: urgent items first (is_urgent = true), then by creation date descending.
/// </summary>
public sealed record ReviewQueueEntry
{
    /// <summary><c>ClinicalConflict.Id</c> — use to fetch full details or resolve.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>Patient whose records contain the conflict.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Patient display name for the review queue list.</summary>
    public string PatientName { get; init; } = string.Empty;

    /// <summary>Conflict category (MedicationContraindication, DuplicateDiagnosis, etc.).</summary>
    public ConflictType ConflictType { get; init; }

    /// <summary>Clinical severity driving review priority.</summary>
    public ConflictSeverity Severity { get; init; }

    /// <summary>Current lifecycle state of this conflict.</summary>
    public ConflictStatus Status { get; init; }

    /// <summary>
    /// True when this conflict should be shown at the top of the queue with an URGENT badge (AC-3).
    /// </summary>
    public bool IsUrgent { get; init; }

    /// <summary>AI-generated human-readable summary of the conflict.</summary>
    public string ConflictDescription { get; init; } = string.Empty;

    /// <summary>AI confidence score in [0.0, 1.0] for this conflict detection.</summary>
    public float AiConfidenceScore { get; init; }

    /// <summary>UTC timestamp when the conflict was first detected.</summary>
    public DateTime DetectedAt { get; init; }

    /// <summary>Number of source documents involved in this conflict.</summary>
    public int SourceDocumentCount { get; init; }
}

/// <summary>
/// Paged result wrapper for the conflict review queue (US_044, FR-053).
/// </summary>
public sealed record ReviewQueuePage
{
    /// <summary>Ordered list of queue entries for the requested page.</summary>
    public IReadOnlyList<ReviewQueueEntry> Items { get; init; } = [];

    /// <summary>Total number of open conflicts across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page index (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; }
}
