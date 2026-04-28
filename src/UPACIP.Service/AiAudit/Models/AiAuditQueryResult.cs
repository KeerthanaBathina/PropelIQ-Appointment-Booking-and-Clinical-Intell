namespace UPACIP.Service.AiAudit.Models;

/// <summary>
/// Paginated result returned by <see cref="IAiAuditService.QueryAuditLogsAsync"/>
/// (AC-4).
///
/// <para>
/// Uses keyset (cursor-based) pagination to provide stable, efficient result pages
/// across the partitioned <c>ai_audit_logs</c> table. Callers advance through results
/// by passing <see cref="NextCursor"/> into the next query's
/// <see cref="AiAuditQueryFilter.Cursor"/> field.
/// </para>
/// </summary>
public sealed record AiAuditQueryResult
{
    /// <summary>Audit log entries on the current page (up to <c>PageSize</c> items).</summary>
    public IReadOnlyList<AiAuditLogEntry> Items { get; init; } = [];

    /// <summary>
    /// Opaque cursor for the next page.
    /// Null when <see cref="HasMore"/> is <see langword="false"/>.
    /// </summary>
    public string? NextCursor { get; init; }

    /// <summary>Total count of matching records (may be expensive on large datasets).</summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// True when additional pages are available beyond the current result set.
    /// </summary>
    public bool HasMore { get; init; }
}
