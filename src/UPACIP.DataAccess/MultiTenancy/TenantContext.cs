using UPACIP.Contracts.MultiTenancy;

namespace UPACIP.DataAccess.MultiTenancy;

/// <summary>
/// Scoped value-object that holds the resolved tenant ID for the current HTTP request (NFR-027).
///
/// Injected into <see cref="ApplicationDbContext"/> so that global query filters and the
/// <c>SaveChanges</c> interceptor can access the tenant without going through DI on every
/// operation.
///
/// Lifetime: <c>Scoped</c> — one instance per HTTP request, resolved once at construction
/// time by <see cref="ITenantProvider.GetCurrentTenantId"/>.
/// </summary>
public sealed class TenantContext
{
    /// <summary>The tenant identifier resolved for the current request scope.</summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Initialises the context by resolving the tenant ID from the provided <see cref="ITenantProvider"/>.
    /// </summary>
    public TenantContext(ITenantProvider provider)
    {
        TenantId = provider.GetCurrentTenantId();
    }
}
