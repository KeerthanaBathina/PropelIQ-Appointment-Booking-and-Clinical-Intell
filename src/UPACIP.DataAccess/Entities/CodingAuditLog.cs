using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Immutable append-only record of every staff action taken on a <see cref="MedicalCode"/>
/// (US_049, AC-2, AC-4, FR-066, HIPAA audit requirement).
/// <para>
/// Follows the same immutable pattern as <see cref="AuditLog"/>: does NOT inherit
/// <see cref="BaseEntity"/> and has no <c>UpdatedAt</c> column.  Rows must never be
/// updated or deleted after insertion.
/// </para>
/// </summary>
public sealed class CodingAuditLog
{
    /// <summary>Surrogate primary key — generated on insert.</summary>
    public Guid LogId { get; set; } = Guid.NewGuid();

    /// <summary>FK to the <see cref="MedicalCode"/> record that was acted upon.</summary>
    public Guid MedicalCodeId { get; set; }

    /// <summary>FK to the <see cref="Patient"/> the medical code belongs to (denormalised for query performance).</summary>
    public Guid PatientId { get; set; }

    /// <summary>The type of staff action that was performed (US_049 AC-2, AC-4).</summary>
    public CodingAuditAction Action { get; set; }

    /// <summary>
    /// Code value that existed before this action.  For an initial approval this is the
    /// AI-suggested code; for an override it is the code being replaced.
    /// Max 20 characters.
    /// </summary>
    public string OldCodeValue { get; set; } = string.Empty;

    /// <summary>
    /// Code value after this action.  Equals <see cref="OldCodeValue"/> for a plain
    /// approval; differs for an override.  Max 20 characters.
    /// </summary>
    public string NewCodeValue { get; set; } = string.Empty;

    /// <summary>
    /// Free-text justification supplied by the staff member, typically required when
    /// <see cref="Action"/> is <see cref="CodingAuditAction.Overridden"/> (US_049 AC-4).
    /// <c>null</c> for approval and revalidation events.  Max 1 000 characters.
    /// </summary>
    public string? Justification { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> who performed the action.
    /// Nullable and preserved (ON DELETE SET NULL) so the audit trail is retained even
    /// if the user account is later deleted (DR-016, HIPAA).
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// UTC point-in-time when the audit event occurred.  Set once on insert; never updated.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Wall-clock creation timestamp (UTC).  Redundant with <see cref="Timestamp"/> but
    /// aligns with the write pattern used by <see cref="AuditLog"/> for consistent querying.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public MedicalCode MedicalCode { get; set; } = null!;

    public Patient Patient { get; set; } = null!;

    public ApplicationUser? User { get; set; }
}
