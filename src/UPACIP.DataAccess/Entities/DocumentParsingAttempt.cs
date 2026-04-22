namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Immutable record of a single AI parsing attempt for a <see cref="ClinicalDocument"/>
/// (US_039 task_004, AC-4, AC-5, EC-1).
///
/// One row is appended for every attempt (initial + each retry). The row is never mutated
/// after insert, mirroring the audit-log pattern. The dispatcher reads the highest
/// <see cref="AttemptNumber"/> row per document to determine the next backoff window.
///
/// Does NOT inherit <see cref="BaseEntity"/> because:
/// — it uses a dedicated <c>AttemptId</c> PK name, and
/// — attempt records are append-only (no <c>UpdatedAt</c> is needed).
/// </summary>
public sealed class DocumentParsingAttempt
{
    // -------------------------------------------------------------------------
    // Primary key
    // -------------------------------------------------------------------------

    /// <summary>Surrogate UUID primary key.</summary>
    public Guid AttemptId { get; set; } = Guid.NewGuid();

    // -------------------------------------------------------------------------
    // Foreign key
    // -------------------------------------------------------------------------

    /// <summary>FK to the parent <see cref="ClinicalDocument"/>.</summary>
    public Guid DocumentId { get; set; }

    // -------------------------------------------------------------------------
    // Attempt metadata
    // -------------------------------------------------------------------------

    /// <summary>
    /// 1-based attempt counter. First attempt = 1, first retry = 2, etc.
    /// Stored so the dispatcher can skip already-seen attempts after restart (EC-1 idempotency).
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>UTC timestamp when this attempt started (status → Processing).</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when this attempt completed (success or failure).
    /// Null while the attempt is still in flight.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Short classification of the failure cause (e.g. "ProviderTimeout", "LowConfidence",
    /// "InvalidResponse", "StorageReadError"). Null on successful attempts.
    /// Never contains PII or raw model output (AIR-S01).
    /// </summary>
    public string? FailureCategory { get; set; }

    /// <summary>
    /// Human-readable failure description safe for staff display and structured log queries.
    /// Truncated to 1000 chars. Null on successful attempts.
    /// Never contains PII or raw model output (AIR-S01).
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// AI provider used for this attempt ("openai" or "anthropic").
    /// Null if the attempt failed before a provider was invoked (e.g. storage read error).
    /// </summary>
    public string? AiProvider { get; set; }

    /// <summary>
    /// Confidence score returned by the model on this attempt (0.0–1.0).
    /// Null if no model response was obtained.
    /// </summary>
    public double? ModelConfidence { get; set; }

    /// <summary>
    /// UTC timestamp after which a retry should be dispatched (exponential backoff schedule).
    /// Null on the final attempt (terminal failure) or successful attempt.
    /// Used by the dispatcher to find due retries after worker restarts (EC-1).
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>Parent clinical document.</summary>
    public ClinicalDocument Document { get; set; } = null!;
}
