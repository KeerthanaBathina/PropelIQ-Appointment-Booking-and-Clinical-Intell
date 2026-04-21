using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Immutable audit trail record. AuditLog does NOT extend <see cref="BaseEntity"/> because:
/// — it uses its own primary key name (<c>LogId</c>), and
/// — audit entries are never updated, so no <c>UpdatedAt</c> field is needed.
/// All writes should be append-only; updates and deletes on this table must be forbidden
/// at the database level (row-security policy or trigger).
/// </summary>
public sealed class AuditLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid LogId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> who performed the action.
    /// Nullable so that audit records are preserved when the user account is deleted
    /// (ON DELETE SET NULL) and for potential future system-generated events (DR-016).
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Type of action that was performed.</summary>
    public AuditAction Action { get; set; }

    /// <summary>Entity type targeted by the action (e.g. "Patient", "Appointment").</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Primary key of the affected record. Null for non-entity actions (e.g. Login).</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>UTC timestamp when the action was recorded.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Client IP address at the time of the action (IPv4 or IPv6).</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>HTTP User-Agent header value for the request that triggered the action.</summary>
    public string UserAgent { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ApplicationUser? User { get; set; }
}
