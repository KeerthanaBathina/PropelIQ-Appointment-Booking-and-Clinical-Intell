namespace UPACIP.Contracts.Services;

/// <summary>
/// CQRS read-side interface for audit log compliance queries (US_096, AC-3, TR-013).
///
/// Separated from <see cref="IAuditLogCommandService"/> (write path) so that read load
/// is isolated from transactional write performance. Implementations use a dedicated
/// read-optimized <c>AuditLogReadDbContext</c> configured with global <c>NoTracking</c>.
///
/// This interface is distinct from <c>UPACIP.Service.Audit.IAuditLogQueryService</c>
/// which uses cursor-based pagination. This interface uses offset pagination via
/// <see cref="Models.PagedResult{T}"/> to align with the standard Contracts model.
/// </summary>
public interface IAuditLogQueryService : IServiceBase
{
    /// <summary>
    /// Returns a filtered, paginated page of audit log entries ordered by timestamp descending.
    /// </summary>
    /// <param name="filter">Multi-field filter with pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Models.PagedResult<Models.AuditLogReadModel>> QueryAsync(
        Models.AuditLogQueryFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single audit log entry by its primary key.
    /// </summary>
    /// <param name="auditLogId">The <c>LogId</c> of the target entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entry, or <c>null</c> if not found.</returns>
    Task<Models.AuditLogReadModel?> GetByIdAsync(Guid auditLogId, CancellationToken ct = default);

    /// <summary>
    /// Returns all audit log entries for a specific entity, ordered by timestamp descending.
    /// Uses the <c>IX_AuditLogs_Entity</c> composite index for O(log n) performance.
    /// </summary>
    /// <param name="entityType">Entity/resource type (e.g., <c>"Patient"</c>).</param>
    /// <param name="entityId">Primary key of the target entity.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<Models.AuditLogReadModel>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the total count of audit entries in the given UTC date range.
    /// Uses the <c>IX_AuditLogs_Timestamp</c> index for O(log n) range scans.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound.</param>
    /// <param name="toUtc">Inclusive upper bound.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> CountByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
