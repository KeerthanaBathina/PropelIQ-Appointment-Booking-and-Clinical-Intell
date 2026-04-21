namespace UPACIP.Service.Appointments;

// ─── Shared field bag ─────────────────────────────────────────────────────────

/// <summary>
/// Flat field bag covering all sections of the manual intake form (US_028, SCR-009).
///
/// Personal-information fields (FirstName … EmergencyContact) are sourced from the
/// Patient entity on draft load and accepted on submit for display-confirmation;
/// they are stored in the draft snapshot for EC-1 restore but never applied to the
/// Patient row by the intake service — demographic updates are a separate operation.
///
/// Medical and insurance fields are persisted to the typed JSONB columns in
/// <c>IntakeData</c> on autosave and final submission.
/// </summary>
public sealed record ManualIntakeFields
{
    // ── Personal Information (from Patient entity) ────────────────────────────
    public string? FirstName          { get; init; }
    public string? LastName           { get; init; }
    /// <summary>ISO date string YYYY-MM-DD.</summary>
    public string? DateOfBirth        { get; init; }
    public string? Gender             { get; init; }
    public string? Phone              { get; init; }
    public string? EmergencyContact   { get; init; }

    // ── Medical History (persisted to IntakeMandatoryFields JSONB) ───────────
    public string? KnownAllergies         { get; init; }
    public string? CurrentMedications     { get; init; }
    public string? PreExistingConditions  { get; init; }

    // ── Insurance (persisted to InsuranceInfo JSONB) ─────────────────────────
    public string? InsuranceProvider  { get; init; }
    public string? PolicyNumber       { get; init; }
    // ── Guardian Consent (US_031 AC-1; only accepted when patient age < 18) ────────
    public string? GuardianName                { get; init; }
    /// <summary>ISO date string YYYY-MM-DD. Validated server-side to be >= 18 years.</summary>
    public string? GuardianDateOfBirth         { get; init; }
    public string? GuardianRelationship        { get; init; }
    public bool?   GuardianConsentAcknowledged { get; init; }
    // ── Consent (required on submit; not stored as a DB column) ─────────────
    public bool? ConsentGiven         { get; init; }
}

// ─── Draft load (GET /api/intake/manual/draft) ────────────────────────────────

/// <summary>
/// Response returned by <c>GET /api/intake/manual/draft</c>.
/// Delivers the current draft state plus metadata identifying which field values
/// were originally carried over from an AI intake session (AC-2, EC-1).
/// </summary>
public sealed record ManualIntakeDraftResponse
{
    /// <summary>Opaque draft record identifier (Guid string). Null when no draft exists.</summary>
    public string? Id           { get; init; }
    public ManualIntakeFields Fields       { get; init; } = new();
    /// <summary>ISO 8601 UTC timestamp of the last autosave. Null when no save has occurred.</summary>
    public string? LastSavedAt  { get; init; }
    /// <summary>
    /// Camel-case field names whose values were pre-populated from an AI intake session (AC-2).
    /// The UI uses this list to render <c>PrefilledFieldIndicator</c> badges.
    /// </summary>
    public IReadOnlyList<string> PrefilledKeys { get; init; } = [];

    /// <summary>
    /// Soft insurance pre-check result from the last save or load (US_031 AC-2, AC-4).
    /// Null when no pre-check has been run yet (e.g. first-time load with no insurance data).
    /// </summary>
    public InsurancePrecheckResultDto? InsurancePrecheck { get; init; }

    /// <summary>
    /// True when the stored draft contains valid guardian consent for a minor patient (US_031 AC-1).
    /// The UI uses this to decide whether to show the booking-readiness notice.
    /// </summary>
    public bool GuardianConsentComplete { get; init; }
}

// ─── Draft save (POST /api/intake/manual/draft) ───────────────────────────────

/// <summary>
/// Request body for <c>POST /api/intake/manual/draft</c> — autosave on 30-second cadence (UXR-004).
/// Partial updates are accepted; null fields are not overwritten in the snapshot.
/// </summary>
public sealed record SaveManualIntakeDraftRequest
{
    public ManualIntakeFields Fields { get; init; } = new();
}

/// <summary>Response for <c>POST /api/intake/manual/draft</c>.</summary>
public sealed record SaveManualIntakeDraftResponse
{
    /// <summary>ISO 8601 UTC timestamp recorded for this save, shown in the UI autosave label.</summary>
    public string LastSavedAt { get; init; } = string.Empty;
}

// ─── Submit (POST /api/intake/manual/submit) ──────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/intake/manual/submit</c> — final intake submission (AC-3, AC-4).
/// All mandatory fields must be populated; field-level errors are returned on HTTP 422 when any
/// are missing or invalid (AC-3).
/// </summary>
public sealed record SubmitManualIntakeRequest
{
    public ManualIntakeFields Fields { get; init; } = new();
}

/// <summary>Response returned on a successful submission (AC-4).</summary>
public sealed record SubmitManualIntakeResponse
{
    public Guid   IntakeDataId { get; init; }
    /// <summary>ISO 8601 UTC timestamp of the completion event.</summary>
    public string CompletedAt  { get; init; } = string.Empty;

    /// <summary>
    /// Insurance pre-check result at submission time (US_031 AC-2, AC-4).
    /// Null when insurance details were absent (EC-2 skipped case).
    /// </summary>
    public InsurancePrecheckResultDto? InsurancePrecheck { get; init; }
}

/// <summary>
/// A single field-level validation failure, keyed by the camelCase form field name (AC-3).
/// Matches the <c>ValidationErrors</c> dictionary shape in <c>ErrorResponse</c>.
/// </summary>
public sealed record ManualIntakeValidationError
{
    public string Field   { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
