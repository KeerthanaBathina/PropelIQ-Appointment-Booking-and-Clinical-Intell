namespace UPACIP.Service.AiAudit.Models;

/// <summary>
/// DTO representing a single AI request/response interaction written to the
/// <c>ai_audit_logs</c> table for compliance review (AIR-S04, AC-3, AC-4).
///
/// <para>
/// <b>PII policy:</b> The <see cref="Prompt"/> field MUST be the post-PII-redacted
/// version produced by <c>PiiRedactionMiddleware</c> (US_074 task_001).
/// Raw patient names, SSNs, or contact details MUST NOT appear here.
/// </para>
/// </summary>
public sealed record AiAuditLogEntry
{
    /// <summary>Surrogate UUID primary key. Assigned by the service before persistence.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Post-PII-redaction prompt text as submitted to the AI provider.
    /// Stored verbatim for audit replay capability (AC-3).
    /// </summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Full AI model response content.</summary>
    public string Response { get; init; } = string.Empty;

    /// <summary>
    /// Model identifier string as reported by the AI provider
    /// (e.g. <c>gpt-4o-mini-2024-07-18</c>, <c>claude-3-5-sonnet-20241022</c>).
    /// </summary>
    public string ModelVersion { get; init; } = string.Empty;

    /// <summary>Input token count from provider response metadata.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output token count from provider response metadata.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Sum of <see cref="InputTokens"/> and <see cref="OutputTokens"/>.</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>End-to-end request latency in milliseconds.</summary>
    public long LatencyMs { get; init; }

    /// <summary>
    /// Optional provider-reported confidence score in [0, 1].
    /// Null for request types that do not produce confidence scores.
    /// </summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>
    /// Request type classifier (e.g. <c>document-parsing</c>, <c>conversational-intake</c>,
    /// <c>medical-coding</c>, <c>rag-retrieval</c>).
    /// </summary>
    public string RequestType { get; init; } = string.Empty;

    /// <summary>
    /// Patient correlation ID for HIPAA compliance review (AIR-S04, AC-3).
    /// Null for system-level or administrative requests without patient context.
    /// </summary>
    public Guid? PatientId { get; init; }

    /// <summary>
    /// A/B experiment ID active at the time of the request (US_080 task_001).
    /// Null when no experiment was active.
    /// </summary>
    public Guid? AbExperimentId { get; init; }

    /// <summary>
    /// A/B variant assigned for this request (<c>Control</c> or <c>Candidate</c>).
    /// Null when no experiment was active.
    /// </summary>
    public string? AbVariant { get; init; }

    /// <summary>
    /// Authenticated user identifier (from <c>ClaimTypes.NameIdentifier</c>).
    /// Used for user-level compliance queries.
    /// </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the interaction was recorded.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
