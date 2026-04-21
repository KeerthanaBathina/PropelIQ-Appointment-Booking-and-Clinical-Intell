namespace UPACIP.Service.Appointments;

// ─── Insurance pre-check DTOs (US_031, FR-033, AC-2, AC-4, EC-2) ─────────────

/// <summary>
/// Client-facing inline result of the soft insurance pre-check (AC-4).
///
/// The service maps internal validation outcomes to one of three patient-safe statuses:
///   <c>valid</c>        — policy matched a dummy record; no follow-up needed.
///   <c>needs-review</c> — policy not matched or data ambiguous; staff will review before visit.
///   <c>skipped</c>      — insurance fields absent; staff will collect during the visit (EC-2).
///
/// Staff-review flagging (AC-3, FR-034): when the outcome is <c>needs-review</c> or
/// <c>skipped</c> the service layer is responsible for emitting a review event so the
/// staff dashboard can surface the record.
/// </summary>
public sealed record InsurancePrecheckResultDto
{
    /// <summary>
    /// Patient-facing status string. One of: <c>valid</c> | <c>needs-review</c> | <c>skipped</c>.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable explanation shown to the patient (AC-4).
    /// Null for <c>valid</c> — no extra copy is needed when the check passes.
    /// Always non-null for <c>needs-review</c> and <c>skipped</c>.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// True when the result was flagged for staff review and a notification record was created
    /// (AC-3, FR-034).  The patient UI uses this to display an informational notice.
    /// </summary>
    public bool FlaggedForStaffReview { get; init; }
}

// ─── Guardian consent DTOs (US_031, FR-032, AC-1, EC-1) ──────────────────────

/// <summary>
/// Guardian details required when the patient is under 18 (AC-1).
/// Accepted as part of <see cref="SubmitManualIntakeRequest"/> and persisted to
/// the <c>IntakeData</c> snapshot so booking enforcement can verify it.
/// </summary>
public sealed record GuardianConsentFields
{
    /// <summary>Full legal name of the parent or guardian.</summary>
    public string? GuardianName         { get; init; }

    /// <summary>
    /// Guardian date of birth (ISO YYYY-MM-DD).
    /// Validated server-side to be >= 18 years at submission time (EC-1).
    /// </summary>
    public string? GuardianDateOfBirth  { get; init; }

    /// <summary>Guardian's relationship to the patient (e.g. "Parent", "Legal Guardian").</summary>
    public string? GuardianRelationship { get; init; }

    /// <summary>Must be <c>true</c> for the intake to be accepted (AC-1).</summary>
    public bool    GuardianConsentAcknowledged { get; init; }
}

/// <summary>
/// Validation failure for a single guardian consent field (EC-1 rejection contract).
/// </summary>
public sealed record GuardianConsentValidationError
{
    public string Field   { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

// ─── Standalone insurance precheck endpoint DTOs ──────────────────────────────

/// <summary>
/// Request body for <c>POST /api/intake/insurance/precheck</c> (US_031 AC-2, AC-4).
/// Called by the FE autosave hook on a 800ms debounce after insurance fields are filled.
/// </summary>
public sealed record InsurancePrecheckRequest
{
    public string InsuranceProvider { get; init; } = string.Empty;
    public string PolicyNumber      { get; init; } = string.Empty;
}
