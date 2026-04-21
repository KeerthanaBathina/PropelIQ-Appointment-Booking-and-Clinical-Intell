namespace UPACIP.Service.Appointments;

/// <summary>
/// Shared DTO contracts for the deterministic intake autosave API (US_030, FR-035, AC-1–AC-3, EC-1, EC-2).
///
/// Both AI conversational intake and the manual form share this envelope so the client's
/// <c>useIntakeAutosave</c> hook can treat both surfaces identically.
///
/// <b>Idempotency (EC-1)</b>:
/// Clients may include an optional <see cref="AutosaveDraftRequest.ClientSavedAt"/> timestamp.
/// When the server-side draft has a <c>LastAutoSavedAt</c> that is newer than or equal to
/// <c>ClientSavedAt</c>, the service interprets the call as a replay of an already-persisted
/// snapshot and returns the existing timestamp without writing again.
///
/// <b>Boundary persistence (EC-2)</b>:
/// The server always overwrites the current in-progress draft with the incoming snapshot.
/// Only the latest 30-second boundary payload from the client is retained — there is no
/// per-field delta or history; the prior draft is replaced atomically.
/// </summary>

// ─── Request ─────────────────────────────────────────────────────────────────

/// <summary>
/// Client-side boundary snapshot sent to the shared autosave endpoint.
///
/// For <b>AI intake</b>: <c>Mode = "ai"</c>; payload carries the in-progress
/// <see cref="CollectedCount"/> so the service can decide whether the snapshot is
/// meaningful.  The AI session service owns field-level state via Redis; the autosave
/// endpoint only needs to touch <c>LastAutoSavedAt</c> on the DB row.
///
/// For <b>manual intake</b>: <c>Mode = "manual"</c>; payload carries the full
/// <see cref="FormFields"/> dict so the service can overwrite the snapshot.
/// </summary>
public sealed record AutosaveDraftRequest
{
    /// <summary>Intake mode identifier: "ai" or "manual".</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// ISO-8601 timestamp of when the client last successfully saved.
    /// When provided the server uses this for idempotency detection (EC-1):
    /// if the server-side draft is already at or after this time, the write is skipped
    /// and the existing timestamp is returned.
    /// Null on first save.
    /// </summary>
    public string? ClientSavedAt { get; init; }

    /// <summary>
    /// For AI intake only: number of mandatory fields collected at the save boundary.
    /// Used to update the DB row's progress metadata without touching Redis session state.
    /// </summary>
    public int? CollectedCount { get; init; }

    /// <summary>
    /// For manual intake only: full form field snapshot at the 30-second boundary.
    /// Keys are camelCase form field names (firstName, phone, etc.).
    /// Null for AI mode saves.
    /// </summary>
    public Dictionary<string, string>? FormFields { get; init; }
}

// ─── Response ─────────────────────────────────────────────────────────────────

/// <summary>
/// Server confirmation returned after a successful autosave write (AC-1, AC-3).
/// The client stores <see cref="LastSavedAt"/> as its reference timestamp for the
/// next retry-detection call (EC-1).
/// </summary>
public sealed record AutosaveDraftResponse
{
    /// <summary>UTC ISO-8601 timestamp confirming when the draft was written (AC-1, AC-3).</summary>
    public string LastSavedAt { get; init; } = string.Empty;

    /// <summary>
    /// True when this call was detected as a retry replay of a snapshot already persisted
    /// (EC-1 idempotency). The client should treat this as a success.
    /// </summary>
    public bool WasIdempotentReplay { get; init; }

    /// <summary>Intake mode echoed back for client-side assertion and diagnostics.</summary>
    public string Mode { get; init; } = string.Empty;
}

// ─── Restore response ─────────────────────────────────────────────────────────

/// <summary>
/// Restore metadata returned when a draft is loaded after an interruption (AC-2).
/// Augments the existing <see cref="ManualIntakeDraftResponse"/> and
/// <see cref="StartAIIntakeResponse"/> with a common envelope so both surfaces
/// report restore freshness consistently.
/// </summary>
public sealed record IntakeRestoreMetadata
{
    /// <summary>ISO-8601 timestamp of the last successful server autosave (UXR-004).</summary>
    public string? LastSavedAt { get; init; }

    /// <summary>
    /// True when the restore loaded state from a prior autosave (vs. a completely fresh session).
    /// Drives the "Auto-saved" freshness indicator on the progress header (UXR-004).
    /// </summary>
    public bool IsRestored { get; init; }

    /// <summary>Intake mode this metadata applies to: "ai" or "manual".</summary>
    public string Mode { get; init; } = string.Empty;
}
