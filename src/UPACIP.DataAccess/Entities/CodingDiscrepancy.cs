using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records an individual discrepancy between an AI-suggested code and the code ultimately
/// selected by staff (US_050, FR-068).
/// <para>
/// A discrepancy row is written whenever a staff member overrides an AI suggestion.
/// These rows feed the daily <see cref="AgreementRateMetric"/> calculation job and
/// provide the audit evidence required for model quality review.
/// </para>
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/> — it has a dedicated <c>DiscrepancyId</c> PK
/// and no <c>UpdatedAt</c> (rows are immutable once inserted, like <see cref="CodingAuditLog"/>).
/// </para>
/// </summary>
public sealed class CodingDiscrepancy
{
    /// <summary>Surrogate UUID primary key — generated on insert.</summary>
    public Guid DiscrepancyId { get; set; } = Guid.NewGuid();

    /// <summary>FK to the <see cref="MedicalCode"/> record that was overridden.</summary>
    public Guid MedicalCodeId { get; set; }

    /// <summary>
    /// FK to the <see cref="Patient"/> the medical code belongs to (denormalised for
    /// patient-level discrepancy queries without a join to <see cref="MedicalCode"/>).
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// The code value originally suggested by the AI pipeline (e.g. "E11.9").
    /// Max 20 characters — matches <c>MedicalCode.CodeValue</c> bound.
    /// </summary>
    public string AiSuggestedCode { get; set; } = string.Empty;

    /// <summary>
    /// The replacement code value selected by staff.
    /// Max 20 characters.
    /// </summary>
    public string StaffSelectedCode { get; set; } = string.Empty;

    /// <summary>Coding standard of the codes involved in this discrepancy.</summary>
    public CodeType CodeType { get; set; }

    /// <summary>
    /// Classification of the disagreement (full override, partial override, or multiple-code mapping).
    /// Partial overrides are treated as disagreements for agreement-rate purposes (EC-1).
    /// </summary>
    public DiscrepancyType DiscrepancyType { get; set; }

    /// <summary>
    /// Clinical justification entered by the staff member when performing the override.
    /// Copied from the originating <see cref="CodingAuditLog"/> row.
    /// Max 1 000 characters; nullable because multiple-code mapping discrepancies
    /// may be system-detected rather than staff-initiated.
    /// </summary>
    public string? OverrideJustification { get; set; }

    /// <summary>UTC timestamp when the discrepancy was detected (i.e. when the override was saved).</summary>
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC wall-clock creation timestamp (redundant with <see cref="DetectedAt"/> but
    /// consistent with other immutable tables for tooling / BI pipelines).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ─────────────────────────────────────────────────────────────────────────
    // Navigation properties
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The <see cref="MedicalCode"/> record that was overridden.</summary>
    public MedicalCode MedicalCode { get; set; } = null!;

    /// <summary>The patient whose code was overridden.</summary>
    public Patient Patient { get; set; } = null!;
}
