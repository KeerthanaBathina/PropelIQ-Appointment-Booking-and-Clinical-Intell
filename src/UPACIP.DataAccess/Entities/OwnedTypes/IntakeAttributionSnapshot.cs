namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// JSONB owned type stored in the <c>intake_attribution</c> column of <c>intake_data</c>.
///
/// Captures the complete source-provenance and mode-switch audit trail for an intake session
/// so that, after any number of AI ↔ manual switches, the system can reconstruct exactly which
/// mode produced each accepted value and which earlier value was superseded (US_029, AC-3,
/// AC-4, EC-1, EC-2).
///
/// Design notes:
///   - Stored as a single JSONB column so all attribution metadata is loaded atomically
///     alongside the session snapshot (no extra joins on resume or submit flows).
///   - Nested collections use <c>List&lt;T&gt;</c> because EF Core 8 <c>ToJson()</c> does not
///     natively support <c>Dictionary&lt;K,V&gt;</c> as an owned-type property.
///   - NULL (absent column) means no mode switch has occurred — fully backward-compatible
///     with existing AI-only or manual-only intake records that never switch modes.
///   - New per-field attributions are appended on every switch; stale entries for the same
///     <see cref="IntakeFieldAttribution.FieldKey"/> are overwritten so the list always
///     represents the current accepted provenance, not a full edit history (space-efficient).
///   - <see cref="ConflictNotes"/> and <see cref="ModeSwitchEvents"/> are append-only audit
///     logs so the full provenance chain can be reconstructed at submission time (EC-2).
/// </summary>
public sealed class IntakeAttributionSnapshot
{
    /// <summary>
    /// Per-field source attribution — records which intake mode produced the
    /// current accepted value for each field key collected during this session.
    /// Entries are upserted on each switch so the list is authoritative at any point in time.
    /// </summary>
    public List<IntakeFieldAttribution> FieldAttributions { get; set; } = [];

    /// <summary>
    /// Conflict audit log — an entry is appended whenever a newer value from one mode
    /// supersedes an earlier value from a different mode (EC-1: most-recent-entry wins;
    /// the replaced value is retained here for auditability).
    /// </summary>
    public List<IntakeConflictNote> ConflictNotes { get; set; } = [];

    /// <summary>
    /// Ordered log of mode-switch events for this intake session.
    /// Enables provenance reconstruction of the full AI ↔ manual switch history (EC-2).
    /// </summary>
    public List<IntakeModeSwitchEvent> ModeSwitchEvents { get; set; } = [];
}

/// <summary>
/// Source attribution for a single collected intake field.
/// Records which intake mode produced the current accepted value for
/// <see cref="FieldKey"/> and when that value was last updated.
/// </summary>
public sealed class IntakeFieldAttribution
{
    /// <summary>
    /// Canonical form-field key identifying which field this attribution applies to.
    /// For manual form fields: camelCase form key (e.g., "firstName", "phone").
    /// For AI session fields: AI field key (e.g., "full_name", "contact_phone").
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// The intake mode that produced the currently accepted value:
    /// <c>"ai"</c> for AI conversational intake, <c>"manual"</c> for manual form entry.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this value was collected or last updated by the stated source.
    /// Used to distinguish the provenance of values entered across multiple mode switches.
    /// </summary>
    public DateTime CollectedAt { get; set; }
}

/// <summary>
/// Audit note created when a newer value from one intake mode supersedes an earlier
/// value from a different mode (EC-1: most-recent-entry wins; replaced value retained).
///
/// A new entry is appended to <see cref="IntakeAttributionSnapshot.ConflictNotes"/>
/// for every field conflict detected during a mode-switch merge operation.
/// </summary>
public sealed class IntakeConflictNote
{
    /// <summary>Canonical field key where the conflict was detected.</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>The value that won under the most-recent-entry-wins rule.</summary>
    public string WinningValue { get; set; } = string.Empty;

    /// <summary>
    /// The intake mode (<c>"ai"</c> or <c>"manual"</c>) that produced the winning value.
    /// </summary>
    public string WinningSource { get; set; } = string.Empty;

    /// <summary>The earlier value that was superseded by the winning value.</summary>
    public string ReplacedValue { get; set; } = string.Empty;

    /// <summary>
    /// The intake mode (<c>"ai"</c> or <c>"manual"</c>) that produced the replaced value.
    /// </summary>
    public string ReplacedSource { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the conflict was detected and resolved during the switch operation.</summary>
    public DateTime RecordedAt { get; set; }
}

/// <summary>
/// Audit record of a single AI ↔ manual mode-switch event.
/// Appended to <see cref="IntakeAttributionSnapshot.ModeSwitchEvents"/> on every successful
/// switch operation so the complete transition history can be reconstructed (EC-2).
/// </summary>
public sealed class IntakeModeSwitchEvent
{
    /// <summary>
    /// The intake mode the patient was leaving: <c>"ai"</c> or <c>"manual"</c>.
    /// </summary>
    public string FromMode { get; set; } = string.Empty;

    /// <summary>
    /// The intake mode the patient was entering: <c>"ai"</c> or <c>"manual"</c>.
    /// </summary>
    public string ToMode { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the switch operation was initiated by the patient.</summary>
    public DateTime SwitchedAt { get; set; }

    /// <summary>
    /// Optional HTTP correlation ID linking this switch event to a specific API request
    /// in Serilog structured logs for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; set; }
}
