using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records each patient profile consolidation event with full audit provenance (US_043, AC-2, FR-056).
///
/// A new version row is created every time the consolidation service merges clinical documents
/// into the patient's consolidated profile.  This entity provides the version-history backbone
/// required by FR-056 and serves as the target for the staff conflict-resolution workflow
/// defined in US_044 and US_045.
///
/// Design notes:
///   - <see cref="VersionNumber"/> is a per-patient monotonically increasing integer assigned
///     at write time (NOT a DB sequence); the unique constraint (patient_id, version_number)
///     prevents duplicates.
///   - <see cref="SourceDocumentIds"/> is stored as a JSONB array of UUIDs so the consolidation
///     service can record an arbitrary number of contributing documents without a junction table.
///   - <see cref="DataSnapshot"/> is a free-form JSONB string containing only the delta of
///     fields that changed during this consolidation event.  The full merged profile is derived
///     by replaying all versions in order; the snapshot accelerates diff-display in the UI.
///   - <see cref="ConsolidatedByUserId"/> is nullable: NULL means the consolidation was
///     triggered automatically by the background processing pipeline (no staff user involved).
///   - Verification lifecycle fields (<c>verification_status</c>, <c>verified_by_user_id</c>,
///     <c>verified_at</c>) are added by US_045 task_001 to keep this migration focused and
///     backward-compatible.
/// </summary>
public sealed class PatientProfileVersion : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> whose profile was consolidated.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Per-patient monotonically increasing version counter.
    /// Version 1 always corresponds to <see cref="ConsolidationType.Initial"/>.
    /// Combined unique constraint with <see cref="PatientId"/> prevents duplicate versions.
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> (staff member) who triggered or approved
    /// the consolidation.  NULL when the event was initiated by the automated pipeline.
    /// </summary>
    public Guid? ConsolidatedByUserId { get; set; }

    /// <summary>
    /// Whether this version represents a first-time profile creation or an incremental
    /// update merging new documents into an existing profile.
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ConsolidationType ConsolidationType { get; set; }

    /// <summary>
    /// JSONB array of <see cref="ClinicalDocument"/> UUIDs that contributed to this version.
    /// Preserves the exact document set that produced the merged state (AC-2 source attribution).
    /// </summary>
    public List<Guid> SourceDocumentIds { get; set; } = [];

    /// <summary>
    /// JSONB string containing the data delta produced by this consolidation event.
    ///
    /// Structure (serialized JSON object):
    /// <code>
    /// {
    ///   "changed": { "fieldKey": { "from": "oldValue", "to": "newValue" } },
    ///   "added":   { "fieldKey": "newValue" },
    ///   "removed": [ "fieldKey" ]
    /// }
    /// </code>
    ///
    /// NULL for the initial consolidation where every field is "added" (no prior state).
    /// The full merged profile is derived by replaying all version deltas in order.
    /// </summary>
    public string? DataSnapshot { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>The patient whose profile this version belongs to.</summary>
    public Patient Patient { get; set; } = null!;

    /// <summary>
    /// Staff user who initiated the consolidation.
    /// Null when the event was triggered by the automated pipeline.
    /// </summary>
    public ApplicationUser? ConsolidatedByUser { get; set; }

    // -------------------------------------------------------------------------
    // Verification lifecycle fields (US_045, AC-4, FR-054)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Current staff verification state for this profile version.
    /// Defaults to <see cref="ProfileVerificationStatus.Unverified"/> for all new versions.
    /// Transitions to <see cref="ProfileVerificationStatus.PartiallyVerified"/> as conflicts
    /// are resolved and to <see cref="ProfileVerificationStatus.Verified"/> when all conflicts
    /// for this version are closed (AC-4).
    /// Persisted as VARCHAR for forward-compatible enum extension.
    /// </summary>
    public ProfileVerificationStatus VerificationStatus { get; set; } = ProfileVerificationStatus.Unverified;

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> (staff member) who completed final verification
    /// by resolving the last open conflict (AC-4).
    /// NULL until the profile version reaches <see cref="ProfileVerificationStatus.Verified"/>.
    /// </summary>
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>
    /// UTC timestamp when the profile version was transitioned to
    /// <see cref="ProfileVerificationStatus.Verified"/> (AC-4).
    /// NULL until all conflicts are resolved.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// Staff user who completed final verification.
    /// Null until the profile version reaches <see cref="ProfileVerificationStatus.Verified"/>.
    /// </summary>
    public ApplicationUser? VerifiedByUser { get; set; }
}
