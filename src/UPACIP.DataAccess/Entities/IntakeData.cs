using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Captures all intake information collected from a patient prior to an appointment.
/// The three JSONB columns (<see cref="MandatoryFields"/>, <see cref="OptionalFields"/>,
/// <see cref="InsuranceInfo"/>) are stored as strongly-typed owned types serialized to
/// JSONB by EF Core / Npgsql.
/// </summary>
public sealed class IntakeData : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> who submitted this intake record.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Channel through which the intake was collected.</summary>
    public IntakeMethod IntakeMethod { get; set; }

    /// <summary>Required clinical fields — stored as JSONB (mandatory_fields column).</summary>
    public IntakeMandatoryFields? MandatoryFields { get; set; }

    /// <summary>Optional clinical fields — stored as JSONB (optional_fields column).</summary>
    public IntakeOptionalFields? OptionalFields { get; set; }

    /// <summary>Patient insurance details — stored as JSONB (insurance_info column).</summary>
    public InsuranceInfo? InsuranceInfo { get; set; }

    /// <summary>UTC timestamp when the patient completed and submitted the intake form.</summary>
    public DateTime? CompletedAt { get; set; }

    // -------------------------------------------------------------------------
    // AI conversational intake session state (US_027, AC-3, AC-5, EC-2)
    // Scalar columns are kept separate from the JSONB snapshot so they can be
    // independently indexed for active-session lookups (EC-2 resume queries).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opaque session identifier linking this record to the Redis session cache entry.
    /// NULL for manually submitted intake records.
    /// Indexed to support session-ID lookup and deduplication (EC-2).
    /// </summary>
    public Guid? AiSessionId { get; set; }

    /// <summary>
    /// Lifecycle status of the conversational session:
    /// "active" | "summary" | "completed" | "manual".
    /// NULL for manually submitted intake records.
    /// Indexed alongside <see cref="PatientId"/> for fast active-session resume queries.
    /// </summary>
    public string? AiSessionStatus { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent autosave write from the conversational intake service.
    /// Used to select the most recent draft when multiple drafts exist (EC-2 tiebreaker).
    /// </summary>
    public DateTime? LastAutoSavedAt { get; set; }

    /// <summary>
    /// Full in-progress session state snapshot stored as JSONB (EC-2 restore).
    /// Contains collected field values, current question pointer, and turn count.
    /// NULL until the first autosave of a conversational intake session.
    /// </summary>
    public AiSessionSnapshot? AiSessionSnapshot { get; set; }

    // -------------------------------------------------------------------------
    // Mode-switch source attribution and provenance (US_029, AC-3, AC-4, EC-1, EC-2)
    // Stored as a single JSONB column so all attribution metadata is loaded atomically.
    // NULL for records that never undergo an AI ↔ manual mode switch.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Per-field source attribution, conflict history, and mode-switch event log.
    /// Populated whenever a patient switches between AI conversational intake and the
    /// manual form so the final intake record can be reconstructed with full provenance.
    /// NULL for intake sessions that never switch modes (backward-compatible).
    /// </summary>
    public IntakeAttributionSnapshot? AttributionSnapshot { get; set; }

    // -------------------------------------------------------------------------
    // Minor guardian consent (US_031, AC-1, EC-1)
    // Stored as a single JSONB column — NULL for adult patients and legacy rows.
    // Contains guardian identity, relationship, consent acknowledgment, and
    // the UTC timestamp when consent was recorded.  Service layer parses
    // GuardianConsentSnapshot.GuardianDateOfBirth to verify guardian age >= 18 (EC-1).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Guardian consent details for minor patients (DOB < 18 years from today).
    /// NULL for adult intake records or records predating this feature.
    /// Required to be non-NULL with <c>ConsentAcknowledged = true</c> before a
    /// minor patient's booking can be confirmed (US_031 AC-1).
    /// </summary>
    public GuardianConsentSnapshot? GuardianConsent { get; set; }

    // -------------------------------------------------------------------------
    // Insurance soft pre-check outcome (US_031, AC-2, AC-3, EC-2)
    // Scalar columns so staff-review queries use a simple indexed boolean filter
    // rather than JSONB path operators (efficient lookup for US_034 staff dashboard).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Outcome of the insurance soft pre-check: "valid" | "needs-review" | "skipped".
    /// NULL means no pre-check has been run yet (e.g., draft not yet submitted).
    /// </summary>
    public string? InsuranceValidationStatus { get; set; }

    /// <summary>
    /// Human-readable reason provided to the patient and staff when status is "needs-review"
    /// or "skipped" (AC-4, EC-2).  NULL when status is "valid" or pre-check not yet run.
    /// </summary>
    public string? InsuranceReviewReason { get; set; }

    /// <summary>
    /// Explicit staff-follow-up flag: <c>true</c> when insurance validation returned
    /// "needs-review" or "skipped" and staff must manually collect or verify coverage
    /// before or during the visit (AC-3, EC-2).
    /// Indexed via a partial index to support efficient staff-dashboard queries.
    /// </summary>
    public bool InsuranceRequiresStaffFollowup { get; set; }

    /// <summary>UTC timestamp when the insurance pre-check was last executed.  NULL if not yet run.</summary>
    public DateTime? InsuranceValidatedAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;
}
