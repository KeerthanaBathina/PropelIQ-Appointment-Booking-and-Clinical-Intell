using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Pgvector;
using UPACIP.DataAccess.MultiTenancy;

namespace UPACIP.DataAccess;

/// <summary>
/// Design-time factory used by EF Core CLI tools (dotnet-ef migrations add / database update).
///
/// At design time the normal DI/Host pipeline is not running, so the <c>NpgsqlDataSource</c>
/// with <c>UseVector()</c> configured in <c>Program.cs</c> is not available.  This factory
/// recreates the minimum required context so that migration scaffolding succeeds.
///
/// Connection string resolution order:
///   1. <c>UPACIP_DESIGN_CONNECTION</c> environment variable (CI / staging).
///   2. Hard-coded development default (localhost, <c>upacip_app</c> role).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("UPACIP_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=upacip;Username=upacip_app;Password=upacip_dev_password";

        // Must call UseVector() so that EF Core can resolve the Vector property type
        // mapping for embedding entities (even though those tables are excluded from migrations).
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            dataSource,
            npgsql => npgsql.SetPostgresVersion(new Version(16, 0)));

        // At design time, use the Phase 1 default tenant provider so migrations scaffold
        // correctly without a running HTTP context.
        var tenantContext = new TenantContext(new DesignTimeTenantProvider());
        return new ApplicationDbContext(optionsBuilder.Options, tenantContext);
    }

    // Minimal Phase 1 tenant provider for design-time scaffold — mirrors DefaultTenantProvider.
    private sealed class DesignTimeTenantProvider : UPACIP.Contracts.MultiTenancy.ITenantProvider
    {
        public Guid GetCurrentTenantId() =>
            new Guid("00000000-0000-0000-0000-000000000001");
    }
}
