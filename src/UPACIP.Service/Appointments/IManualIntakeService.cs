namespace UPACIP.Service.Appointments;

/// <summary>
/// Business logic for the manual intake form lifecycle (US_028).
///
/// Responsibilities:
///   - Load a patient's current manual draft, rehydrating personal-info fields from the
///     Patient entity and intake fields from the <c>IntakeData</c> snapshot (EC-1).
///   - Persist partial field updates into the draft snapshot without creating duplicate rows.
///   - Validate mandatory fields and return field-specific errors consumable by the UI (AC-3).
///   - Finalize submission idempotently, preventing duplicate completion events on retry (EC-2).
///   - Emit a structured completion event for downstream staff-review notification hooks (AC-4).
///
/// Ownership: all operations accept a <paramref name="patientId"/> resolved server-side from
/// the JWT — never trusted from the request body (OWASP A01).
/// </summary>
public interface IManualIntakeService
{
    /// <summary>
    /// Loads the current manual intake draft for <paramref name="patientId"/>.
    /// Returns <c>null</c> when no in-progress draft exists (HTTP 404 → FE starts fresh form).
    /// Personal-information fields are sourced from the Patient entity.
    /// AI-prefilled field keys are identified and returned in <c>PrefilledKeys</c> (AC-2).
    /// </summary>
    Task<ManualIntakeDraftResponse?> LoadDraftAsync(
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a partial autosave update into the shared <c>IntakeData</c> draft row.
    /// Creates the draft row on the first call; updates it on subsequent calls (EC-1).
    /// Null field values in <paramref name="request"/> are silently ignored — they do not
    /// overwrite existing values in the snapshot.
    /// </summary>
    Task<SaveManualIntakeDraftResponse> SaveDraftAsync(
        Guid patientId,
        SaveManualIntakeDraftRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Validates all mandatory fields and, if valid, marks the intake as completed.
    ///
    /// Returns:
    ///   - <c>(Response, null)</c>  — success: intake completed or idempotently re-confirmed.
    ///   - <c>(null, Errors)</c>    — validation failure: field-specific error list (AC-3).
    ///
    /// Idempotency (EC-2): if the matching draft row already has <c>CompletedAt</c> set the
    /// method returns the existing completion data without re-writing anything.
    /// </summary>
    Task<(SubmitManualIntakeResponse? Response, IReadOnlyList<ManualIntakeValidationError>? Errors)> SubmitAsync(
        Guid patientId,
        SubmitManualIntakeRequest request,
        CancellationToken ct = default);
    /// <summary>
    /// Runs the soft insurance pre-check for <paramref name="patientId"/> against dummy records
    /// and returns an inline result suitable for the patient-facing UI (US_031 AC-2, AC-4).
    ///
    /// Used by the standalone <c>POST /api/intake/insurance/precheck</c> endpoint so the UI
    /// can display a fresh result on 800ms debounce without triggering a full draft save.
    /// </summary>
    Task<InsurancePrecheckResultDto> RunInsurancePrecheckAsync(
        Guid              patientId,
        string?           insuranceProvider,
        string?           policyNumber,
        CancellationToken ct = default);
}
