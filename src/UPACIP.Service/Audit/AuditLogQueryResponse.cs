namespace UPACIP.Service.Audit;

/// <summary>
/// Paginated response from <c>GET /api/audit-logs</c> (US_064 AC-3).
/// Uses keyset pagination: supply <see cref="NextCursor"/> as the <c>Cursor</c> parameter
/// on the next request to fetch the following page.
/// </summary>
public sealed record AuditLogQueryResponse(
    IReadOnlyList<AuditLogEntryDto> Items,
    Guid?                           NextCursor,
    long                            TotalCount,
    bool                            HasMore);
