using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// State-transition outage detector that creates and resolves <see cref="OutageRecord"/> rows
/// and emits Serilog structured alerts (US_083 task_001, AC-3, NFR-019).
///
/// <para>
/// <b>Registered as Singleton</b> — the in-memory <c>_previousStatuses</c> dictionary must
/// survive across probe cycles.  <see cref="IServiceScopeFactory"/> is injected to create
/// short-lived DI scopes when persisting outage records to PostgreSQL, avoiding the
/// "cannot consume Scoped service from Singleton" issue.
/// </para>
///
/// <para>
/// Impact classification: <c>database</c> → Critical, <c>redis</c> → Major, others → Minor.
/// When multiple dependencies are affected simultaneously the highest severity wins.
/// </para>
///
/// <para>
/// Alert latency: the probe runs every 30 seconds, so the worst-case detection lag is 30 s,
/// well within the 1-minute requirement of AC-3.
/// </para>
/// </summary>
public sealed class OutageAlertService : IOutageAlertService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutageAlertService> _logger;

    // In-memory state: dependency name → first-detected-unhealthy timestamp.
    // Protected by a dedicated lock object because EvaluateHealthTransitionAsync may be
    // called concurrently if the probe interval is shorter than the DB write duration.
    private readonly Dictionary<string, DateTime> _unhealthy = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    // Track which outage records are open per dependency set (keyed by dependency name).
    private readonly Dictionary<string, Guid> _openOutageIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _previousStatuses = new(StringComparer.OrdinalIgnoreCase);

    public OutageAlertService(IServiceScopeFactory scopeFactory, ILogger<OutageAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc />
    public async Task EvaluateHealthTransitionAsync(
        Dictionary<string, string> currentStatuses,
        bool isMaintenance = false,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        List<string> newlyUnhealthy;
        List<string> newlyRecovered;

        lock (_stateLock)
        {
            // Healthy → Unhealthy: present in current as non-Healthy AND not already tracked.
            newlyUnhealthy = currentStatuses
                .Where(kv => !string.Equals(kv.Value, "Healthy", StringComparison.OrdinalIgnoreCase)
                             && !_unhealthy.ContainsKey(kv.Key))
                .Select(kv => kv.Key)
                .ToList();

            // Unhealthy → Healthy: was tracked as unhealthy AND now reports Healthy.
            newlyRecovered = _unhealthy.Keys
                .Where(dep => currentStatuses.TryGetValue(dep, out var st)
                              && string.Equals(st, "Healthy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Update in-memory state.
            foreach (var dep in newlyUnhealthy)
                _unhealthy[dep] = now;

            foreach (var dep in newlyRecovered)
                _unhealthy.Remove(dep);

            _previousStatuses = new Dictionary<string, string>(currentStatuses, StringComparer.OrdinalIgnoreCase);
        }

        // --- Handle newly-unhealthy dependencies ---
        if (newlyUnhealthy.Count > 0)
        {
            var affectedServices = string.Join(",", newlyUnhealthy);
            var impactLevel      = ClassifyImpact(newlyUnhealthy);

            var outageRecord = new OutageRecord
            {
                Id               = Guid.NewGuid(),
                StartedAt        = now,
                AffectedServices = affectedServices,
                ImpactLevel      = impactLevel,
            };

            if (!isMaintenance)
            {
                outageRecord.AlertSentAt = now;
                _logger.LogWarning(
                    "OUTAGE_DETECTED: Services={AffectedServices}, StartedAt={StartedAt}, Impact={ImpactLevel}",
                    affectedServices, now, impactLevel);
            }

            await PersistOutageRecordAsync(outageRecord, ct);

            lock (_stateLock)
            {
                foreach (var dep in newlyUnhealthy)
                    _openOutageIds[dep] = outageRecord.Id;
            }
        }

        // --- Handle newly-recovered dependencies ---
        if (newlyRecovered.Count > 0)
        {
            foreach (var dep in newlyRecovered)
            {
                Guid outageId;
                lock (_stateLock)
                {
                    if (!_openOutageIds.TryGetValue(dep, out outageId))
                        continue;
                    _openOutageIds.Remove(dep);
                }

                await ResolveOutageRecordAsync(outageId, now, ct);

                var duration = now - (await GetOutageStartAsync(outageId, ct) ?? now);
                _logger.LogInformation(
                    "OUTAGE_RESOLVED: Services={AffectedService}, Duration={Duration}",
                    dep, duration);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutageRecord>> GetActiveOutagesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutageRecords
            .Where(o => o.ResolvedAt == null)
            .OrderBy(o => o.StartedAt)
            .ToListAsync(ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task PersistOutageRecordAsync(OutageRecord record, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OutageRecords.Add(record);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist OutageRecord for services={AffectedServices}",
                record.AffectedServices);
        }
    }

    private async Task ResolveOutageRecordAsync(Guid outageId, DateTime resolvedAt, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.OutageRecords.FindAsync(new object[] { outageId }, ct);
            if (record is null) return;
            record.ResolvedAt = resolvedAt;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve OutageRecord {OutageId}", outageId);
        }
    }

    private async Task<DateTime?> GetOutageStartAsync(Guid outageId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.OutageRecords.FindAsync(new object[] { outageId }, ct))?.StartedAt;
    }

    private static string ClassifyImpact(IEnumerable<string> affectedDependencies)
    {
        // Highest severity wins.
        var hasDatabase = false;
        var hasRedis    = false;

        foreach (var dep in affectedDependencies)
        {
            if (dep.Equals("database", StringComparison.OrdinalIgnoreCase)) hasDatabase = true;
            if (dep.Equals("redis",    StringComparison.OrdinalIgnoreCase)) hasRedis    = true;
        }

        if (hasDatabase) return "Critical";
        if (hasRedis)    return "Major";
        return "Minor";
    }
}
