namespace UPACIP.Contracts.Models;

/// <summary>
/// Multi-field filter DTO for audit log compliance queries (US_096, AC-3, TR-013).
///
/// All fields are optional — omitted fields produce no WHERE clause predicate.
/// Multiple specified fields are combined with AND logic.
/// Default page size is 50; callers should cap at a reasonable maximum via service config.
/// </summary>
public sealed class AuditLogQueryFilter
{
    /// <summary>Inclusive UTC lower bound on entry timestamp.</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>Inclusive UTC upper bound on entry timestamp.</summary>
    public DateTime? ToUtc { get; init; }

    /// <summary>Filter to entries authored by this user ID.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Filter to a specific entity/resource type (e.g., <c>"Patient"</c>).</summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// Filter to a specific action string (e.g., <c>"DataModify"</c>, <c>"Login"</c>).
    /// Must match an <c>AuditAction</c> enum name exactly (case-insensitive).
    /// </summary>
    public string? Action { get; init; }

    /// <summary>Filter to a specific entity/resource ID.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Filter to entries with this correlation ID.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Number of entries per page. Defaults to 50.</summary>
    public int PageSize { get; init; } = 50;
}
