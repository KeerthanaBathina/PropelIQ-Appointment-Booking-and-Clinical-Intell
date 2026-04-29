namespace UPACIP.Contracts.Services;

/// <summary>
/// CQRS write-side interface for audit log entry creation (US_096, AC-3, DR-016).
///
/// Intentionally exposes NO update or delete operations — audit entries are immutable
/// per HIPAA Security Rule §164.312(b) and internal policy DR-016.
///
/// Implementations must persist entries through the standard Service → DataAccess pipeline
/// using <c>ApplicationDbContext</c> (the transactional write context).
/// </summary>
public interface IAuditLogCommandService : IServiceBase
{
    /// <summary>
    /// Appends a single immutable audit log entry.
    /// </summary>
    /// <param name="entry">The command model describing the event to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated <c>LogId</c> of the persisted entry.</returns>
    Task<Guid> AppendAsync(Models.AuditLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Appends multiple audit log entries in a single database round-trip.
    /// Used for bulk operations (e.g., import processes, batch data changes).
    /// All entries are persisted in a single <c>SaveChangesAsync</c> call.
    /// </summary>
    /// <param name="entries">The audit entries to persist. Must not be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AppendBatchAsync(IReadOnlyList<Models.AuditLogEntry> entries, CancellationToken ct = default);
}
