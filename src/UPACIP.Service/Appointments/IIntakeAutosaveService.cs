namespace UPACIP.Service.Appointments;

/// <summary>
/// Handles deterministic 30-second boundary autosave and post-interruption restore
/// for both AI conversational intake and the manual intake form (US_030, FR-035).
///
/// <b>AI mode</b>: The AI session state (Redis + DB) is already kept fresh by
/// <see cref="IAIIntakeSessionService.SendMessageAsync"/>. This service's
/// <see cref="SaveAISessionSnapshotAsync"/> provides a lightweight heartbeat: it touches
/// <c>LastAutoSavedAt</c> on the DB row and returns the updated timestamp so the client
/// can confirm persistence without re-uploading the full Redis session payload.
///
/// <b>Manual mode</b>: <see cref="SaveManualDraftSnapshotAsync"/> replaces the current
/// draft snapshot atomically. The prior draft is overwritten with the 30-second boundary
/// payload (EC-2 boundary-only persistence).
///
/// <b>Idempotency (EC-1)</b>: Both methods accept an optional <paramref name="clientSavedAt"/>
/// timestamp. When the server-side record is already at or after that time, the write is
/// skipped and the existing timestamp is returned with <c>WasIdempotentReplay = true</c>.
/// </summary>
public interface IIntakeAutosaveService
{
    /// <summary>
    /// Confirms persistence of the in-progress AI session and returns a fresh
    /// <c>lastSavedAt</c> timestamp (AC-1, AC-3).
    ///
    /// Returns <c>null</c> when the session does not exist or belongs to a different patient
    /// (OWASP A01 — no cross-patient data access).
    /// </summary>
    /// <param name="sessionId">AI intake session GUID.</param>
    /// <param name="patientId">Resolved from JWT — not trusted from client.</param>
    /// <param name="collectedCount">Optional field count for DB metadata update.</param>
    /// <param name="clientSavedAt">Optional idempotency guard timestamp from the client (EC-1).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AutosaveDraftResponse?> SaveAISessionSnapshotAsync(
        Guid sessionId,
        Guid patientId,
        int? collectedCount,
        DateTimeOffset? clientSavedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically overwrites the manual intake draft with the 30-second boundary snapshot
    /// and returns the server-confirmed <c>lastSavedAt</c> timestamp (AC-1, AC-3).
    ///
    /// Always succeeds — creates a new draft row when none exists (upsert semantics).
    /// </summary>
    /// <param name="patientId">Resolved from JWT — not trusted from client.</param>
    /// <param name="fields">Full form field snapshot at the save boundary.</param>
    /// <param name="clientSavedAt">Optional idempotency guard timestamp from the client (EC-1).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AutosaveDraftResponse> SaveManualDraftSnapshotAsync(
        Guid patientId,
        Dictionary<string, string> fields,
        DateTimeOffset? clientSavedAt,
        CancellationToken ct = default);
}
