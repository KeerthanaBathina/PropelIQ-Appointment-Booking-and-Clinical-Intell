using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Rolling 30-day uptime tracker backed by PostgreSQL time-series snapshots (US_083 task_001, AC-1).
///
/// <para>
/// Each call to <see cref="RecordSnapshotAsync"/> appends a new <see cref="UptimeSnapshot"/> row.
/// <see cref="GetUptimePercentageAsync"/> queries the rolling window and returns the fraction of
/// non-maintenance snapshots that were healthy.  Maintenance-window rows are excluded from both
/// numerator and denominator so planned downtime does not count against the 99.9% SLA target.
/// </para>
///
/// <para>
/// <b>Registered as Scoped</b> — one instance per DI scope; the background service creates a
/// fresh scope for each probe cycle.
/// </para>
/// </summary>
public sealed class UptimeTracker : IUptimeTracker
{
    private readonly ApplicationDbContext _db;

    public UptimeTracker(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task RecordSnapshotAsync(
        bool isHealthy,
        Dictionary<string, string> dependencyStatuses,
        bool isMaintenanceWindow = false,
        CancellationToken ct = default)
    {
        var snapshot = new UptimeSnapshot
        {
            Id                   = Guid.NewGuid(),
            Timestamp            = DateTime.UtcNow,
            IsHealthy            = isHealthy,
            DependencyStatusesJson = JsonSerializer.Serialize(dependencyStatuses),
            IsMaintenanceWindow  = isMaintenanceWindow,
        };

        _db.UptimeSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<double> GetUptimePercentageAsync(int windowDays = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-windowDays);

        // Only count non-maintenance snapshots (maintenance is excluded from SLA computation).
        var query = _db.UptimeSnapshots
            .Where(s => s.Timestamp >= cutoff && !s.IsMaintenanceWindow);

        var totalCount   = await query.CountAsync(ct);
        if (totalCount == 0)
            return 100.0;

        var healthyCount = await query.CountAsync(s => s.IsHealthy, ct);

        return (double)healthyCount / totalCount * 100.0;
    }

    /// <inheritdoc />
    public async Task<UptimeSnapshot?> GetCurrentStatusAsync(CancellationToken ct = default) =>
        await _db.UptimeSnapshots
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task PruneOldSnapshotsAsync(int retentionDays = 90, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        await _db.UptimeSnapshots
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
