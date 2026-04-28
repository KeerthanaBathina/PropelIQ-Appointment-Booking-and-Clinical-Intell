using UPACIP.Service.AiAudit.Models;

namespace UPACIP.Service.AiAudit;

/// <summary>
/// Contract for the AI audit logging pipeline (US_080 task_002, AIR-S04, AC-3, AC-4).
///
/// <para>
/// Implementations persist AI request/response interactions to the <c>ai_audit_logs</c>
/// partitioned table via a non-blocking channel-backed writer so audit logging never
/// stalls the AI Gateway response path.
/// </para>
/// </summary>
public interface IAiAuditService
{
    /// <summary>
    /// Enqueues an AI interaction record for asynchronous persistence to the
    /// <c>ai_audit_logs</c> table.
    ///
    /// <para>
    /// Returns immediately (channel write) in the happy path.
    /// If the internal channel is full, the entry is silently dropped and a warning
    /// is emitted via Serilog — audit logging MUST NOT block AI responses.
    /// </para>
    ///
    /// <para>
    /// <see cref="AiAuditLogEntry.Prompt"/> MUST be the post-PII-redacted version
    /// produced by <c>PiiRedactionMiddleware</c> (AIR-S01).
    /// </para>
    /// </summary>
    /// <param name="entry">Audit log data to persist.</param>
    /// <param name="ct">Propagates cancellation; use <see cref="CancellationToken.None"/> for fire-and-forget callers.</param>
    ValueTask LogAiInteractionAsync(AiAuditLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Queries audit log records with optional filtering and cursor-based pagination (AC-4).
    /// </summary>
    /// <param name="filter">Filter and pagination parameters.</param>
    /// <param name="ct">Propagates cancellation.</param>
    /// <returns>Paginated result with items, next cursor, total count, and has-more flag.</returns>
    Task<AiAuditQueryResult> QueryAuditLogsAsync(AiAuditQueryFilter filter, CancellationToken ct = default);
}
