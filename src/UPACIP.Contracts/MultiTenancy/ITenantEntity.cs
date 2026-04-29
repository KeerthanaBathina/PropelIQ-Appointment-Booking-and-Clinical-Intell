namespace UPACIP.Contracts.MultiTenancy;

/// <summary>
/// Marker interface for tenant-scoped domain entities (NFR-027).
/// Entities that implement this interface will receive a <c>TenantId</c> column in the
/// database and are automatically filtered by <see cref="TenantId"/> via the EF Core
/// global query filter configured in <c>ApplicationDbContext</c>.
///
/// System-level entities (AuditLog, UserSession, etc.) intentionally do NOT implement
/// this interface — they are tenant-agnostic by design.
///
/// In Phase 2 (multi-tenant), the <c>TenantId</c> value will be resolved from the
/// incoming request context (JWT claim or request header) instead of the static default.
/// </summary>
public interface ITenantEntity
{
    /// <summary>
    /// Unique identifier for the tenant that owns this record.
    /// Defaults to the Phase 1 single-tenant default GUID
    /// (<c>00000000-0000-0000-0000-000000000001</c>).
    /// </summary>
    Guid TenantId { get; set; }
}
