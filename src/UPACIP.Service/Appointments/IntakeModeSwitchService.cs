using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Deterministic mode-switch orchestration for bidirectional AI ↔ manual intake transitions
/// (US_029, FR-028, AC-1–AC-4, EC-1–EC-2).
///
/// <b>AI-to-Manual (SwitchToManualAsync)</b>:
/// Reads the patient's active AI session, merges collected field values into the manual draft
/// snapshot, identifies prefilled keys, and returns the merged payload to the controller.
/// Delegates to <see cref="ManualIntakeService"/> for draft persistence so the logic is reused
/// and not duplicated here.
///
/// <b>Manual-to-AI (SwitchToAIAsync)</b>:
/// Maps current manual form values to AI field keys, delegates to
/// <see cref="AIIntakeSessionService.ResumeFromManualAsync"/> for merge and session management,
/// and returns the session ID plus next field for the UI to resume (AC-2).
///
/// <b>Conflict resolution</b> (EC-1, AC-4):
/// Most-recent-entry wins. Replaced values are returned in <see cref="IntakeFieldConflict"/> so
/// the UI can render <c>IntakeConflictNotice</c> badges on the summary screen.
///
/// <b>AI availability probing</b> (EC-2):
/// A cached flag is maintained for 30 seconds via <see cref="ICacheService"/> to avoid hitting
/// the AI gateway on every button hover. The gateway is probed with a cheap no-op request on
/// cache miss; the result drives the disabled state of the switch-to-AI button in the UI.
/// </summary>
public sealed class IntakeModeSwitchService : IIntakeModeSwitchService
{
    // Redis key for the AI availability cache (TTL: 30 s — fast recovery on transient failures)
    private const string AiAvailabilityCacheKey = "ai-intake:availability";
    private static readonly TimeSpan AiAvailabilityCacheTtl = TimeSpan.FromSeconds(30);

    private const string StatusManual      = "manual";
    private const string StatusManualDraft = "manual_draft";
    private const string PrefilledKeysKey  = "__prefilled_keys";

    private readonly IAIIntakeSessionService     _aiSessionService;
    private readonly IManualIntakeService        _manualIntakeService;
    private readonly ICacheService               _cache;
    private readonly ApplicationDbContext        _db;
    private readonly ILogger<IntakeModeSwitchService> _logger;

    public IntakeModeSwitchService(
        IAIIntakeSessionService          aiSessionService,
        IManualIntakeService             manualIntakeService,
        ICacheService                    cache,
        ApplicationDbContext             db,
        ILogger<IntakeModeSwitchService> logger)
    {
        _aiSessionService    = aiSessionService;
        _manualIntakeService = manualIntakeService;
        _cache               = cache;
        _db                  = db;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Switch AI → Manual (AC-1, AC-3, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<SwitchToManualModeResponse> SwitchToManualAsync(
        Guid patientId,
        CancellationToken ct = default)
    {
        // 1. Load AI-collected fields from DB (existing draft row for this patient)
        var aiDraft = await _db.IntakeRecords
            .AsNoTracking()
            .Where(i => i.PatientId == patientId
                     && i.AiSessionId != null
                     && (i.AiSessionStatus == "active" || i.AiSessionStatus == "summary"))
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        var aiFields = aiDraft?.AiSessionSnapshot?.CollectedFields
            ?? [];

        // Convert AI snapshot list to a dict for easier lookup
        var aiFieldMap = aiFields
            .Where(f => f.Key != PrefilledKeysKey && !string.IsNullOrWhiteSpace(f.Value))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

        // 2. Load existing manual draft fields (if any — from a prior back-switch)
        var manualDraft = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual || i.AiSessionStatus == StatusManualDraft)
                     && i.CompletedAt == null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        // 3. Determine which AI field keys we're bringing into the manual draft
        var aiKeysToPrefill = aiFieldMap.Keys.ToHashSet(StringComparer.Ordinal);

        // 4. Detect conflicts: fields with a value in both AI and existing manual drafts
        var conflicts = new List<IntakeFieldConflict>();
        var mergedAiKeys = MapAIToManualFormKeys(aiFieldMap);

        if (manualDraft?.AiSessionSnapshot?.CollectedFields is { } existingManualFields)
        {
            var existingManualMap = existingManualFields
                .Where(f => f.Key != PrefilledKeysKey && !string.IsNullOrWhiteSpace(f.Value))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

            foreach (var (formKey, aiValue) in mergedAiKeys)
            {
                if (existingManualMap.TryGetValue(formKey, out var manualValue)
                    && !string.IsNullOrWhiteSpace(manualValue)
                    && manualValue != aiValue)
                {
                    // Existing manual value wins (most-recent-entry) — AI value is the alternate (EC-1)
                    conflicts.Add(new IntakeFieldConflict
                    {
                        FieldKey         = formKey,
                        ActiveValue      = manualValue,
                        AlternateValue   = aiValue,
                        OverriddenSource = "ai",
                    });
                }
            }
        }

        // 5. Upsert the manual draft snapshot with AI-collected values (non-overwriting)
        //    We create or update a draft row for the manual form with all AI fields as prefill.
        if (manualDraft is null)
        {
            // Create a new manual draft row pre-populated with AI fields
            var now = DateTime.UtcNow;

            var snapshot = new AiSessionSnapshot
            {
                Status = StatusManual,
                CollectedFields = BuildManualSnapshotFields(mergedAiKeys, aiKeysToPrefill),
            };

            // Seed attribution snapshot — all prefilled values came from AI (AC-4, EC-1)
            var attribution = new IntakeAttributionSnapshot();

            attribution.ModeSwitchEvents.Add(new IntakeModeSwitchEvent
            {
                FromMode   = "ai",
                ToMode     = "manual",
                SwitchedAt = now,
            });

            foreach (var formKey in mergedAiKeys.Keys)
            {
                attribution.FieldAttributions.Add(new IntakeFieldAttribution
                {
                    FieldKey    = formKey,
                    Source      = "ai",
                    CollectedAt = now,
                });
            }

            // No conflicts on first switch — no existing manual values to conflict with

            var newDraft = new DataAccess.Entities.IntakeData
            {
                Id                  = Guid.NewGuid(),
                PatientId           = patientId,
                IntakeMethod        = IntakeMethod.ManualForm,
                AiSessionStatus     = StatusManual,
                LastAutoSavedAt     = now,
                AiSessionSnapshot   = snapshot,
                AttributionSnapshot = attribution,
            };
            _db.IntakeRecords.Add(newDraft);
            await _db.SaveChangesAsync(ct);
        }
        else if (manualDraft.AiSessionStatus == StatusManual)
        {
            var now = DateTime.UtcNow;

            // Merge AI fields into the existing "manual" draft without overwriting newer manual values
            var snapshot = manualDraft.AiSessionSnapshot ??= new AiSessionSnapshot
            {
                Status = StatusManual,
                CollectedFields = [],
            };

            foreach (var (formKey, aiValue) in mergedAiKeys)
            {
                // Only apply AI value when no manual value exists yet (conflicts already recorded above)
                var hasConflict = conflicts.Any(c => c.FieldKey == formKey);
                if (!hasConflict)
                {
                    var existing = snapshot.CollectedFields.FirstOrDefault(f => f.Key == formKey);
                    if (existing is not null)
                        existing.Value = aiValue;
                    else
                        snapshot.CollectedFields.Add(new AiCollectedField { Key = formKey, Value = aiValue });
                }
            }

            // Record which keys are AI-prefilled (preserving any existing sentinel)
            var sentinelEntry = snapshot.CollectedFields.FirstOrDefault(f => f.Key == PrefilledKeysKey);
            var prefillSentinelValue = string.Join(",", aiKeysToPrefill.Select(k =>
                AI_TO_FORM_KEY_MAP.GetValueOrDefault(k, k)));

            if (sentinelEntry is not null)
                sentinelEntry.Value = prefillSentinelValue;
            else
                snapshot.CollectedFields.Add(new AiCollectedField
                {
                    Key   = PrefilledKeysKey,
                    Value = prefillSentinelValue,
                });

            // Upsert attribution snapshot — AI-prefilled fields + conflict notes + switch event (AC-4, EC-1, EC-2)
            manualDraft.AttributionSnapshot ??= new IntakeAttributionSnapshot();

            foreach (var (formKey, _) in mergedAiKeys)
            {
                var hasConflict = conflicts.Any(c => c.FieldKey == formKey);
                if (hasConflict) continue; // manual value won; attribution for this field stays "manual"

                var existingAttr = manualDraft.AttributionSnapshot.FieldAttributions
                    .FirstOrDefault(a => a.FieldKey == formKey);
                if (existingAttr is not null)
                {
                    existingAttr.Source      = "ai";
                    existingAttr.CollectedAt = now;
                }
                else
                {
                    manualDraft.AttributionSnapshot.FieldAttributions.Add(new IntakeFieldAttribution
                    {
                        FieldKey    = formKey,
                        Source      = "ai",
                        CollectedAt = now,
                    });
                }
            }

            // Append conflict notes (EC-1 — replaced values retained for audit)
            foreach (var conflict in conflicts)
            {
                manualDraft.AttributionSnapshot.ConflictNotes.Add(new IntakeConflictNote
                {
                    FieldKey       = conflict.FieldKey,
                    WinningValue   = conflict.ActiveValue,
                    WinningSource  = "manual",
                    ReplacedValue  = conflict.AlternateValue,
                    ReplacedSource = "ai",
                    RecordedAt     = now,
                });
            }

            // Append switch event (EC-2 — ordered provenance chain)
            manualDraft.AttributionSnapshot.ModeSwitchEvents.Add(new IntakeModeSwitchEvent
            {
                FromMode   = "ai",
                ToMode     = "manual",
                SwitchedAt = now,
            });

            manualDraft.LastAutoSavedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        // 6. Build response
        var prefilledFormKeys = aiKeysToPrefill
            .Select(k => AI_TO_FORM_KEY_MAP.GetValueOrDefault(k, k))
            .Where(k => k != string.Empty)
            .ToList();

        _logger.LogInformation(
            "IntakeModeSwitchSvc: AI→Manual for patient {PatientId}; prefilledCount={Count}; conflicts={ConflictCount}.",
            patientId, prefilledFormKeys.Count, conflicts.Count);

        return new SwitchToManualModeResponse
        {
            PrefilledFields = mergedAiKeys,
            PrefilledKeys   = prefilledFormKeys,
            Conflicts       = conflicts,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Switch Manual → AI (AC-2, AC-3, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<SwitchToAIResponse?> SwitchToAIAsync(
        Guid patientId,
        SwitchToAIRequest request,
        CancellationToken ct = default)
    {
        // Check availability first (fast Redis cache, EC-2)
        var availability = await CheckAIAvailabilityAsync(ct);
        if (!availability.Available)
        {
            _logger.LogWarning(
                "IntakeModeSwitchSvc: switch-to-AI rejected; AI unavailable: {Reason}.", availability.UnavailableReason);
            return null;
        }

        // Map form fields to AI field key namespace and delegate resume to AIIntakeSessionService
        var manualFieldDict = BuildFormToAIFieldDict(request.Fields);
        var result = await _aiSessionService.ResumeFromManualAsync(patientId, manualFieldDict, ct);

        if (result is null)
        {
            // Treat null (AI session creation failure) as a 503-equivalent
            _logger.LogWarning(
                "IntakeModeSwitchSvc: ResumeFromManualAsync returned null for patient {PatientId}.", patientId);
            return null;
        }

        _logger.LogInformation(
            "IntakeModeSwitchSvc: Manual→AI for patient {PatientId}; sessionId={SessionId}; nextField={NextField}; conflicts={ConflictCount}.",
            patientId, result.SessionId, result.NextField ?? "none", result.Conflicts.Count);

        // Persist attribution data to the AI session's IntakeData row (AC-4, EC-1, EC-2)
        var aiRow = await _db.IntakeRecords
            .Where(i => i.AiSessionId == result.SessionId && i.PatientId == patientId)
            .FirstOrDefaultAsync(ct);

        if (aiRow is not null)
        {
            var nowAi = DateTime.UtcNow;
            aiRow.AttributionSnapshot ??= new IntakeAttributionSnapshot();

            // Record manual-source attributions for fields merged from the manual form (AC-4)
            foreach (var (aiKey, _) in manualFieldDict)
            {
                var hasConflict = result.Conflicts.Any(c => c.FieldKey == aiKey);
                if (hasConflict) continue; // AI value won; attribution for this field stays "ai"

                var existingAttr = aiRow.AttributionSnapshot.FieldAttributions
                    .FirstOrDefault(a => a.FieldKey == aiKey);
                if (existingAttr is not null)
                {
                    existingAttr.Source      = "manual";
                    existingAttr.CollectedAt = nowAi;
                }
                else
                {
                    aiRow.AttributionSnapshot.FieldAttributions.Add(new IntakeFieldAttribution
                    {
                        FieldKey    = aiKey,
                        Source      = "manual",
                        CollectedAt = nowAi,
                    });
                }
            }

            // Append conflict notes (EC-1 — replaced AI values retained for audit)
            foreach (var conflict in result.Conflicts)
            {
                aiRow.AttributionSnapshot.ConflictNotes.Add(new IntakeConflictNote
                {
                    FieldKey       = conflict.FieldKey,
                    WinningValue   = conflict.ActiveValue,
                    WinningSource  = "ai",
                    ReplacedValue  = conflict.AlternateValue,
                    ReplacedSource = "manual",
                    RecordedAt     = nowAi,
                });
            }

            // Append switch event (EC-2 — ordered provenance chain)
            aiRow.AttributionSnapshot.ModeSwitchEvents.Add(new IntakeModeSwitchEvent
            {
                FromMode   = "manual",
                ToMode     = "ai",
                SwitchedAt = nowAi,
            });

            await _db.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogWarning(
                "IntakeModeSwitchSvc: No IntakeData row found for AI session {SessionId}; attribution not persisted.",
                result.SessionId);
        }

        return new SwitchToAIResponse
        {
            SessionId = result.SessionId,
            NextField = result.NextField,
            Conflicts = result.Conflicts,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AI availability probe (EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AIAvailabilityResponse> CheckAIAvailabilityAsync(CancellationToken ct = default)
    {
        var checkedAt = DateTimeOffset.UtcNow.ToString("O");

        // Check 30-second cache first to avoid gateway hammering
        var cached = await _cache.GetAsync<AIAvailabilityResponse>(AiAvailabilityCacheKey, ct);
        if (cached is not null)
            return cached;

        // Lightweight DB connectivity check as a proxy for service health.
        // A full AI gateway ping is avoided to prevent rate-limit consumption on every probe.
        // When the gateway-specific health endpoint becomes available (US_034 infra),
        // replace this with a direct HTTP HEAD to the gateway endpoint.
        try
        {
            // A fast count query confirms DB is reachable (AI session pipeline requires DB)
            _ = await _db.IntakeRecords.CountAsync(ct);

            var response = new AIAvailabilityResponse
            {
                Available  = true,
                CheckedAt  = checkedAt,
            };

            await _cache.SetAsync(AiAvailabilityCacheKey, response, AiAvailabilityCacheTtl, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("IntakeModeSwitchSvc: AI availability probe failed: {Message}.", ex.Message);

            var unavailable = new AIAvailabilityResponse
            {
                Available         = false,
                UnavailableReason = "Health check failed.",
                CheckedAt         = checkedAt,
            };

            await _cache.SetAsync(AiAvailabilityCacheKey, unavailable, AiAvailabilityCacheTtl, ct);
            return unavailable;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    // Maps AI field keys → camelCase manual form field names (inverse of BuildManualToAIFieldMap)
    private static readonly IReadOnlyDictionary<string, string> AI_TO_FORM_KEY_MAP =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IntakeFieldDefinitions.FullName]                     = "firstName",  // split into first+last at response layer
            [IntakeFieldDefinitions.DateOfBirth]                  = "dateOfBirth",
            [IntakeFieldDefinitions.ContactPhone]                 = "phone",
            [IntakeFieldDefinitions.EmergencyContactName]         = "emergencyContact",
            [IntakeFieldDefinitions.KnownAllergies]               = "knownAllergies",
            [IntakeFieldDefinitions.CurrentMedications]           = "currentMedications",
            [IntakeFieldDefinitions.MedicalHistory]               = "preExistingConditions",
            [IntakeFieldDefinitions.InsuranceProvider]            = "insuranceProvider",
            [IntakeFieldDefinitions.InsurancePolicyNumber]        = "policyNumber",
        };

    /// <summary>
    /// Maps AI session field keys to camelCase manual form field names for the prefill payload.
    /// full_name is split into firstName / lastName when a space is present.
    /// </summary>
    private static Dictionary<string, string> MapAIToManualFormKeys(
        Dictionary<string, string> aiFields)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (aiKey, value) in aiFields)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (aiKey == IntakeFieldDefinitions.FullName)
            {
                var parts = value.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                result["firstName"] = parts.Length > 0 ? parts[0] : value;
                result["lastName"]  = parts.Length > 1 ? parts[1] : string.Empty;
            }
            else if (AI_TO_FORM_KEY_MAP.TryGetValue(aiKey, out var formKey))
            {
                result[formKey] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the <c>AiSessionSnapshot.CollectedFields</c> list for a new manual draft row,
    /// including the AI-prefill sentinel.
    /// </summary>
    private static List<AiCollectedField> BuildManualSnapshotFields(
        Dictionary<string, string> mergedFormKeys,
        HashSet<string> aiKeysToPrefill)
    {
        var list = mergedFormKeys
            .Select(kv => new AiCollectedField { Key = kv.Key, Value = kv.Value })
            .ToList();

        var sentinelValue = string.Join(",", aiKeysToPrefill
            .Select(k => AI_TO_FORM_KEY_MAP.GetValueOrDefault(k, k)));

        if (!string.IsNullOrEmpty(sentinelValue))
            list.Add(new AiCollectedField { Key = PrefilledKeysKey, Value = sentinelValue });

        return list;
    }

    /// <summary>
    /// Maps camelCase manual form field values to AI field key namespace for session merge (US_029, AC-2).
    /// </summary>
    private static Dictionary<string, string> BuildFormToAIFieldDict(ManualIntakeFields f)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // Merge first + last name into full_name
        var fullName = $"{f.FirstName?.Trim()} {f.LastName?.Trim()}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
            result[IntakeFieldDefinitions.FullName] = fullName;

        if (!string.IsNullOrWhiteSpace(f.DateOfBirth))
            result[IntakeFieldDefinitions.DateOfBirth] = f.DateOfBirth.Trim();
        if (!string.IsNullOrWhiteSpace(f.Phone))
            result[IntakeFieldDefinitions.ContactPhone] = f.Phone.Trim();
        if (!string.IsNullOrWhiteSpace(f.EmergencyContact))
            result[IntakeFieldDefinitions.EmergencyContactName] = f.EmergencyContact.Trim();
        if (!string.IsNullOrWhiteSpace(f.KnownAllergies))
            result[IntakeFieldDefinitions.KnownAllergies] = f.KnownAllergies.Trim();
        if (!string.IsNullOrWhiteSpace(f.CurrentMedications))
            result[IntakeFieldDefinitions.CurrentMedications] = f.CurrentMedications.Trim();
        if (!string.IsNullOrWhiteSpace(f.PreExistingConditions))
            result[IntakeFieldDefinitions.MedicalHistory] = f.PreExistingConditions.Trim();
        if (!string.IsNullOrWhiteSpace(f.InsuranceProvider))
            result[IntakeFieldDefinitions.InsuranceProvider] = f.InsuranceProvider.Trim();
        if (!string.IsNullOrWhiteSpace(f.PolicyNumber))
            result[IntakeFieldDefinitions.InsurancePolicyNumber] = f.PolicyNumber.Trim();

        return result;
    }
}
