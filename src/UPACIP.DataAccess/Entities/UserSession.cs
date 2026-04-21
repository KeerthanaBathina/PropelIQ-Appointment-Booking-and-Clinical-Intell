using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Immutable PostgreSQL audit record for every session lifecycle event.
///
/// <para>
/// Complements the real-time Redis session key (<c>session:{userId}</c>) by providing a
/// permanent, queryable history for HIPAA compliance audit trails (DR-016: 7-year retention).
/// Redis stores the live session state with a 15-minute sliding TTL; this table stores
/// the immutable history of every session that was ever created, extended, or terminated.
/// </para>
///
/// <para>
/// <b>Immutability contract:</b> records in this table are never updated after insertion.
/// <see cref="LogoutAt"/> and <see cref="ExpirationReason"/> are populated only once on
/// session termination (explicit logout, timeout, or admin force).
/// </para>
///
/// <para>
/// <b>PK design:</b> uses <see cref="LogId"/> (not the <see cref="BaseEntity"/> Id convention)
/// to match the <see cref="AuditLog"/> pattern for append-only audit tables.
/// </para>
/// </summary>
public sealed class UserSession
{
    /// <summary>Surrogate UUID primary key for this audit record.</summary>
    public Guid LogId { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="ApplicationUser"/> who owns this session.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Correlates to the Redis session key and the <c>sessionId</c> JWT claim.
    /// Use this to cross-reference the live Redis entry with the historical DB record.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>UTC timestamp when the session was created (login time).</summary>
    public DateTime LoginAt { get; set; }

    /// <summary>
    /// UTC timestamp when the session ended.
    /// <c>null</c> indicates the session is still active or ended via Redis TTL expiry
    /// without an explicit logout call (termination time cannot be determined exactly).
    /// </summary>
    public DateTime? LogoutAt { get; set; }

    /// <summary>
    /// Reason the session was terminated. <c>null</c> until the session ends.
    /// </summary>
    public SessionExpirationReason? ExpirationReason { get; set; }

    /// <summary>Client IPv4 or IPv6 address at login time (max 45 chars for IPv6).</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>HTTP User-Agent string at login time (max 512 chars).</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this audit record was persisted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>User who owns this session.</summary>
    public ApplicationUser User { get; set; } = null!;
}
