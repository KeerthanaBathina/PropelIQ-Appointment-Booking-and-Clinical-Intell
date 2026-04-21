using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Deterministic insurance pre-check and guardian consent validation (US_031, FR-032–FR-034).
///
/// <b>Insurance validation</b> (AC-2, AC-4):
///   This is a <em>soft</em> pre-check only.  No real insurance clearinghouse is called.
///   Dummy known-valid policy prefixes are matched locally; any unrecognised provider or
///   prefix returns <c>needs-review</c>.  This design allows the feature to ship and the
///   staff-review workflow to be exercised before a real clearinghouse integration is added.
///
/// <b>Staff-review flagging</b> (AC-3, FR-034):
///   When the outcome is <c>needs-review</c> or <c>skipped</c> a structured log event is
///   emitted with all identifiers needed to build a staff notification.
///   When US_034 notification infrastructure is available the structured log can be replaced
///   by an INotificationService call without changing this service's contract.
///
/// <b>Guardian consent</b> (FR-032, AC-1, EC-1):
///   Deterministic field-level validation: all required fields must be non-empty, the
///   guardian's age must be >= 18 (EC-1 blocks minor-on-minor consent), and the consent
///   acknowledgment checkbox must be <c>true</c>.
///
/// PII policy: patient name, DOB, and policy numbers are never written to logs (AIR-S01).
/// </summary>
public sealed class InsurancePrecheckService : IInsurancePrecheckService
{
    // ── Dummy-record registry (FR-033) ────────────────────────────────────────
    // In a real integration this would be replaced by a clearinghouse API call.
    // The dummy set covers major US carriers so the pre-check has realistic pass-rate
    // in development and QA environments.
    private static readonly HashSet<string> KnownValidProviderPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "blue cross",
        "bcbs",
        "aetna",
        "cigna",
        "humana",
        "united health",
        "unitedhealthcare",
        "uhc",
        "anthem",
        "kaiser",
        "molina",
        "wellcare",
        "centene",
        "tricare",
        "medicare",
        "medicaid",
    };

    // Recognised policy-number prefixes for dummy validation.
    // In a real integration the clearinghouse would validate the full number.
    private static readonly HashSet<string> KnownValidPolicyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BCB-", "AET-", "CIG-", "HUM-", "UHC-", "ANT-",
        "KAI-", "MOL-", "WEL-", "TRI-", "MCR-", "MCD-",
    };

    private readonly ILogger<InsurancePrecheckService> _logger;

    public InsurancePrecheckService(ILogger<InsurancePrecheckService> logger)
    {
        _logger = logger;
    }

    // ─── Insurance pre-check ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<InsurancePrecheckResultDto> RunPrecheckAsync(
        string?           insuranceProvider,
        string?           policyNumber,
        Guid              patientId,
        Guid              intakeDataId,
        CancellationToken ct = default)
    {
        var providerTrimmed = insuranceProvider?.Trim();
        var policyTrimmed   = policyNumber?.Trim();

        // EC-2: insurance information absent → skip pre-check
        if (string.IsNullOrEmpty(providerTrimmed) && string.IsNullOrEmpty(policyTrimmed))
        {
            _logger.LogInformation(
                "InsurancePrecheck: skipped (no insurance data) for intakeData={IntakeDataId}; flagged for staff collection.",
                intakeDataId);

            EmitStaffReviewEvent(patientId, intakeDataId, "skipped", reason: "Insurance information absent.");

            return Task.FromResult(new InsurancePrecheckResultDto
            {
                Status               = "skipped",
                Message              = "No insurance details provided. Staff will collect your insurance information during your visit.",
                FlaggedForStaffReview = true,
            });
        }

        // Partial data — one field missing: treat as needs-review (non-blocking)
        if (string.IsNullOrEmpty(providerTrimmed) || string.IsNullOrEmpty(policyTrimmed))
        {
            _logger.LogInformation(
                "InsurancePrecheck: needs-review (partial insurance data) for intakeData={IntakeDataId}.",
                intakeDataId);

            EmitStaffReviewEvent(patientId, intakeDataId, "needs-review", reason: "Partial insurance data submitted.");

            return Task.FromResult(new InsurancePrecheckResultDto
            {
                Status               = "needs-review",
                Message              = "Your insurance details are incomplete. A staff member will review them before your visit.",
                FlaggedForStaffReview = true,
            });
        }

        // Dummy validation: check provider name and policy prefix against known-valid sets
        var providerValid = KnownValidProviderPrefixes.Any(p => providerTrimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        var policyValid   = KnownValidPolicyPrefixes.Any(prefix => policyTrimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (providerValid && policyValid)
        {
            _logger.LogDebug(
                "InsurancePrecheck: valid result for intakeData={IntakeDataId}.",
                intakeDataId);

            return Task.FromResult(new InsurancePrecheckResultDto
            {
                Status               = "valid",
                Message              = null,
                FlaggedForStaffReview = false,
            });
        }

        // Not matched — needs staff review
        _logger.LogInformation(
            "InsurancePrecheck: needs-review (unrecognised provider/policy) for intakeData={IntakeDataId}; flagged for staff review.",
            intakeDataId);

        EmitStaffReviewEvent(patientId, intakeDataId, "needs-review", reason: "Insurance provider or policy number not recognised.");

        return Task.FromResult(new InsurancePrecheckResultDto
        {
            Status               = "needs-review",
            Message              = "Your insurance details could not be automatically verified. A staff member will review them before your visit.",
            FlaggedForStaffReview = true,
        });
    }

    // ─── Guardian consent validation ─────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<GuardianConsentValidationError> ValidateGuardianConsent(
        GuardianConsentFields fields)
    {
        var errors = new List<GuardianConsentValidationError>();

        if (string.IsNullOrWhiteSpace(fields.GuardianName))
            errors.Add(Err("guardianName", "Guardian full name is required."));

        if (string.IsNullOrWhiteSpace(fields.GuardianRelationship))
            errors.Add(Err("guardianRelationship", "Guardian relationship to patient is required."));

        if (string.IsNullOrWhiteSpace(fields.GuardianDateOfBirth))
        {
            errors.Add(Err("guardianDateOfBirth", "Guardian date of birth is required."));
        }
        else
        {
            // EC-1: guardian must be 18 or older at the time of submission
            if (!DateOnly.TryParse(fields.GuardianDateOfBirth, out var guardianDob))
            {
                errors.Add(Err("guardianDateOfBirth", "Enter a valid guardian date of birth."));
            }
            else
            {
                var today        = DateOnly.FromDateTime(DateTime.UtcNow);
                var guardianAge  = today.Year - guardianDob.Year;
                if (today < guardianDob.AddYears(guardianAge)) guardianAge--;

                if (guardianAge < 18)
                {
                    errors.Add(Err(
                        "guardianDateOfBirth",
                        "Guardian must be 18 years or older. A minor cannot provide consent for another minor (EC-1)."));
                }
            }
        }

        if (!fields.GuardianConsentAcknowledged)
        {
            errors.Add(Err(
                "guardianConsentAcknowledged",
                "Guardian must acknowledge consent before the minor's intake can be submitted."));
        }

        return errors.AsReadOnly();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Emits a structured log event for a staff-review requirement (AC-3, FR-034).
    /// When US_034 notification infrastructure is available, replace this method body
    /// with an <c>INotificationService.CreateReviewRecordAsync</c> call.
    /// </summary>
    private void EmitStaffReviewEvent(Guid patientId, Guid intakeDataId, string outcome, string reason)
    {
        // Structured event: staff dashboard pipeline subscribes to EventType="InsuranceReviewRequired".
        // PII is deliberately omitted from this log line per AIR-S01.
        _logger.LogWarning(
            "StaffReview required: EventType=InsuranceReviewRequired, " +
            "PatientId={PatientId}, IntakeDataId={IntakeDataId}, " +
            "Outcome={Outcome}, Reason={Reason}.",
            patientId, intakeDataId, outcome, reason);
    }

    private static GuardianConsentValidationError Err(string field, string message)
        => new() { Field = field, Message = message };
}
