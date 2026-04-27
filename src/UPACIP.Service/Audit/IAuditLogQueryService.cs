namespace UPACIP.Service.Audit;

/// <summary>
/// CQRS read-side interface for audit log queries (US_064 AC-3, Architecture Decision #5).
/// Separated from <see cref="UPACIP.Service.Auth.IAuditLogService"/> (write side) to isolate
/// query load from transactional write performance.
/// </summary>
public interface IAuditLogQueryService
{
    /// <summary>
    /// Returns a filtered, paginated page of audit log entries.
    /// All filters in <paramref name="request"/> are additive (AND logic).
    /// Uses keyset pagination for O(1) seek performance on large datasets (NFR-013, TR-013).
    /// </summary>
    Task<AuditLogQueryResponse> QueryAsync(
        AuditLogQueryRequest request,
        CancellationToken    ct = default);
}
