namespace UPACIP.Service.Appointments;

/// <summary>
/// Validates insurance details against dummy records and maps results to the
/// patient-safe <see cref="InsurancePrecheckResultDto"/> contract (US_031, FR-033, AC-2, AC-4).
///
/// <b>Skipped check (EC-2)</b>: When insurance fields are absent the service returns a
/// <c>skipped</c> result and flags the record for staff collection during the visit.
///
/// <b>Staff-review flagging (AC-3, FR-034)</b>: When the outcome is <c>needs-review</c>
/// or <c>skipped</c>, a structured log event is emitted (and optionally a notification
/// record created) so the staff dashboard can surface the record for follow-up.
///
/// <b>Guardian consent validation (FR-032, AC-1, EC-1)</b>: Static helpers on this
/// interface provide deterministic guardian-consent validation so both the intake and
/// booking services can enforce the same rules from a single source of truth.
/// </summary>
public interface IInsurancePrecheckService
{
    /// <summary>
    /// Runs the soft insurance pre-check for a patient intake record (AC-2, AC-4).
    ///
    /// When <paramref name="insuranceProvider"/> or <paramref name="policyNumber"/> is
    /// absent or whitespace, returns a <c>skipped</c> result immediately without any
    /// external call (EC-2).
    ///
    /// The <paramref name="patientId"/> and <paramref name="intakeDataId"/> are used for
    /// structured logging and staff-review notification payloads only — they are never
    /// forwarded to any external insurance service.
    /// </summary>
    /// <param name="insuranceProvider">Name of the insurance provider. May be null/empty (EC-2).</param>
    /// <param name="policyNumber">Policy number. May be null/empty (EC-2).</param>
    /// <param name="patientId">Patient identifier for audit logging.</param>
    /// <param name="intakeDataId">Intake record identifier for the review notification payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<InsurancePrecheckResultDto> RunPrecheckAsync(
        string?           insuranceProvider,
        string?           policyNumber,
        Guid              patientId,
        Guid              intakeDataId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates guardian consent fields for a minor patient (AC-1, EC-1).
    ///
    /// Validates:
    ///   1. All required fields are non-empty.
    ///   2. Guardian date of birth indicates the guardian is 18 or older (EC-1).
    ///   3. Guardian consent acknowledgment is <c>true</c>.
    ///
    /// Returns an empty list when all fields are valid.
    /// Returns field-level errors suitable for the UI error map when validation fails.
    /// </summary>
    /// <param name="fields">Guardian consent fields from the intake request.</param>
    IReadOnlyList<GuardianConsentValidationError> ValidateGuardianConsent(
        GuardianConsentFields fields);
}
