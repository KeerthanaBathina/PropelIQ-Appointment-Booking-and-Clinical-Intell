using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements deterministic 30-second boundary autosave and post-interruption restore
/// for both intake surfaces (US_030, FR-035, AC-1–AC-3, EC-1, EC-2).
///
/// <b>AI mode</b>: The AI session is already kept current in Redis + DB by
/// <see cref="AIIntakeSessionService.SendMessageAsync"/>. This service acts as a
/// lightweight heartbeat: it verifies the session belongs to the caller, updates
/// <c>LastAutoSavedAt</c> on the DB row, and returns the fresh timestamp.
/// It does NOT re-push session payload to Redis (avoiding write amplification).
///
/// <b>Manual mode</b>: Atomically replaces the draft snapshot with the boundary payload.
/// Uses the same upsert logic as <see cref="ManualIntakeService.SaveDraftAsync"/> but
/// accepts a raw field dictionary to avoid an extra service-layer hop.
///
/// <b>Security</b>: Both paths validate that the DB row's <c>PatientId</c> matches the
/// JWT-resolved <paramref name="patientId"/> before any write (OWASP A01, US_030 EC-1).
///
/// <b>Idempotency (EC-1)</b>: When <paramref name="clientSavedAt"/> is provided and the
/// server record is already at or after that instant, the write is skipped and the
/// existing timestamp is returned with <c>WasIdempotentReplay = true</c>.
///
/// PII policy: patient identifiers appear in structured log properties only as opaque GUIDs.
/// </summary>
public sealed class IntakeAutosaveService : IIntakeAutosaveService
{
    private const string StatusManualDraft = "manual_draft";
    private const string StatusManual      = "manual";

    // Redis key prefixes — must match AIIntakeSessionService constants (shared boundary).
    private const string SessionKeyPrefix = "ai-intake:session:";

    private readonly ApplicationDbContext                _db;
    private readonly ICacheService                       _cache;
    private readonly ILogger<IntakeAutosaveService>      _logger;

    public IntakeAutosaveService(
        ApplicationDbContext            db,
        ICacheService                   cache,
        ILogger<IntakeAutosaveService>  logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─── AI Session Autosave ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AutosaveDraftResponse?> SaveAISessionSnapshotAsync(
        Guid sessionId,
        Guid patientId,
        int? collectedCount,
        DateTimeOffset? clientSavedAt,
        CancellationToken ct = default)
    {
        // 1. Load the draft row — must match both sessionId AND patientId (OWASP A01).
        var draft = await _db.IntakeRecords
            .Where(i => i.AiSessionId == sessionId
                     && i.PatientId   == patientId
                     && (i.AiSessionStatus == "active"
                         || i.AiSessionStatus == "summary"
                         || i.AiSessionStatus == "manual"))   // "manual" path: session switched but row still exists
            .FirstOrDefaultAsync(ct);

        if (draft is null)
        {
            _logger.LogWarning(
                "IntakeAutosaveSvc: AI session {SessionId} not found or access denied for patient {PatientId}.",
                sessionId, patientId);
            return null;
        }

        var now = DateTime.UtcNow;

        // 2. Idempotency guard (EC-1): if client's prior save time is already reflected
        //    on the server, treat this as a replay and return without writing.
        if (clientSavedAt.HasValue
            && draft.LastAutoSavedAt.HasValue
            && draft.LastAutoSavedAt.Value >= clientSavedAt.Value.UtcDateTime)
        {
            _logger.LogDebug(
                "IntakeAutosaveSvc: AI session {SessionId} — idempotent replay detected; skipping write.",
                sessionId);

            return new AutosaveDraftResponse
            {
                LastSavedAt         = draft.LastAutoSavedAt.Value.ToString("O"),
                WasIdempotentReplay = true,
                Mode                = "ai",
            };
        }

        // 3. Confirm the session is still alive in Redis (lightweight key existence check).
        //    If Redis has the key the session is warm; if not the DB row is the source of truth.
        //    Either way we proceed with the DB touch — this is just for logging.
        var redisKey  = SessionKeyPrefix + sessionId.ToString();
        var isInRedis = await _cache.GetAsync<object>(redisKey, ct) is not null;

        if (!isInRedis)
        {
            _logger.LogInformation(
                "IntakeAutosaveSvc: AI session {SessionId} not found in Redis; DB row used as source-of-truth for autosave.",
                sessionId);
        }

        // 4. Touch LastAutoSavedAt (EC-2 boundary write).
        draft.LastAutoSavedAt = now;

        // Update progress count on the snapshot if provided.
        if (collectedCount.HasValue && draft.AiSessionSnapshot is not null)
        {
            draft.AiSessionSnapshot.CollectedFields.RemoveAll(f => f.Key == "__collected_count");
            draft.AiSessionSnapshot.CollectedFields.Add(new AiCollectedField
            {
                Key   = "__collected_count",
                Value = collectedCount.Value.ToString(),
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "IntakeAutosaveSvc: AI session {SessionId} autosaved for patient {PatientId}; collectedCount={Count}.",
            sessionId, patientId, collectedCount);

        return new AutosaveDraftResponse
        {
            LastSavedAt         = now.ToString("O"),
            WasIdempotentReplay = false,
            Mode                = "ai",
        };
    }

    // ─── Manual Draft Autosave ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AutosaveDraftResponse> SaveManualDraftSnapshotAsync(
        Guid patientId,
        Dictionary<string, string> fields,
        DateTimeOffset? clientSavedAt,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1. Find the most recent incomplete manual draft row for this patient.
        var draft = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId
                     && (i.AiSessionStatus == StatusManual || i.AiSessionStatus == StatusManualDraft)
                     && i.CompletedAt == null)
            .OrderByDescending(i => i.LastAutoSavedAt)
            .FirstOrDefaultAsync(ct);

        // 2. Idempotency guard (EC-1): if the incoming clientSavedAt is not newer than
        //    what is already on the server, skip the write.
        if (clientSavedAt.HasValue
            && draft?.LastAutoSavedAt.HasValue == true
            && draft.LastAutoSavedAt!.Value >= clientSavedAt.Value.UtcDateTime)
        {
            _logger.LogDebug(
                "IntakeAutosaveSvc: manual draft for patient {PatientId} — idempotent replay detected; skipping write.",
                patientId);

            return new AutosaveDraftResponse
            {
                LastSavedAt         = draft.LastAutoSavedAt.Value.ToString("O"),
                WasIdempotentReplay = true,
                Mode                = "manual",
            };
        }

        // 3. Upsert: create a new row when no draft exists yet.
        if (draft is null)
        {
            draft = new IntakeData
            {
                Id              = Guid.NewGuid(),
                PatientId       = patientId,
                IntakeMethod    = IntakeMethod.ManualForm,
                AiSessionStatus = StatusManualDraft,
                LastAutoSavedAt = now,
                AiSessionSnapshot = new AiSessionSnapshot
                {
                    Status          = StatusManualDraft,
                    CollectedFields = [],
                },
            };
            _db.IntakeRecords.Add(draft);
        }
        else
        {
            draft.AiSessionStatus = StatusManualDraft;
            draft.LastAutoSavedAt = now;
        }

        // 4. Replace the snapshot with the boundary payload (EC-2 boundary-only persistence).
        //    Preserve the prefilled-keys sentinel if present; all other fields are replaced.
        draft.AiSessionSnapshot ??= new AiSessionSnapshot
        {
            Status          = StatusManualDraft,
            CollectedFields = [],
        };

        const string prefilledSentinelKey = "__prefilled_keys";

        var sentinelEntry = draft.AiSessionSnapshot.CollectedFields
            .FirstOrDefault(f => f.Key == prefilledSentinelKey);

        // Replace all non-sentinel entries with the incoming boundary snapshot.
        draft.AiSessionSnapshot.CollectedFields.RemoveAll(f => f.Key != prefilledSentinelKey);

        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key) || key == prefilledSentinelKey)
                continue;

            draft.AiSessionSnapshot.CollectedFields.Add(new AiCollectedField
            {
                Key   = key,
                Value = value ?? string.Empty,
            });
        }

        // Re-add the sentinel if it was present (preserves AI prefill attribution).
        if (sentinelEntry is not null
            && !draft.AiSessionSnapshot.CollectedFields.Any(f => f.Key == prefilledSentinelKey))
        {
            draft.AiSessionSnapshot.CollectedFields.Add(sentinelEntry);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "IntakeAutosaveSvc: manual draft autosaved for patient {PatientId}; draftId={DraftId}.",
            patientId, draft.Id);

        return new AutosaveDraftResponse
        {
            LastSavedAt         = now.ToString("O"),
            WasIdempotentReplay = false,
            Mode                = "manual",
        };
    }
}
