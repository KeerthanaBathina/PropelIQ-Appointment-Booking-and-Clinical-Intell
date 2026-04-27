using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Audit;

/// <summary>
/// Query parameters for <c>GET /api/audit-logs</c> (US_064 AC-3).
/// All filters are optional — omitting a filter means "all values".
/// Cursor-based pagination via <see cref="Cursor"/> provides O(1) seek performance
/// regardless of dataset depth (keyset pagination per NFR-013, TR-013).
/// </summary>
public sealed class AuditLogQueryRequest
{
    /// <summary>Filter to entries authored by a specific user. Optional.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Filter to a specific <see cref="AuditAction"/> type. Optional.</summary>
    public AuditAction? ActionType { get; init; }

    /// <summary>Filter to entries targeting a specific entity type (e.g. "Patient"). Optional.</summary>
    public string? EntityType { get; init; }

    /// <summary>Filter to entries targeting a specific entity ID. Optional.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Inclusive UTC lower bound on <c>Timestamp</c>. Optional.</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>Inclusive UTC upper bound on <c>Timestamp</c>. Optional.</summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Keyset cursor: the <c>LogId</c> of the last item from the previous page.
    /// Omit on the first request. Entries are returned where <c>LogId &gt; Cursor</c>
    /// ordered by <c>LogId ASC</c>.
    /// </summary>
    public Guid? Cursor { get; init; }

    /// <summary>
    /// Number of entries to return. Clamped to the configured maximum in
    /// <see cref="AuditSettings.QueryMaxPageSize"/> by the service layer.
    /// Default: 50.
    /// </summary>
    public int PageSize { get; init; } = 50;
}
