using UPACIP.Contracts.MultiTenancy;

namespace UPACIP.Service.MultiTenancy;

/// <summary>
/// Phase 1 tenant provider — returns the single static default tenant GUID (NFR-027).
///
/// All data in the Phase 1 single-tenant deployment belongs to this tenant.
/// The deterministic value <c>00000000-0000-0000-0000-000000000001</c> is easily
/// identifiable in queries, logs, and database rows.
///
/// In Phase 2, register an <c>HttpContextTenantProvider</c> in place of this class.
/// The <see cref="ITenantProvider"/> abstraction ensures zero application-layer changes.
/// </summary>
public sealed class DefaultTenantProvider : ITenantProvider
{
    /// <summary>
    /// Deterministic GUID for the single default tenant.
    /// Used as the EF Core column default value and as the Phase 1 runtime value.
    /// </summary>
    public static readonly Guid DefaultTenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <inheritdoc />
    public Guid GetCurrentTenantId() => DefaultTenantId;
}
