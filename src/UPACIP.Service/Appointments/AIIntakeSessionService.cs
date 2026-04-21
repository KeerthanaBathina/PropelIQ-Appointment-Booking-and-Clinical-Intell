using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Deterministic session lifecycle orchestrator for AI conversational intake (US_027, FR-026).
///
/// <b>Dual-layer persistence strategy</b>:
/// 1. <b>Redis</b> (<see cref="ICacheService"/>, 60-min TTL) — primary hot-path read/write so
///    every exchange round-trip stays in memory without a DB round-trip (AC-2 latency).
/// 2. <b>PostgreSQL draft row</b> (<c>IntakeData</c> with <c>AiSessionStatus = "active"</c>)
///    — written after every exchange as the EC-2 autosave target. When Redis TTL expires
///    (patient timeout), <see cref="LoadSessionAsync"/> restores state from DB and rehydrates
///    Redis so subsequent requests proceed without data loss (EC-2 full restore).
///
/// Session ownership: every operation validates that <c>CachedIntakeSession.PatientId</c>
/// matches the caller's patientId (OWASP A01 — Broken Access Control).
///
/// Draft-to-final promotion: on <see cref="CompleteSessionAsync"/>, the existing draft
/// <c>IntakeData</c> row is updated in-place (status → "completed", MandatoryFields populated)
/// instead of creating a new row — ensuring only one record exists per session.
/// </summary>
public sealed class AIIntakeSessionService : IAIIntakeSessionService
{
    // Redis key patterns — namespaced under "upacip:" (prefix configured in RedisCacheService)
    private const string SessionKeyPrefix   = "ai-intake:session:";  // {sessionId}
    private const string PatientIndexPrefix = "ai-intake:patient:";  // {patientId} → active sessionId

    // Session TTL: 60 minutes — covers an intake session with generous headroom (EC-2)
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(60);

    // Mandatory fields count for progress metadata (FR-029, AC-3)
    private static readonly int TotalMandatoryFields =
        IntakeFieldDefinitions.MandatoryOrder.Count;

    private static readonly string GreetingMessage =
        "Hello! I'm your AI health assistant. I'll help you complete your intake information " +
        "through a short conversation. This should only take a few minutes. " +
        "Let's start — could you please tell me your full legal name?";

    private readonly IConversationalIntakeService _aiService;
    private readonly ICacheService                _cache;
    private readonly ApplicationDbContext         _db;
    private readonly ILogger<AIIntakeSessionService> _logger;

    public AIIntakeSessionService(
        IConversationalIntakeService    aiService,
        ICacheService                   cache,
        ApplicationDbContext            db,
        ILogger<AIIntakeSessionService> logger)
    {
        _aiService = aiService;
        _cache     = cache;
        _db        = db;
        _logger    = logger;
    }

    // ─── Start or Resume (AC-1, EC-2) ─────────────────────────────────────────

    public async Task<StartAIIntakeResponse> StartOrResumeSessionAsync(
        Guid patientId,
        string patientEmail,
        CancellationToken ct = default)
    {
        // 1. Try Redis hot-path (normal case — no timeout has occurred)
        var existingSessionId = await _cache.GetAsync<string>(
            PatientIndexPrefix + patientId.ToString(), ct);

        if (existingSessionId is not null && Guid.TryParse(existingSessionId, out var resumeId))
        {
            var existing = await _cache.GetAsync<CachedIntakeSession>(
                SessionKeyPrefix + resumeId.ToString(), ct);

            if (existing is not null && existing.PatientId == patientId
                && existing.Status == IntakeSessionStatus.Active)
            {
                _logger.LogInformation(
                    "AIIntakeSvc: Resuming session {SessionId} from Redis for patient {PatientId}.",
                    resumeId, patientId);

                return BuildStartResponse(existing, isResumed: true);
            }
        }

        // 2. Redis miss — check DB for an active draft row (EC-2 timeout recovery)
        var dbDraft = await _db.IntakeRecords
            .AsNoTracking()
            .Where(i => i.PatientId == patientId
                     && i.AiSessionStatus == "active"
                     && i.AiSessionId != null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        if (dbDraft?.AiSessionSnapshot is not null)
        {
            var restored = RestoreSessionFromDraft(dbDraft);

            // Rehydrate Redis so subsequent calls hit the hot-path again
            await SaveSessionToRedisAsync(restored, ct);

            _logger.LogInformation(
                "AIIntakeSvc: Restored session {SessionId} from DB draft for patient {PatientId} (EC-2).",
                restored.SessionId, patientId);

            return BuildStartResponse(restored, isResumed: true);
        }

        // 3. No active session anywhere — create new session + draft DB row
        var sessionId = Guid.NewGuid();
        var session = new CachedIntakeSession
        {
            SessionId       = sessionId,
            PatientId       = patientId,
            Status          = IntakeSessionStatus.Active,
            CollectedFields = new Dictionary<string, string>(StringComparer.Ordinal),
            History         = [],
            TurnCount       = 0,
            ConsecutiveProviderFailures = 0,
            CreatedAt       = DateTimeOffset.UtcNow,
            LastSavedAt     = DateTimeOffset.UtcNow,
        };

        // Create draft DB row first, then save to Redis
        await CreateDraftDbRowAsync(session, ct);
        await SaveSessionToRedisAsync(session, ct);

        _logger.LogInformation(
            "AIIntakeSvc: New session {SessionId} created for patient {PatientId}; dbRowId={DbRowId}.",
            sessionId, patientId, session.DbIntakeDataId);

        return BuildStartResponse(session, isResumed: false);
    }

    // ─── Send Message (AC-2, AC-5) ────────────────────────────────────────────

    public async Task<AIIntakeMessageResponse?> SendMessageAsync(
        Guid sessionId,
        Guid patientId,
        string content,
        CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, patientId, ct);
        if (session is null) return null;

        var currentFieldKey = IntakeFieldDefinitions.NextFieldToCollect(session.CollectedFields)
            ?? IntakeFieldDefinitions.AllFields[0]; // fallback — should not be reached

        // Build context for the AI orchestration layer
        var context = new IntakeSessionContext
        {
            SessionId       = sessionId,
            PatientId       = patientId,
            CurrentFieldKey = currentFieldKey,
            CollectedFields = session.CollectedFields,
            History         = session.History
                .Select(t => new ConversationTurn { Role = t.Role, Content = t.Content, Timestamp = t.Timestamp })
                .ToList(),
            TurnCount       = session.TurnCount,
            ConsecutiveProviderFailures = session.ConsecutiveProviderFailures,
        };

        // Delegate to AI orchestration (never throws — returns ShouldSwitchToManual on failure)
        var result = await _aiService.ProcessMessageAsync(context, content, ct);

        // Update session state
        session.History.Add(new CachedTurn
        {
            Role      = "user",
            Content   = content,
            Timestamp = DateTimeOffset.UtcNow,
        });
        session.History.Add(new CachedTurn
        {
            Role                 = "assistant",
            Content              = result.ReplyToPatient,
            Timestamp            = DateTimeOffset.UtcNow,
            ClarificationExamples = result.ClarificationExamples.Count > 0
                ? [.. result.ClarificationExamples]
                : null,
        });

        if (result.IsFieldComplete && result.ExtractedValue is not null)
        {
            session.CollectedFields[result.FieldKey] = result.ExtractedValue;
        }

        session.TurnCount++;
        session.ConsecutiveProviderFailures = result.ShouldSwitchToManual
            ? session.ConsecutiveProviderFailures + 1
            : 0;
        session.LastSavedAt = DateTimeOffset.UtcNow;

        if (result.ShouldSwitchToManual)
        {
            session.Status = IntakeSessionStatus.SwitchedToManual;
        }

        // Dual-layer persist: Redis hot-path + DB autosave for EC-2 recovery
        await SaveSessionAsync(session, ct);

        var collectedMandatoryCount = CountMandatoryCollected(session.CollectedFields);

        return new AIIntakeMessageResponse
        {
            ReplyToPatient       = result.ReplyToPatient,
            FieldKey             = result.FieldKey,
            ExtractedValue       = result.ExtractedValue,
            NeedsClarification   = result.NeedsClarification,
            ClarificationExamples = result.ClarificationExamples,
            CollectedCount       = collectedMandatoryCount,
            TotalRequired        = TotalMandatoryFields,
            SummaryReady         = result.IsSummaryReady,
            ShouldSwitchToManual = result.ShouldSwitchToManual,
            LastSavedAt          = session.LastSavedAt.ToString("O"),
            Provider             = result.Provider,
        };
    }

    // ─── Summary (AC-4) ───────────────────────────────────────────────────────

    public async Task<AIIntakeSummaryResponse?> GetSummaryAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, patientId, ct);
        if (session is null) return null;

        var summaryResult = await _aiService.GenerateSummaryAsync(
            sessionId, session.CollectedFields, ct);

        return new AIIntakeSummaryResponse
        {
            SummaryText            = summaryResult.SummaryText,
            Fields                 = summaryResult.Fields
                .Select(f => new AIIntakeSummaryFieldDto
                {
                    Key        = f.Key,
                    Label      = f.Label,
                    Value      = f.Value,
                    IsMandatory = f.IsMandatory,
                    IsEditable  = f.IsEditable,
                })
                .ToList(),
            MandatoryCollectedCount = summaryResult.MandatoryCollectedCount,
            MandatoryTotalCount     = summaryResult.MandatoryTotalCount,
        };
    }

    // ─── Complete (AC-4, FR-029) ──────────────────────────────────────────────

    public async Task<CompleteIntakeResponse?> CompleteSessionAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, patientId, ct);
        if (session is null) return null;

        // Require mandatory fields to be complete before finalising (FR-029, AC-3)
        if (!IntakeFieldDefinitions.AreMandatoryFieldsComplete(session.CollectedFields))
        {
            _logger.LogWarning(
                "AIIntakeSvc: CompleteSession rejected — mandatory fields incomplete. " +
                "sessionId={SessionId}, patientId={PatientId}.",
                sessionId, patientId);
            return null;
        }

        // Promote draft row → final completed row (UC-002)
        Guid finalIntakeDataId;
        DateTime completedAt = DateTime.UtcNow;

        if (session.DbIntakeDataId.HasValue)
        {
            // Update the existing draft row in-place: populate clinical fields + mark completed
            var draft = await _db.IntakeRecords
                .FirstOrDefaultAsync(i => i.Id == session.DbIntakeDataId.Value, ct);

            if (draft is not null)
            {
                PopulateIntakeDataFields(draft, session);
                draft.AiSessionStatus = "completed";
                draft.CompletedAt     = completedAt;
                draft.UpdatedAt       = completedAt;
                await _db.SaveChangesAsync(ct);
                finalIntakeDataId = draft.Id;
            }
            else
            {
                // Draft row gone (e.g. external delete) — insert fresh final row
                var freshRow = BuildIntakeData(patientId, session);
                _db.IntakeRecords.Add(freshRow);
                await _db.SaveChangesAsync(ct);
                finalIntakeDataId = freshRow.Id;
            }
        }
        else
        {
            // No draft row tracked in this session object — create a final row directly
            var newRow = BuildIntakeData(patientId, session);
            _db.IntakeRecords.Add(newRow);
            await _db.SaveChangesAsync(ct);
            finalIntakeDataId = newRow.Id;
        }

        // Mark session completed in cache
        session.Status = IntakeSessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        await SaveSessionToRedisAsync(session, ct);

        // Remove patient index so the next start creates a fresh session
        await _cache.RemoveAsync(PatientIndexPrefix + patientId.ToString(), ct);

        _logger.LogInformation(
            "AIIntakeSvc: Session {SessionId} completed; intakeDataId={IntakeDataId}; patientId={PatientId}.",
            sessionId, finalIntakeDataId, patientId);

        return new CompleteIntakeResponse
        {
            IntakeDataId = finalIntakeDataId,
            CompletedAt  = completedAt.ToString("O"),
        };
    }

    // ─── Switch to Manual (FL-004, US_028) ────────────────────────────────────

    public async Task<SwitchToManualResponse?> SwitchToManualAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, patientId, ct);
        if (session is null) return null;

        // Mark session transferred (DB + Redis) so resume never re-attaches to it
        session.Status = IntakeSessionStatus.SwitchedToManual;
        await SaveSessionAsync(session, ct);

        // Remove patient index so a future start does not re-attach to this session
        await _cache.RemoveAsync(PatientIndexPrefix + patientId.ToString(), ct);

        _logger.LogInformation(
            "AIIntakeSvc: Session {SessionId} switched to manual form. fieldsCollected={Count}.",
            sessionId, session.CollectedFields.Count);

        return new SwitchToManualResponse
        {
            PrefilledFields = new Dictionary<string, string>(session.CollectedFields, StringComparer.Ordinal),
        };
    }

    // ─── Resume from Manual (US_029, AC-2, AC-3) ──────────────────────────────

    public async Task<ResumeFromManualResult?> ResumeFromManualAsync(
        Guid patientId,
        IReadOnlyDictionary<string, string> manualFields,
        CancellationToken ct = default)
    {
        // 1. Find or create an active AI session for this patient
        var existingSessionId = await _cache.GetAsync<string>(
            PatientIndexPrefix + patientId.ToString(), ct);

        CachedIntakeSession? session = null;

        if (existingSessionId is not null && Guid.TryParse(existingSessionId, out var resumeId))
        {
            session = await _cache.GetAsync<CachedIntakeSession>(
                SessionKeyPrefix + resumeId.ToString(), ct);

            if (session?.PatientId != patientId
                || session.Status == IntakeSessionStatus.Completed)
                session = null;
        }

        // Attempt DB restore if Redis miss
        if (session is null)
        {
            var dbDraft = await _db.IntakeRecords
                .AsNoTracking()
                .Where(i => i.PatientId == patientId
                         && i.AiSessionId != null
                         && (i.AiSessionStatus == "active" || i.AiSessionStatus == "manual"))
                .OrderByDescending(i => i.LastAutoSavedAt)
                .FirstOrDefaultAsync(ct);

            if (dbDraft?.AiSessionSnapshot is not null)
            {
                session = RestoreSessionFromDraft(dbDraft);
                await SaveSessionToRedisAsync(session, ct);
            }
        }

        // Create a new session when none found (first-time switch-to-AI from manual)
        if (session is null)
        {
            session = new CachedIntakeSession
            {
                SessionId       = Guid.NewGuid(),
                PatientId       = patientId,
                Status          = IntakeSessionStatus.Active,
                CollectedFields = new Dictionary<string, string>(StringComparer.Ordinal),
                History         = [],
                TurnCount       = 0,
                ConsecutiveProviderFailures = 0,
                CreatedAt       = DateTimeOffset.UtcNow,
                LastSavedAt     = DateTimeOffset.UtcNow,
            };
            await CreateDraftDbRowAsync(session, ct);
        }

        // 2. Merge manual field values into the session using most-recent-entry-wins (EC-1)
        //    Map camelCase form field names to the AI field key namespace
        var conflicts = new List<IntakeFieldConflict>();
        var fieldMap = BuildManualToAIFieldMap(manualFields);

        foreach (var (aiKey, newValue) in fieldMap)
        {
            if (string.IsNullOrWhiteSpace(newValue)) continue;

            if (session.CollectedFields.TryGetValue(aiKey, out var existingValue)
                && !string.IsNullOrWhiteSpace(existingValue)
                && existingValue != newValue)
            {
                // Conflict: manual value overrides earlier AI value (EC-1, AC-4)
                conflicts.Add(new IntakeFieldConflict
                {
                    FieldKey        = aiKey,
                    ActiveValue     = newValue,
                    AlternateValue  = existingValue,
                    OverriddenSource = "ai",
                });
            }

            session.CollectedFields[aiKey] = newValue;
        }

        // 3. Transition session back to Active if it was SwitchedToManual
        session.Status      = IntakeSessionStatus.Active;
        session.LastSavedAt = DateTimeOffset.UtcNow;

        await SaveSessionAsync(session, ct);

        // Restore patient index so the next AI page load resumes this session
        await _cache.SetAsync(
            PatientIndexPrefix + patientId.ToString(),
            session.SessionId.ToString(),
            SessionTtl,
            ct);

        var nextField = IntakeFieldDefinitions.NextFieldToCollect(session.CollectedFields);

        _logger.LogInformation(
            "AIIntakeSvc: ResumeFromManual; patient={PatientId}, sessionId={SessionId}, nextField={NextField}, conflicts={ConflictCount}.",
            patientId, session.SessionId, nextField ?? "none", conflicts.Count);

        return new ResumeFromManualResult
        {
            SessionId = session.SessionId,
            NextField = nextField,
            Conflicts = conflicts,
        };
    }

    /// <summary>
    /// Maps camelCase manual-form field names to AI field key namespace for merge operations (US_029).
    /// Only non-null, non-whitespace source values are included.
    /// </summary>
    private static Dictionary<string, string> BuildManualToAIFieldMap(
        IReadOnlyDictionary<string, string> manualFields)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        void Map(string formKey, string aiKey)
        {
            if (manualFields.TryGetValue(formKey, out var v) && !string.IsNullOrWhiteSpace(v))
                result[aiKey] = v;
        }

        // Personal information
        if (manualFields.TryGetValue("firstName", out var fn) && !string.IsNullOrWhiteSpace(fn)
            && manualFields.TryGetValue("lastName",  out var ln) && !string.IsNullOrWhiteSpace(ln))
        {
            result[IntakeFieldDefinitions.FullName] = $"{fn.Trim()} {ln.Trim()}";
        }

        Map("dateOfBirth",           IntakeFieldDefinitions.DateOfBirth);
        Map("phone",                 IntakeFieldDefinitions.ContactPhone);
        Map("emergencyContact",      IntakeFieldDefinitions.EmergencyContactName);

        // Medical
        Map("knownAllergies",        IntakeFieldDefinitions.KnownAllergies);
        Map("currentMedications",    IntakeFieldDefinitions.CurrentMedications);
        Map("preExistingConditions", IntakeFieldDefinitions.MedicalHistory);

        // Insurance
        Map("insuranceProvider",     IntakeFieldDefinitions.InsuranceProvider);
        Map("policyNumber",          IntakeFieldDefinitions.InsurancePolicyNumber);

        return result;
    }

    private async Task<CachedIntakeSession?> LoadSessionAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct)
    {
        // Try Redis first (hot-path)
        var session = await _cache.GetAsync<CachedIntakeSession>(
            SessionKeyPrefix + sessionId.ToString(), ct);

        if (session is null)
        {
            // Redis TTL expired — attempt DB restore (EC-2)
            _logger.LogInformation(
                "AIIntakeSvc: Session {SessionId} not in Redis; attempting DB restore (EC-2).", sessionId);

            var dbDraft = await _db.IntakeRecords
                .AsNoTracking()
                .Where(i => i.AiSessionId == sessionId
                         && i.PatientId   == patientId
                         && (i.AiSessionStatus == "active" || i.AiSessionStatus == "summary"))
                .FirstOrDefaultAsync(ct);

            if (dbDraft?.AiSessionSnapshot is null)
            {
                _logger.LogWarning(
                    "AIIntakeSvc: Session {SessionId} not found in Redis or DB.", sessionId);
                return null;
            }

            session = RestoreSessionFromDraft(dbDraft);

            // Rehydrate Redis so subsequent calls avoid DB round-trips
            await SaveSessionToRedisAsync(session, ct);

            _logger.LogInformation(
                "AIIntakeSvc: Session {SessionId} restored from DB draft (EC-2).", sessionId);
        }

        // Ownership check (OWASP A01 — Broken Access Control)
        if (session.PatientId != patientId)
        {
            _logger.LogWarning(
                "AIIntakeSvc: Ownership mismatch — session {SessionId} does not belong to patient {PatientId}.",
                sessionId, patientId);
            return null;
        }

        return session;
    }

    private async Task SaveSessionAsync(CachedIntakeSession session, CancellationToken ct)
    {
        // Write to both layers: Redis (hot-path) + DB draft row (EC-2 autosave)
        await SaveSessionToRedisAsync(session, ct);
        await UpsertDraftDbRowAsync(session, ct);
    }

    private async Task SaveSessionToRedisAsync(CachedIntakeSession session, CancellationToken ct)
    {
        await _cache.SetAsync(
            SessionKeyPrefix + session.SessionId.ToString(),
            session,
            SessionTtl,
            ct);

        // Maintain patient → active session index (only for active sessions)
        if (session.Status == IntakeSessionStatus.Active)
        {
            await _cache.SetAsync(
                PatientIndexPrefix + session.PatientId.ToString(),
                session.SessionId.ToString(),
                SessionTtl,
                ct);
        }
    }

    /// <summary>
    /// Creates the initial draft <c>IntakeData</c> row when a new AI session starts.
    /// Stores the session ID so DB → Redis restore can locate the row by session ID.
    /// </summary>
    private async Task CreateDraftDbRowAsync(CachedIntakeSession session, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var draft = new IntakeData
        {
            Id              = Guid.NewGuid(),
            PatientId       = session.PatientId,
            IntakeMethod    = IntakeMethod.AiConversational,
            AiSessionId     = session.SessionId,
            AiSessionStatus = "active",
            LastAutoSavedAt = now,
            AiSessionSnapshot = BuildSessionSnapshot(session),
            CreatedAt       = now,
            UpdatedAt       = now,
        };

        _db.IntakeRecords.Add(draft);
        await _db.SaveChangesAsync(ct);

        // Track DB row ID in the cached session so Complete can update it in-place
        session.DbIntakeDataId = draft.Id;
    }

    /// <summary>
    /// Upserts the draft <c>IntakeData</c> row with the latest session snapshot (EC-2 autosave).
    /// Skips when no DB row is tracked (edge case: first save failed to create the draft).
    /// </summary>
    private async Task UpsertDraftDbRowAsync(CachedIntakeSession session, CancellationToken ct)
    {
        if (!session.DbIntakeDataId.HasValue) return;

        var statusString = session.Status switch
        {
            IntakeSessionStatus.Active         => "active",
            IntakeSessionStatus.Completed      => "completed",
            IntakeSessionStatus.SwitchedToManual => "manual",
            _                                  => "active",
        };

        var now = DateTime.UtcNow;
        await _db.IntakeRecords
            .Where(i => i.Id == session.DbIntakeDataId.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(i => i.AiSessionStatus,   statusString)
                .SetProperty(i => i.LastAutoSavedAt,   now)
                .SetProperty(i => i.AiSessionSnapshot, BuildSessionSnapshot(session))
                .SetProperty(i => i.UpdatedAt,         now),
                ct);
    }

    private static AiSessionSnapshot BuildSessionSnapshot(CachedIntakeSession session)
        => new()
        {
            Status                     = session.Status switch
            {
                IntakeSessionStatus.Active         => "active",
                IntakeSessionStatus.Completed      => "completed",
                IntakeSessionStatus.SwitchedToManual => "manual",
                _                                  => "active",
            },
            CurrentFieldKey                    = IntakeFieldDefinitions.NextFieldToCollect(session.CollectedFields),
            TurnCount                          = session.TurnCount,
            ConsecutiveProviderFailures        = session.ConsecutiveProviderFailures,
            CollectedFields                    = session.CollectedFields
                .Select(kv => new AiCollectedField { Key = kv.Key, Value = kv.Value })
                .ToList(),
        };

    private static CachedIntakeSession RestoreSessionFromDraft(IntakeData draft)
    {
        var snapshot = draft.AiSessionSnapshot!;
        var fields   = snapshot.CollectedFields
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

        return new CachedIntakeSession
        {
            SessionId                   = draft.AiSessionId!.Value,
            PatientId                   = draft.PatientId,
            DbIntakeDataId              = draft.Id,
            Status                      = snapshot.Status == "manual"
                                          ? IntakeSessionStatus.SwitchedToManual
                                          : IntakeSessionStatus.Active,
            CollectedFields             = fields,
            History                     = [], // conversation history not persisted (EC-2 only restores field progress)
            TurnCount                   = snapshot.TurnCount,
            ConsecutiveProviderFailures = snapshot.ConsecutiveProviderFailures,
            CreatedAt                   = new DateTimeOffset(draft.CreatedAt, TimeSpan.Zero),
            LastSavedAt                 = draft.LastAutoSavedAt.HasValue
                                          ? new DateTimeOffset(draft.LastAutoSavedAt.Value, TimeSpan.Zero)
                                          : new DateTimeOffset(draft.UpdatedAt, TimeSpan.Zero),
        };
    }

    private static StartAIIntakeResponse BuildStartResponse(CachedIntakeSession session, bool isResumed)
    {
        var history = isResumed
            ? session.History.Select(t => new AIIntakeTurnDto
            {
                Id        = Guid.NewGuid().ToString(),
                Role      = t.Role,
                Content   = t.Content,
                Timestamp = t.Timestamp.ToString("O"),
                ClarificationExamples = t.ClarificationExamples,
            }).ToList()
            : new List<AIIntakeTurnDto>();

        var greeting = isResumed
            ? $"Welcome back! Let's continue where we left off. " +
              $"We've collected {CountMandatoryCollected(session.CollectedFields)} of " +
              $"{TotalMandatoryFields} required fields."
            : GreetingMessage;

        return new StartAIIntakeResponse
        {
            SessionId      = session.SessionId,
            IsResumed      = isResumed,
            GreetingMessage = greeting,
            History        = history,
            CollectedCount = CountMandatoryCollected(session.CollectedFields),
            TotalRequired  = TotalMandatoryFields,
            LastSavedAt    = isResumed ? session.LastSavedAt.ToString("O") : null,
        };
    }

    private static IntakeData BuildIntakeData(Guid patientId, CachedIntakeSession session)
    {
        var now = DateTime.UtcNow;
        var intakeData = new IntakeData
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            IntakeMethod    = IntakeMethod.AiConversational,
            AiSessionId     = session.SessionId,
            AiSessionStatus = "completed",
            CompletedAt     = now,
            CreatedAt       = now,
            UpdatedAt       = now,
        };
        PopulateIntakeDataFields(intakeData, session);
        return intakeData;
    }

    private static void PopulateIntakeDataFields(IntakeData intakeData, CachedIntakeSession session)
    {
        var cf = session.CollectedFields;

        // Map collected AI fields → IntakeData owned types.
        // Patient demographics (name, DOB, phone) go to the Patient entity already registered;
        // clinical fields are mapped here to MandatoryFields/OptionalFields for the clinical record.
        intakeData.MandatoryFields = new IntakeMandatoryFields
        {
            MedicalHistory     = cf.GetValueOrDefault(IntakeFieldDefinitions.MedicalHistory) ?? string.Empty,
            Allergies          = cf.GetValueOrDefault(IntakeFieldDefinitions.KnownAllergies) ?? string.Empty,
            CurrentMedications = ParseMedications(cf.GetValueOrDefault(IntakeFieldDefinitions.CurrentMedications)),
            ChiefComplaint     = string.Empty, // populated by clinical staff post-intake
        };

        intakeData.OptionalFields = new IntakeOptionalFields
        {
            AdditionalNotes = BuildAdditionalNotes(cf),
        };
    }

    private static List<string> ParseMedications(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();
    }

    private static string? BuildAdditionalNotes(IReadOnlyDictionary<string, string> cf)
    {
        var parts = new List<string>();

        if (cf.TryGetValue(IntakeFieldDefinitions.InsuranceProvider, out var ins))
            parts.Add($"Insurance Provider: {ins}");
        if (cf.TryGetValue(IntakeFieldDefinitions.InsurancePolicyNumber, out var pol))
            parts.Add($"Policy Number: {pol}");

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static int CountMandatoryCollected(IReadOnlyDictionary<string, string> fields)
        => IntakeFieldDefinitions.MandatoryOrder
            .Count(k => fields.ContainsKey(k) && !string.IsNullOrWhiteSpace(fields[k]));
}

// ─── Session state types (Redis-serialised) ───────────────────────────────────

/// <summary>In-memory / Redis-serialised AI intake session state (EC-2 recovery).</summary>
internal sealed class CachedIntakeSession
{
    public Guid SessionId { get; init; }
    public Guid PatientId { get; init; }
    public IntakeSessionStatus Status { get; set; }
    public Dictionary<string, string> CollectedFields { get; init; } = new(StringComparer.Ordinal);
    public List<CachedTurn> History { get; init; } = [];
    public int TurnCount { get; set; }
    public int ConsecutiveProviderFailures { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSavedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>
    /// DB primary key of the draft <c>IntakeData</c> row for this session.
    /// Set after <see cref="AIIntakeSessionService.CreateDraftDbRowAsync"/> succeeds so
    /// the completion path can update the row in-place instead of inserting a duplicate.
    /// </summary>
    public Guid? DbIntakeDataId { get; set; }
}

internal sealed class CachedTurn
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string[]? ClarificationExamples { get; init; }
}

internal enum IntakeSessionStatus
{
    Active,
    Completed,
    SwitchedToManual,
}
