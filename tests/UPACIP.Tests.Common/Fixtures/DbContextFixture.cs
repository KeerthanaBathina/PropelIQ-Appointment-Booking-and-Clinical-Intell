using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UPACIP.Contracts.MultiTenancy;
using UPACIP.DataAccess;
using UPACIP.DataAccess.MultiTenancy;

namespace UPACIP.Tests.Common.Fixtures;

/// <summary>
/// Factory for creating isolated <see cref="ApplicationDbContext"/> instances backed by
/// the EF Core in-memory provider (US_097, AC-1).
///
/// Each call with no <paramref name="dbName"/> gets a unique database name via
/// <see cref="Guid.NewGuid"/>, ensuring test isolation by default.
/// Pass a shared <paramref name="dbName"/> when testing multi-step workflows that
/// require state to persist across multiple service calls within one test.
///
/// Important: The <see cref="ApplicationDbContext"/> has entity configurations using
/// Npgsql JSONB column types (<c>HasColumnType("jsonb")</c>) that are relational-only.
/// When tests need to write/read via EF, use the <see cref="CreateForAuditLog"/> overload
/// which creates a minimal context targeting only the <c>AuditLogs</c> table,
/// bypassing the unsupported JSONB model configuration.
/// </summary>
public static class DbContextFixture
{
    /// <summary>
    /// Creates a minimal <see cref="ApplicationDbContext"/> for AuditLog tests, backed
    /// by an in-memory database. Relational entity configurations are not applied so
    /// that provider-specific types (JSONB) do not cause model-validation failures.
    /// </summary>
    /// <param name="dbName">
    /// Optional database name. Defaults to a new <see cref="Guid"/> to ensure isolation.
    /// </param>
    public static ApplicationDbContext CreateForAuditLog(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w =>
            {
                w.Ignore(InMemoryEventId.TransactionIgnoredWarning);
                w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning);
            })
            .EnableServiceProviderCaching(false)
            .Options;

        return new ApplicationDbContext(options, CreateDefaultTenantContext());
    }

    // Creates a TenantContext using the Phase 1 default tenant for test isolation.
    private static TenantContext CreateDefaultTenantContext()
        => new TenantContext(new TestTenantProvider());

    private sealed class TestTenantProvider : ITenantProvider
    {
        public Guid GetCurrentTenantId() =>
            new Guid("00000000-0000-0000-0000-000000000001");
    }

    /// <summary>
    /// Creates a new <see cref="ApplicationDbContext"/> backed by an in-memory database.
    /// Only use this when the tested code does not access JSONB-mapped entities
    /// (e.g. <c>ExtractedData</c>). For audit log tests prefer <see cref="CreateForAuditLog"/>.
    /// </summary>
    public static ApplicationDbContext CreateInMemory(string? dbName = null)
        => CreateForAuditLog(dbName);
}
