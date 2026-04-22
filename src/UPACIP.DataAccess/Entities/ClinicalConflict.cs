using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persists a clinical data conflict detected by the AI conflict detection service
/// (US_044, AC-2, AC-3, AC-5, FR-053).
///
/// A conflict record is created when the AI pipeline identifies an inconsistency between
/// two or more extracted data points or clinical documents for the same patient.  The record
/// captures the full conflict lifecycle — from initial detection through staff review to
/// resolution or dismissal — enabling the review queue and side-by-side comparison workflows
/// required by FR-053.
///
/// Design notes:
///   - <see cref="SourceExtractedDataIds"/> and <see cref="SourceDocumentIds"/> are stored as
///     JSONB arrays so the detection service can reference an arbitrary number of source records
///     without a junction table while preserving source attribution (AC-2, AC-3, AC-5).
///   - <see cref="IsUrgent"/> is set to <c>true</c> by the service for
///     <see cref="ConflictType.MedicationContraindication"/> conflicts, causing them to be moved
///     to the top of the review queue with an URGENT indicator (AC-3).
///   - <see cref="ResolvedByUserId"/>, <see cref="ResolutionNotes"/>, and <see cref="ResolvedAt"/>
///     are nullable; they are populated only when a staff member closes the conflict.
///   - <see cref="ProfileVersionId"/> links the conflict to the patient profile version that was
///     current at detection time, enabling version-correlated history queries (US_043/task_001).
/// </summary>
public sealed class ClinicalConflict : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> whose records contain the conflict.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Category of the detected conflict — drives detection logic and review-queue routing.
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ConflictType ConflictType { get; set; }

    /// <summary>
    /// Clinical severity of the conflict.
    /// Determines review priority and escalation path (AC-3).
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ConflictSeverity Severity { get; set; }

    /// <summary>
    /// Current lifecycle state of the conflict record.
    /// Defaults to <see cref="ConflictStatus.Detected"/> on creation.
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ConflictStatus Status { get; set; } = ConflictStatus.Detected;

    /// <summary>
    /// When <c>true</c>, the conflict is treated as an URGENT item and promoted to the
    /// top of the staff review queue (AC-3).
    /// Set by the detection service for <see cref="ConflictType.MedicationContraindication"/>
    /// conflicts; can also be set manually by staff for other severe conflicts.
    /// </summary>
    public bool IsUrgent { get; set; }

    /// <summary>
    /// JSONB array of <see cref="ExtractedData"/> UUIDs that are the source data points
    /// for this conflict.  Provides AC-2 / AC-5 source citations at the extraction level.
    /// </summary>
    public List<Guid> SourceExtractedDataIds { get; set; } = [];

    /// <summary>
    /// JSONB array of <see cref="ClinicalDocument"/> UUIDs whose content contributed to
    /// the detected conflict.  Provides AC-2 / AC-3 source citations at the document level.
    /// </summary>
    public List<Guid> SourceDocumentIds { get; set; } = [];

    /// <summary>
    /// Human-readable summary of the conflict written by the AI service
    /// (e.g. "Duplicate diagnosis: ICD-10 E11.9 found in documents from 2024-01-15 and 2024-03-22").
    /// </summary>
    public string ConflictDescription { get; set; } = string.Empty;

    /// <summary>
    /// Detailed AI-generated explanation of why this was flagged as a conflict,
    /// including supporting evidence and clinical reasoning.
    /// Used for the side-by-side comparison view (FR-053).
    /// </summary>
    public string AiExplanation { get; set; } = string.Empty;

    /// <summary>
    /// AI model confidence score in the range [0.0, 1.0].
    /// Higher values indicate greater certainty that a genuine conflict exists.
    /// </summary>
    public float AiConfidenceScore { get; set; }

    // -------------------------------------------------------------------------
    // Resolution workflow fields (US_045, AC-2, EC-2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// How the conflict was resolved by staff (US_045, AC-2, EC-2).
    /// NULL while the conflict is still open (<see cref="ConflictStatus.Detected"/> or
    /// <see cref="ConflictStatus.UnderReview"/>).
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ConflictResolutionType? ResolutionType { get; set; }

    /// <summary>
    /// FK to the <see cref="ExtractedData"/> record whose value was chosen by staff
    /// as the authoritative correct value (AC-2 — SelectedValue resolution path).
    /// NULL when the resolution type is <see cref="ConflictResolutionType.BothValid"/> or
    /// <see cref="ConflictResolutionType.Dismissed"/>, or while the conflict is open.
    /// </summary>
    public Guid? SelectedExtractedDataId { get; set; }

    /// <summary>
    /// Free-text explanation provided by staff when selecting
    /// <see cref="ConflictResolutionType.BothValid"/> (Edge Case — "Both Valid — Different Dates").
    /// Describes the distinct date attribution that justifies preserving both entries.
    /// NULL when the resolution type is <see cref="ConflictResolutionType.SelectedValue"/> or
    /// <see cref="ConflictResolutionType.Dismissed"/>.
    /// </summary>
    public string? BothValidExplanation { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> (staff member) who resolved or dismissed
    /// this conflict.  NULL until the conflict is closed by staff.
    /// </summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>
    /// Free-text notes recorded by the staff member when resolving or dismissing the conflict.
    /// NULL until the conflict is closed.
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// UTC timestamp when the conflict was resolved or dismissed.
    /// NULL until the conflict is closed by staff.
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// FK to the <see cref="PatientProfileVersion"/> that was current when this conflict
    /// was detected.  NULL when conflict detection runs outside a consolidation event.
    /// </summary>
    public Guid? ProfileVersionId { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>The patient whose records contain this conflict.</summary>
    public Patient Patient { get; set; } = null!;

    /// <summary>
    /// Staff user who resolved or dismissed the conflict.
    /// Null while the conflict is still open.
    /// </summary>
    public ApplicationUser? ResolvedByUser { get; set; }

    /// <summary>
    /// The patient profile version that was current at conflict detection time.
    /// Null when the conflict was detected outside a consolidation event.
    /// </summary>
    public PatientProfileVersion? ProfileVersion { get; set; }

    /// <summary>
    /// The extracted data record selected by staff as the authoritative correct value
    /// (SelectedValue resolution path, AC-2).  Null when the conflict is open or resolved
    /// via BothValid / Dismissed.
    /// </summary>
    public ExtractedData? SelectedExtractedData { get; set; }
}
