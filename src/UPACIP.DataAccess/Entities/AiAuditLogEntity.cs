namespace UPACIP.DataAccess.Entities;

/// <summary>
/// EF Core entity backing the <c>ai_audit_logs</c> partitioned table.
/// Stores the full AI request/response audit trail for compliance review
/// (AIR-S04, US_080 task_002, AC-3, AC-4).
///
/// <para>
/// <b>Table design:</b>
/// The physical table is partitioned by <c>RANGE (created_at)</c> with monthly partitions
/// (see <c>scripts/create-ai-audit-partitions.sql</c>).  EF Core treats it as a regular
/// table and remains unaware of partitioning; all writes and reads route to the correct
/// partition automatically via PostgreSQL.
/// </para>
///
/// <para>
/// <b>Retention policy:</b>
/// <list type="bullet">
///   <item>Hot storage 90 days — partition stays attached to the parent table.</item>
///   <item>Archive 90–365 days — partition detached into <c>ai_audit_logs_archive</c> schema.</item>
///   <item>Permanent delete after 365 days — partition dropped via DDL.</item>
/// </list>
/// </para>
///
/// <para>
/// Does NOT extend <see cref="BaseEntity"/> because audit records are immutable
/// append-only entries (no <c>UpdatedAt</c> required) and the primary key name
/// follows the convention of this entity type.
/// </para>
///
/// <para>
/// <b>PII policy:</b> <see cref="Prompt"/> MUST always contain the post-PII-redacted
/// version produced by <c>PiiRedactionMiddleware</c>.  Raw patient identifiers
/// MUST NOT be stored in this column.
/// </para>
/// </summary>
public sealed class AiAuditLogEntity
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Post-PII-redacted prompt text (AC-3, AIR-S01).</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Full AI model response content.</summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Model identifier string (e.g. <c>gpt-4o-mini-2024-07-18</c>).</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Input token count as reported by the provider.</summary>
    public int InputTokens { get; set; }

    /// <summary>Output token count as reported by the provider.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Sum of <see cref="InputTokens"/> and <see cref="OutputTokens"/>.</summary>
    public int TotalTokens { get; set; }

    /// <summary>End-to-end request latency in milliseconds.</summary>
    public long LatencyMs { get; set; }

    /// <summary>
    /// Provider-reported confidence score in [0, 1].
    /// Null for request types that do not produce confidence scores.
    /// </summary>
    public float? ConfidenceScore { get; set; }

    /// <summary>Request type classifier (e.g. <c>document-parsing</c>).</summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>Patient correlation ID for HIPAA compliance review (AIR-S04).</summary>
    public Guid? PatientId { get; set; }

    /// <summary>A/B experiment ID active at request time (US_080 task_001). Null if none.</summary>
    public Guid? AbExperimentId { get; set; }

    /// <summary>A/B variant (<c>Control</c> | <c>Candidate</c>). Null if no experiment.</summary>
    public string? AbVariant { get; set; }

    /// <summary>Authenticated user identifier (ClaimTypes.NameIdentifier).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the interaction. Drives partition routing.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
