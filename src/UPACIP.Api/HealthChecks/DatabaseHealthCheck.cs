using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using UPACIP.DataAccess;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// PostgreSQL dependency health check (US_099, AC-1, AC-3).
///
/// Executes two probes:
/// <list type="number">
///   <item><see cref="DatabaseFacade.CanConnectAsync"/> — validates that the connection pool
///         can open a connection to the server.</item>
///   <item><c>SELECT 1</c> — validates that the server can process queries (read capability).
///         </item>
/// </list>
///
/// Database is a <b>critical</b> dependency — any failure returns
/// <see cref="HealthCheckResult.Unhealthy"/> so the overall health status is also
/// <c>Unhealthy</c> and load balancers remove the instance from rotation.
///
/// A 400 ms per-check timeout is enforced by the <see cref="HealthCheckConfiguration"/>
/// registration so a slow database never blocks the /health response beyond 500 ms
/// (edge case 1).
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ApplicationDbContext dbContext, ILogger<DatabaseHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger    = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy(
                    "PostgreSQL connection failed — pool exhausted or server unreachable",
                    data: new Dictionary<string, object> { ["server"] = "PostgreSQL 16" });
            }

            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return HealthCheckResult.Healthy(
                "PostgreSQL connection and query successful",
                data: new Dictionary<string, object> { ["server"] = "PostgreSQL 16" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HEALTH_CHECK_FAILED: Database health check exception");
            return HealthCheckResult.Unhealthy(
                "PostgreSQL health check failed",
                exception: ex,
                data: new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }
}
