namespace UPACIP.Contracts.Models;

/// <summary>
/// Read-side projection DTO for audit log query results (US_096, AC-3, TR-013).
///
/// Flattened and denormalized for query performance — includes <see cref="UserName"/>
/// from a joined user record to avoid additional round-trips in compliance dashboards.
/// This read model is distinct from the write entity; it evolves independently.
/// </summary>
public sealed class AuditLogReadModel
{
    /// <summary>Surrogate UUID primary key (maps to <c>AuditLog.LogId</c>).</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp when the audit entry was recorded.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>Id of the user who performed the action. Null for system events.</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Display name of the user (denormalized from <c>ApplicationUser.FullName</c>).
    /// Null when queried via the read-only <c>AuditLogReadDbContext</c> without user join,
    /// or when the user account has been deleted.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>Action performed (string representation of <c>AuditAction</c> enum).</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Entity/resource type (maps to <c>AuditLog.ResourceType</c>).</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Primary key of the affected record (maps to <c>AuditLog.ResourceId</c>).</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Client IP address at the time of the action.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Correlation ID linking this entry to an HTTP request chain.</summary>
    public string? CorrelationId { get; init; }
}
