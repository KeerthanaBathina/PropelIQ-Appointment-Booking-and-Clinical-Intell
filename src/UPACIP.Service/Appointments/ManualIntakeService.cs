using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Deterministic business logic for the manual intake form lifecycle (US_028, FR-027, FR-029, FR-030, FR-031).
///
/// <b>Draft persistence strategy</b>:
/// All form field values (including personal-info for EC-1 restore) are stored in
/// <c>IntakeData.AiSessionSnapshot.CollectedFields</c> as a flat key-value list during
/// the draft phase — no additional schema columns are needed.  The sentinel key
/// <c>__prefilled_keys</c> stores the comma-separated list of fields that were originally
/// carried over from an AI intake session so that the UI can render prefill badges (AC-2).
///
/// Status transitions:
///   "manual"        — draft row created by SwitchToManualAsync; snapshot holds AI-collected values.
///   "manual_draft"  — patient has saved at least once through the manual form.
///   null            — intake is completed (CompletedAt is set).
///
/// <b>Idempotency</b> (EC-2):
/// <see cref="SubmitAsync"/> checks <c>CompletedAt</c> before writing; a second call with
/// the same patientId returns the existing completion record without any side-effects.
///
/// <b>Staff-review notification</b> (AC-4):
/// On completion a structured log event is emitted.  When US_034 notification infrastructure
/// is available this log event can be replaced by an INotificationService call without
/// changing this service's contract.
///
/// PII policy: patient name, DOB, and free-text clinical values are never written to logs.
/// </summary>
public sealed class ManualIntakeService : IManualIntakeService
{
    // Snapshot status constants
    private const string StatusManual      = "manual";
    private const string StatusManualDraft = "manual_draft";

    // Sentinel key that records which field names were originally AI-prefilled (AC-2).
    private const string PrefilledKeysSnapshotKey = "__prefilled_keys";

    private readonly ApplicationDbContext          _db;
    private readonly IInsurancePrecheckService     _insurancePrecheck;
    private readonly ILogger<ManualIntakeService>  _logger;

    public ManualIntakeService(
        ApplicationDbContext         db,
        IInsurancePrecheckService    insurancePrecheck,
        ILogger<ManualIntakeService> logger)
    {
        _db                = db;
        _insurancePrecheck = insurancePrecheck;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Load draft — GET /api/intake/manual/draft
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ManualIntakeDraftResponse?> LoadDraftAsync(
        Guid patientId,
        CancellationToken ct = default)
    {
        // 1. Load patient demographics
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId && p.DeletedAt == null)
            .Select(p => new
            {
                p.FullName,
                p.DateOfBirth,
                p.PhoneNumber,
                p.EmergencyContact,
            })
            .FirstOrDefaultAsync(ct);

        if (patient is null)
        {
            _logger.LogWarning("ManualIntakeSvc: patient {PatientId} not found for draft load.", patientId);
            return null;
        }

        // 2. Find the most recent incomplete manual draft
        var draft = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual || i.AiSessionStatus == StatusManualDraft)
                     && i.CompletedAt == null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        // 3. Split patient FullName → firstName / lastName for display
        var nameParts = (patient.FullName ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
        var lastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        // 4. If no draft, return patient demographics with empty medical/insurance fields (fresh start)
        if (draft is null)
        {
            return new ManualIntakeDraftResponse
            {
                Fields = new ManualIntakeFields
                {
                    FirstName        = firstName,
                    LastName         = lastName,
                    DateOfBirth      = patient.DateOfBirth.ToString("yyyy-MM-dd"),
                    Phone            = patient.PhoneNumber,
                    EmergencyContact = patient.EmergencyContact,
                },
                PrefilledKeys = [],
            };
        }

        // 5. Read snapshot fields
        var snapshot = draft.AiSessionSnapshot;
        var collected = snapshot?.CollectedFields ?? [];
        var fieldMap = collected
            .Where(f => f.Key != PrefilledKeysSnapshotKey)
            .ToDictionary(f => f.Key, f => f.Value);

        // 6. Determine which fields were AI-prefilled
        var prefilledRaw = collected
            .FirstOrDefault(f => f.Key == PrefilledKeysSnapshotKey)?.Value ?? string.Empty;
        var prefilledKeys = prefilledRaw.Length > 0
            ? prefilledRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        // 7. Build fields: snapshot values take precedence over patient entity for
        //    fields that were saved mid-draft; patient entity is used for any missing personal info.
        var fields = new ManualIntakeFields
        {
            FirstName             = Get(fieldMap, "firstName", firstName),
            LastName              = Get(fieldMap, "lastName", lastName),
            DateOfBirth           = Get(fieldMap, "dateOfBirth", patient.DateOfBirth.ToString("yyyy-MM-dd")),
            Gender                = Get(fieldMap, "gender", null),
            Phone                 = Get(fieldMap, "phone", patient.PhoneNumber),
            EmergencyContact      = Get(fieldMap, "emergencyContact", patient.EmergencyContact),
            KnownAllergies        = Get(fieldMap, "knownAllergies", null),
            CurrentMedications    = Get(fieldMap, "currentMedications", null),
            PreExistingConditions = Get(fieldMap, "preExistingConditions", null),
            InsuranceProvider     = Get(fieldMap, "insuranceProvider", null),
            PolicyNumber          = Get(fieldMap, "policyNumber", null),
        };

        return new ManualIntakeDraftResponse
        {
            Id            = draft.Id.ToString(),
            Fields        = fields,
            LastSavedAt   = draft.LastAutoSavedAt?.ToString("O"),
            PrefilledKeys = prefilledKeys,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Autosave draft — POST /api/intake/manual/draft
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<SaveManualIntakeDraftResponse> SaveDraftAsync(
        Guid patientId,
        SaveManualIntakeDraftRequest request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1. Find existing in-progress manual draft
        var draft = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual || i.AiSessionStatus == StatusManualDraft)
                     && i.CompletedAt == null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        if (draft is null)
        {
            // No draft row yet — create one (fresh manual entry, not from AI switch)
            draft = new IntakeData
            {
                Id             = Guid.NewGuid(),
                PatientId      = patientId,
                IntakeMethod   = IntakeMethod.ManualForm,
                AiSessionStatus = StatusManualDraft,
                LastAutoSavedAt = now,
                AiSessionSnapshot = new AiSessionSnapshot
                {
                    Status = StatusManualDraft,
                    CollectedFields = [],
                },
            };
            _db.IntakeRecords.Add(draft);
        }
        else
        {
            // When transitioning from AI-prefilled "manual" to first manual save,
            // capture the original prefilled keys so later loads can identify them.
            if (draft.AiSessionStatus == StatusManual)
            {
                var snapshot = draft.AiSessionSnapshot;
                if (snapshot is not null)
                {
                    var alreadyHasSentinel = snapshot.CollectedFields
                        .Any(f => f.Key == PrefilledKeysSnapshotKey);

                    if (!alreadyHasSentinel)
                    {
                        // Record the keys that are currently populated as AI-prefilled keys
                        var aiKeys = snapshot.CollectedFields
                            .Where(f => !string.IsNullOrEmpty(f.Value))
                            .Select(f => f.Key)
                            .ToList();

                        if (aiKeys.Count > 0)
                        {
                            snapshot.CollectedFields.Add(new AiCollectedField
                            {
                                Key   = PrefilledKeysSnapshotKey,
                                Value = string.Join(",", aiKeys),
                            });
                        }
                    }
                }

                draft.AiSessionStatus = StatusManualDraft;
            }

            draft.LastAutoSavedAt = now;
        }

        // 2. Ensure snapshot exists
        draft.AiSessionSnapshot ??= new AiSessionSnapshot
        {
            Status = StatusManualDraft,
            CollectedFields = [],
        };

        // 3. Merge request fields into snapshot (null values are not overwritten)
        var fields = request.Fields;
        MergeField(draft.AiSessionSnapshot.CollectedFields, "firstName",             fields.FirstName);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "lastName",              fields.LastName);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "dateOfBirth",           fields.DateOfBirth);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "gender",                fields.Gender);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "phone",                 fields.Phone);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "emergencyContact",      fields.EmergencyContact);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "knownAllergies",        fields.KnownAllergies);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "currentMedications",    fields.CurrentMedications);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "preExistingConditions", fields.PreExistingConditions);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "insuranceProvider",     fields.InsuranceProvider);
        MergeField(draft.AiSessionSnapshot.CollectedFields, "policyNumber",          fields.PolicyNumber);
        // ConsentGiven is not persisted to the snapshot — it is only relevant on final submit.

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "ManualIntakeSvc: autosave for patient {PatientId}; draftId={DraftId}.",
            patientId, draft.Id);

        return new SaveManualIntakeDraftResponse
        {
            LastSavedAt = now.ToString("O"),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Submit — POST /api/intake/manual/submit
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(SubmitManualIntakeResponse? Response, IReadOnlyList<ManualIntakeValidationError>? Errors)> SubmitAsync(
        Guid patientId,
        SubmitManualIntakeRequest request,
        CancellationToken ct = default)
    {
        // 1. Validate mandatory fields (AC-3)
        var errors = ValidateMandatory(request.Fields);
        if (errors.Count > 0)
            return (null, errors);

        var fields = request.Fields;
        var now    = DateTime.UtcNow;

        // 1b. Resolve patient DOB to determine if minor (US_031 AC-1)
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId && p.DeletedAt == null)
            .Select(p => new { p.DateOfBirth })
            .FirstOrDefaultAsync(ct);

        if (patient is not null)
        {
            var today      = DateOnly.FromDateTime(DateTime.UtcNow);
            var age        = today.Year - patient.DateOfBirth.Year;
            if (today < patient.DateOfBirth.AddYears(age)) age--;
            var isMinor    = age < 18;

            if (isMinor)
            {
                // Validate guardian consent (AC-1, EC-1)
                var guardianFields = new GuardianConsentFields
                {
                    GuardianName                = fields.GuardianName,
                    GuardianDateOfBirth         = fields.GuardianDateOfBirth,
                    GuardianRelationship        = fields.GuardianRelationship,
                    GuardianConsentAcknowledged = fields.GuardianConsentAcknowledged ?? false,
                };

                var guardianErrors = _insurancePrecheck.ValidateGuardianConsent(guardianFields);
                if (guardianErrors.Count > 0)
                {
                    var fieldErrors = guardianErrors
                        .Select(e => new ManualIntakeValidationError { Field = e.Field, Message = e.Message })
                        .ToList();
                    return (null, fieldErrors);
                }
            }
        }

        // 2. Find existing draft or create a new row (fresh direct-to-submit path)
        var draft = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual
                         || i.AiSessionStatus == StatusManualDraft
                         || i.CompletedAt != null)   // include already-completed for idempotency check
                     && i.IntakeMethod == IntakeMethod.ManualForm)
            .OrderByDescending(i => i.LastAutoSavedAt ?? i.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // 3. Idempotency guard (EC-2) — return existing completion without re-writing
        if (draft?.CompletedAt is not null)
        {
            _logger.LogInformation(
                "ManualIntakeSvc: idempotent re-submit for patient {PatientId}; existing intakeDataId={Id}.",
                patientId, draft.Id);

            return (new SubmitManualIntakeResponse
            {
                IntakeDataId = draft.Id,
                CompletedAt  = draft.CompletedAt.Value.ToString("O"),
            }, null);
        }

        if (draft is null)
        {
            draft = new IntakeData
            {
                Id           = Guid.NewGuid(),
                PatientId    = patientId,
                IntakeMethod = IntakeMethod.ManualForm,
            };
            _db.IntakeRecords.Add(draft);
        }

        // 4. Map form fields to typed JSONB columns
        draft.MandatoryFields = new IntakeMandatoryFields
        {
            // ChiefComplaint is required by the entity but not collected by the manual form;
            // store an empty string so the JSONB column is well-formed.
            ChiefComplaint      = string.Empty,
            Allergies           = Trim(fields.KnownAllergies),
            CurrentMedications  = SplitMedications(fields.CurrentMedications),
            MedicalHistory      = Trim(fields.PreExistingConditions),
        };

        if (!string.IsNullOrWhiteSpace(fields.InsuranceProvider)
            || !string.IsNullOrWhiteSpace(fields.PolicyNumber))
        {
            draft.InsuranceInfo = new InsuranceInfo
            {
                Provider     = fields.InsuranceProvider?.Trim(),
                PolicyNumber = fields.PolicyNumber?.Trim(),
            };
        }

        // 4b. Run insurance pre-check and flag for staff review when needed (AC-2, AC-3, FR-033)
        var insuranceResult = await _insurancePrecheck.RunPrecheckAsync(
            fields.InsuranceProvider,
            fields.PolicyNumber,
            patientId,
            draft.Id,
            ct);

        // 4c. Persist insurance pre-check outcome to scalar columns (US_031 task_003, AC-2, AC-3, EC-2).
        //     Scalar columns enable an efficient staff-dashboard partial-index query without
        //     JSONB path operators.
        draft.InsuranceValidationStatus        = insuranceResult.Status;
        draft.InsuranceReviewReason            = insuranceResult.Message;
        draft.InsuranceRequiresStaffFollowup   = insuranceResult.FlaggedForStaffReview;
        draft.InsuranceValidatedAt             = now;

        // 4d. Persist guardian consent details for minor patients (US_031 task_003, AC-1, EC-1).
        //     Stored as JSONB — null for adult patients (patient is null implies we could not
        //     resolve a DOB, so we skip guardian persistence as a safe default).
        if (patient is not null)
        {
            var today2     = DateOnly.FromDateTime(DateTime.UtcNow);
            var age2       = today2.Year - patient.DateOfBirth.Year;
            if (today2 < patient.DateOfBirth.AddYears(age2)) age2--;

            if (age2 < 18
                && !string.IsNullOrWhiteSpace(fields.GuardianName)
                && fields.GuardianConsentAcknowledged == true)
            {
                draft.GuardianConsent = new GuardianConsentSnapshot
                {
                    GuardianName          = fields.GuardianName?.Trim(),
                    GuardianDateOfBirth   = fields.GuardianDateOfBirth?.Trim(),
                    GuardianRelationship  = fields.GuardianRelationship?.Trim(),
                    ConsentAcknowledged   = true,
                    ConsentRecordedAt     = now,
                };
            }
        }

        // 5. Mark as completed — clears draft status
        draft.CompletedAt       = now;
        draft.AiSessionStatus   = null;   // no longer an in-progress draft
        draft.LastAutoSavedAt   = now;

        await _db.SaveChangesAsync(ct);

        // 6. Structured log — staff-review notification hook (AC-4).
        //    When US_034 INotificationService is available, replace this log with a call to
        //    INotificationService.NotifyStaffIntakeCompletedAsync(patientId, draft.Id, ct).
        _logger.LogInformation(
            "ManualIntakeSvc: intake completed; patientId={PatientId}, intakeDataId={IntakeDataId}, completedAt={CompletedAt:O}. [STAFF_REVIEW_PENDING]",
            patientId, draft.Id, now);

        return (new SubmitManualIntakeResponse
        {
            IntakeDataId      = draft.Id,
            CompletedAt       = now.ToString("O"),
            InsurancePrecheck = insuranceResult,
        }, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Insurance pre-check (standalone) — POST /api/intake/insurance/precheck
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<InsurancePrecheckResultDto> RunInsurancePrecheckAsync(
        Guid              patientId,
        string?           insuranceProvider,
        string?           policyNumber,
        CancellationToken ct = default)
    {
        // Find the most recent in-progress draft to associate the check (for staff review logging)
        var draftId = await _db.IntakeRecords
            .AsNoTracking()
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual || i.AiSessionStatus == StatusManualDraft)
                     && i.CompletedAt == null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct)
            ?? Guid.Empty;

        return await _insurancePrecheck.RunPrecheckAsync(
            insuranceProvider,
            policyNumber,
            patientId,
            draftId,
            ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns the snapshot value for <paramref name="key"/> or <paramref name="fallback"/>.</summary>
    private static string? Get(Dictionary<string, string> map, string key, string? fallback)
        => map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    /// <summary>
    /// Upserts a key-value entry in <paramref name="fields"/>.
    /// Skips null values so existing non-null snapshot values are preserved (partial update).
    /// </summary>
    private static void MergeField(List<AiCollectedField> fields, string key, string? value)
    {
        if (value is null) return;

        var existing = fields.FirstOrDefault(f => f.Key == key);
        if (existing is not null)
            existing.Value = value;
        else
            fields.Add(new AiCollectedField { Key = key, Value = value });
    }

    /// <summary>
    /// Validates mandatory form fields and returns a list of field-level errors (AC-3).
    /// Returns an empty list when all required fields are satisfied.
    /// </summary>
    private static List<ManualIntakeValidationError> ValidateMandatory(ManualIntakeFields f)
    {
        var errors = new List<ManualIntakeValidationError>();

        if (string.IsNullOrWhiteSpace(f.FirstName))
            errors.Add(Error("firstName", "First name is required."));

        if (string.IsNullOrWhiteSpace(f.LastName))
            errors.Add(Error("lastName", "Last name is required."));

        if (string.IsNullOrWhiteSpace(f.DateOfBirth))
            errors.Add(Error("dateOfBirth", "Date of birth is required."));

        if (string.IsNullOrWhiteSpace(f.Gender))
            errors.Add(Error("gender", "Gender is required."));

        if (string.IsNullOrWhiteSpace(f.Phone))
            errors.Add(Error("phone", "Phone number is required."));

        if (string.IsNullOrWhiteSpace(f.KnownAllergies))
            errors.Add(Error("knownAllergies", "Known allergies (or \"None\") is required."));

        if (f.ConsentGiven != true)
            errors.Add(Error("consentGiven", "Consent must be confirmed before submission."));

        return errors;
    }

    private static ManualIntakeValidationError Error(string field, string message)
        => new() { Field = field, Message = message };

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// Converts a comma/semicolon-delimited medication string from the UI to
    /// the <c>List&lt;string&gt;</c> expected by <c>IntakeMandatoryFields</c>.
    /// </summary>
    private static List<string> SplitMedications(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();
    }
}
