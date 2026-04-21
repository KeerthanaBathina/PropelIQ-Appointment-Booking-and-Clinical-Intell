namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// JSONB owned type stored in the <c>ai_session_snapshot</c> column of <c>intake_data</c>.
///
/// Captures the complete in-progress conversational intake state needed to resume
/// a session after a Redis TTL expiry (EC-2: session timeout recovery).
///
/// Design notes:
///   - Stored as a single JSONB column so the full state is loaded atomically.
///   - Collected fields are stored as a key-value list (not a Dictionary) because
///     EF Core 8 <c>ToJson()</c> does not natively support <c>Dictionary&lt;K,V&gt;</c>
///     as an owned-type property. The service layer converts to/from a dictionary.
///   - This type is only populated for <c>AiConversational</c> intake records.
/// </summary>
public sealed class AiSessionSnapshot
{
    /// <summary>Session lifecycle status: "active" | "summary" | "completed" | "manual".</summary>
    public string Status { get; set; } = "active";

    /// <summary>Field key last presented to the patient. Used to resume from the right question.</summary>
    public string? CurrentFieldKey { get; set; }

    /// <summary>Total number of AI exchange turns in this session.</summary>
    public int TurnCount { get; set; }

    /// <summary>
    /// Consecutive AI provider failures, used for circuit-breaker tracking across
    /// a Redis TTL recovery (NFR-022, UXR-605).
    /// </summary>
    public int ConsecutiveProviderFailures { get; set; }

    /// <summary>
    /// Snapshot of all collected field values.
    /// Stored as a flat key-value list for EF Core JSON compatibility (EC-2 restore).
    /// </summary>
    public List<AiCollectedField> CollectedFields { get; set; } = [];
}

/// <summary>
/// A single key-value pair in <see cref="AiSessionSnapshot.CollectedFields"/>.
/// Mapped as a JSON array element so EF Core can serialise it correctly with <c>ToJson()</c>.
/// </summary>
public sealed class AiCollectedField
{
    public string Key   { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
