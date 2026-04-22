namespace UPACIP.Service.Conflict;

/// <summary>
/// Request DTO for the "select correct value" resolution path (US_045, AC-2).
///
/// Staff picks one of the conflicting <see cref="UPACIP.DataAccess.Entities.ExtractedData"/>
/// records as the authoritative value.  The chosen record's data is written to the
/// consolidated patient profile and the conflict is marked Resolved with SelectedValue type.
/// </summary>
public sealed record SelectValueRequest
{
    /// <summary>ID of the <c>ClinicalConflict</c> being resolved.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>
    /// ID of the <c>ExtractedData</c> record the staff member selected as correct.
    /// Must appear in <c>ClinicalConflict.SourceExtractedDataIds</c>.
    /// </summary>
    public Guid SelectedExtractedDataId { get; init; }

    /// <summary>ID of the staff user performing the resolution.</summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Staff rationale for selecting this value — required for the resolution audit trail.
    /// </summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}

/// <summary>
/// Request DTO for the "Both Valid — Different Dates" resolution path (US_045, EC-2).
///
/// Staff confirms that both conflicting values are clinically valid and represent distinct
/// events with different date attribution.  Both entries are preserved in the consolidated
/// profile and the conflict is marked Resolved with BothValid type.
/// </summary>
public sealed record BothValidRequest
{
    /// <summary>ID of the <c>ClinicalConflict</c> being resolved.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>ID of the staff user performing the resolution.</summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Staff explanation describing the distinct date contexts that justify preserving
    /// both entries.  Required — an empty explanation is rejected.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Progress snapshot for a patient's conflict resolution workflow (US_045, EC-1).
///
/// Returned by <see cref="IConflictResolutionService.GetResolutionProgressAsync"/> so the
/// staff UI can display a progress indicator and resume partial work in a new session.
/// </summary>
public sealed record ResolutionProgressDto
{
    /// <summary>ID of the patient this progress snapshot belongs to.</summary>
    public Guid PatientId { get; init; }

    /// <summary>
    /// Total number of <c>ClinicalConflict</c> records detected for this patient
    /// (all statuses included).
    /// </summary>
    public int TotalConflicts { get; init; }

    /// <summary>
    /// Number of conflicts already closed (Resolved or Dismissed).
    /// </summary>
    public int ResolvedCount { get; init; }

    /// <summary>
    /// Number of conflicts still open (Detected or UnderReview).
    /// </summary>
    public int RemainingCount { get; init; }

    /// <summary>
    /// Whole-number percentage of conflicts resolved, rounded down.
    /// Returns 0 when <see cref="TotalConflicts"/> is 0.
    /// </summary>
    public int PercentComplete { get; init; }

    /// <summary>
    /// Current verification status of the latest <c>PatientProfileVersion</c>.
    /// Reflects Unverified / PartiallyVerified / Verified (AC-4).
    /// </summary>
    public string VerificationStatus { get; init; } = "Unverified";
}
