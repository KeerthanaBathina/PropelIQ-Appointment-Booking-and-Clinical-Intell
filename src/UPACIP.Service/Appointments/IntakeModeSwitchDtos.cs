namespace UPACIP.Service.Appointments;

// ─── Shared field conflict descriptor ─────────────────────────────────────────

/// <summary>
/// Describes a value conflict between AI and manual intake sources (US_029, EC-1, AC-4).
/// The winning value is the most recent patient entry; the losing value is preserved here
/// for audit and UI attribution display (IntakeConflictNotice on the review screen).
/// </summary>
public sealed record IntakeFieldConflict
{
    /// <summary>Camel-case or AI field key of the conflicting field.</summary>
    public string FieldKey { get; init; } = string.Empty;

    /// <summary>The winning (most recent) value — returned in the main field payload.</summary>
    public string ActiveValue { get; init; } = string.Empty;

    /// <summary>The overridden (earlier) value — stored for audit and UI note (EC-1).</summary>
    public string AlternateValue { get; init; } = string.Empty;

    /// <summary>Which source owns the overridden value: "ai" or "manual".</summary>
    public string OverriddenSource { get; init; } = string.Empty;
}

// ─── AI-to-Manual switch ──────────────────────────────────────────────────────

/// <summary>
/// Response for <c>POST /api/intake/mode/switch-manual</c>.
/// Carries merged field values plus prefill and conflict metadata for the manual form UI (AC-1, AC-4).
/// </summary>
public sealed record SwitchToManualModeResponse
{
    /// <summary>Merged field values for pre-filling the manual form (AC-1).</summary>
    public IReadOnlyDictionary<string, string> PrefilledFields { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Camel-case field keys whose values were originally collected by AI.
    /// Used by the UI to render prefill badges (AC-2, <see cref="PrefilledFieldIndicator"/>).
    /// </summary>
    public IReadOnlyList<string> PrefilledKeys { get; init; } = [];

    /// <summary>Conflicts detected during merge — populated when switching back after manual edits (EC-1).</summary>
    public IReadOnlyList<IntakeFieldConflict> Conflicts { get; init; } = [];
}

// ─── Manual-to-AI switch ──────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/intake/manual/switch-ai</c>.
/// Carries the current form field values so the AI session can be pre-populated
/// and resume from the first uncollected field (AC-2, AC-3).
/// </summary>
public sealed record SwitchToAIRequest
{
    /// <summary>Current manual form field values to merge into the AI session.</summary>
    public ManualIntakeFields Fields { get; init; } = new();
}

/// <summary>
/// Response for <c>POST /api/intake/manual/switch-ai</c>.
/// Carries the session identifier and the next field the AI will ask so the UI
/// can resume the conversation at the correct position (AC-2).
/// </summary>
public sealed record SwitchToAIResponse
{
    /// <summary>AI intake session ID to resume.</summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The next uncollected field key, or null when all fields are already complete.
    /// Used by the FE to highlight progress continuity (AC-2).
    /// </summary>
    public string? NextField { get; init; }

    /// <summary>Conflicts detected when manual values overwrite earlier AI values (EC-1, AC-4).</summary>
    public IReadOnlyList<IntakeFieldConflict> Conflicts { get; init; } = [];
}

// ─── AI availability probe ────────────────────────────────────────────────────

/// <summary>
/// Response for <c>GET /api/intake/mode/ai-availability</c>.
/// Lets the UI disable switch-to-AI without waiting for a failing switch attempt (EC-2).
/// </summary>
public sealed record AIAvailabilityResponse
{
    /// <summary>True when the AI service is reachable and accepting sessions.</summary>
    public bool Available { get; init; }

    /// <summary>
    /// Human-readable reason for unavailability.  Null when available.
    /// Not surfaced in the UI but useful for support diagnostics (OWASP A09 — safe logging).
    /// </summary>
    public string? UnavailableReason { get; init; }

    /// <summary>ISO 8601 UTC check timestamp.</summary>
    public string CheckedAt { get; init; } = string.Empty;
}
