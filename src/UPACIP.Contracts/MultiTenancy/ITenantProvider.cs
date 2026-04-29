namespace UPACIP.Contracts.MultiTenancy;

/// <summary>
/// Resolves the current tenant identifier for the active request scope (NFR-027).
///
/// In Phase 1 (single-tenant), the only implementation is <c>DefaultTenantProvider</c>
/// in <c>UPACIP.Service</c>, which returns a deterministic default GUID.
///
/// In Phase 2 (multi-tenant), register an <c>HttpContextTenantProvider</c> that reads
/// the tenant ID from a JWT claim or request header.
/// All consuming code remains unchanged because it depends on this interface.
/// </summary>
public interface ITenantProvider
{
    /// <summary>Returns the tenant identifier for the current request context.</summary>
    Guid GetCurrentTenantId();
}
