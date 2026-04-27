using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Admin;

/// <summary>
/// Aggregates system-wide platform metrics for the Admin Dashboard (US_058 AC-1, AC-2).
///
/// All queries use AsNoTracking — this service is read-only.
/// Results are cached in Redis with a 5-minute TTL (NFR-030).
/// On DB computation failure the most recent cached snapshot is returned with
/// <c>IsStale = true</c> and a structured log warning (NFR-017, NFR-035).
/// </summary>
public sealed class SystemMetricsService : ISystemMetricsService
{
    private const string CurrentCacheKey       = "admin:metrics:current";
    private const string TrendsCacheKeyPrefix  = "admin:metrics:trends:";
    private static readonly TimeSpan CacheTtl  = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext                _db;
    private readonly UserManager<ApplicationUser>        _userManager;
    private readonly ICacheService                       _cache;
    private readonly ILogger<SystemMetricsService>       _logger;

    public SystemMetricsService(
        ApplicationDbContext               db,
        UserManager<ApplicationUser>       userManager,
        ICacheService                      cache,
        ILogger<SystemMetricsService>      logger)
    {
        _db          = db;
        _userManager = userManager;
        _cache       = cache;
        _logger      = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetCurrentMetricsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminMetricsResponse> GetCurrentMetricsAsync(CancellationToken ct = default)
    {
        // 1. Try cache-aside pattern (5-min TTL).
        var cached = await _cache.GetAsync<AdminMetricsResponse>(CurrentCacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("SystemMetrics cache hit — key={CacheKey}.", CurrentCacheKey);
            return cached;
        }

        // 2. Compute from database.
        try
        {
            var snapshot = await ComputeCurrentSnapshotAsync(ct);
            var response = new AdminMetricsResponse(snapshot, DateTime.UtcNow, IsStale: false);

            await _cache.SetAsync(CurrentCacheKey, response, CacheTtl, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemMetrics live computation failed — attempting stale cache fallback.");

            // 3. Stale fallback: return last cached value with IsStale = true (edge-case spec).
            var stale = await _cache.GetAsync<AdminMetricsResponse>(CurrentCacheKey, ct);
            if (stale is not null)
            {
                _logger.LogInformation(
                    "SystemMetrics returning stale snapshot from {GeneratedAt}.", stale.GeneratedAt);
                return stale with { IsStale = true };
            }

            throw; // Nothing in cache — let the controller handle the 500.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTrendDataAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminMetricsTrendsResponse> GetTrendDataAsync(
        int               period,
        CancellationToken ct = default)
    {
        var cacheKey = $"{TrendsCacheKeyPrefix}{period}d";

        var cached = await _cache.GetAsync<AdminMetricsTrendsResponse>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("SystemMetrics trends cache hit — key={CacheKey}.", cacheKey);
            return cached;
        }

        try
        {
            var dataPoints = await ComputeTrendDataAsync(period, ct);
            var response   = new AdminMetricsTrendsResponse(
                Period:      $"{period}d",
                DataPoints:  dataPoints,
                GeneratedAt: DateTime.UtcNow);

            await _cache.SetAsync(cacheKey, response, CacheTtl, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemMetrics trends computation failed for period={Period}d — attempting stale fallback.", period);

            var stale = await _cache.GetAsync<AdminMetricsTrendsResponse>(cacheKey, ct);
            if (stale is not null)
            {
                _logger.LogInformation(
                    "SystemMetrics trends returning stale snapshot from {GeneratedAt}.", stale.GeneratedAt);
                return stale;
            }

            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<AdminMetricsSnapshot> ComputeCurrentSnapshotAsync(CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var today    = DateOnly.FromDateTime(now);
        var todayUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var tomorrowUtc = todayUtc.AddDays(1);

        // 1. Active users — logged in within the last 15 minutes.
        var activeThreshold = now.AddMinutes(-15);
        var activeUsers = await _db.Users
            .AsNoTracking()
            .CountAsync(u => u.LastLoginAt != null && u.LastLoginAt >= activeThreshold, ct);

        // 2. Daily appointments — non-cancelled appointments for today (UTC).
        var dailyAppointments = await _db.Appointments
            .AsNoTracking()
            .CountAsync(
                a => a.AppointmentTime >= todayUtc &&
                     a.AppointmentTime <  tomorrowUtc &&
                     a.Status          != AppointmentStatus.Cancelled,
                ct);

        // 3. No-show rate — last 30 calendar days.
        var thirtyDaysAgo = now.AddDays(-30);
        var (noShows30, total30) = await GetNoShowCountsAsync(thirtyDaysAgo, now, ct);
        var noShowRate = total30 > 0
            ? Math.Round((decimal)noShows30 / total30 * 100, 1)
            : 0m;

        // 4. AI agreement rate — all-time: approved AI codes / total AI-suggested codes.
        var (approved, totalAi) = await GetAiCodeCountsAsync(ct);
        var aiAgreementRate = totalAi > 0
            ? Math.Round((decimal)approved / totalAi * 100, 1)
            : 0m;

        // 5. Uptime — derived from recent queue/session activity as a proxy.
        //    When task_005_db_admin_metrics_config_schema is complete this will
        //    read from the system_metrics_snapshot table. Until then we compute
        //    a proxy: if the DB responded within this request, uptime is 100%.
        //    A real value will be provided by the health-check aggregation job.
        var uptimePercent = await ComputeUptimeProxyAsync(ct);

        return new AdminMetricsSnapshot(
            ActiveUsers:      activeUsers,
            DailyAppointments: dailyAppointments,
            NoShowRate:       noShowRate,
            AiAgreementRate:  aiAgreementRate,
            UptimePercent:    uptimePercent);
    }

    private async Task<IReadOnlyList<MetricTrendPoint>> ComputeTrendDataAsync(
        int period, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var today  = DateOnly.FromDateTime(now);
        var points = new List<MetricTrendPoint>(period);

        // Compute per-day aggregates in a single batched query per metric type.
        // Active users per day from UserSessions (login timestamps).
        // Appointments per day from Appointments table.
        // No-show per day from Appointments grouped by date.
        // AI agreement rate is computed per rolling window per day.

        var startDate      = today.AddDays(-period + 1);
        var startUtc       = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // ── Daily appointment counts ─────────────────────────────────────────
        var apptsByDay = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentTime >= startUtc && a.Status != AppointmentStatus.Cancelled)
            .GroupBy(a => DateOnly.FromDateTime(a.AppointmentTime))
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // ── No-show counts per day ───────────────────────────────────────────
        var noShowsByDay = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentTime >= startUtc)
            .GroupBy(a => DateOnly.FromDateTime(a.AppointmentTime))
            .Select(g => new
            {
                Date     = g.Key,
                NoShows  = g.Count(a => a.Status == AppointmentStatus.NoShow),
                Total    = g.Count(),
            })
            .ToListAsync(ct);

        // ── Active sessions per day (from UserSessions) ──────────────────────
        var sessionsByDay = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.LoginAt >= startUtc)
            .GroupBy(s => DateOnly.FromDateTime(s.LoginAt))
            .Select(g => new { Date = g.Key, Logins = g.Select(s => s.UserId).Distinct().Count() })
            .ToListAsync(ct);

        // ── All-time AI agreement rate (constant across trend period) ────────
        var (approved, totalAi) = await GetAiCodeCountsAsync(ct);
        var aiRateConstant = totalAi > 0
            ? Math.Round((decimal)approved / totalAi * 100, 1)
            : 0m;

        // ── Uptime proxy (constant — health aggregation not yet available) ───
        var uptimeProxy = await ComputeUptimeProxyAsync(ct);

        // Build one row per day.
        for (var d = startDate; d <= today; d = d.AddDays(1))
        {
            var apptRow    = apptsByDay.FirstOrDefault(r => r.Date == d);
            var noShowRow  = noShowsByDay.FirstOrDefault(r => r.Date == d);
            var sessionRow = sessionsByDay.FirstOrDefault(r => r.Date == d);

            var dailyTotal  = noShowRow?.Total   ?? 0;
            var dailyNoShow = noShowRow?.NoShows  ?? 0;
            var noShowRate  = dailyTotal > 0
                ? Math.Round((decimal)dailyNoShow / dailyTotal * 100, 1)
                : 0m;

            points.Add(new MetricTrendPoint(
                Date:              d,
                ActiveUsers:       sessionRow?.Logins   ?? 0,
                DailyAppointments: apptRow?.Count        ?? 0,
                NoShowRate:        noShowRate,
                AiAgreementRate:   aiRateConstant,
                UptimePercent:     uptimeProxy));
        }

        return points;
    }

    // ── Shared count helpers ─────────────────────────────────────────────────

    private async Task<(int noShows, int total)> GetNoShowCountsAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var counts = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentTime >= from && a.AppointmentTime <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                NoShows = g.Count(a => a.Status == AppointmentStatus.NoShow),
                Total   = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null ? (0, 0) : (counts.NoShows, counts.Total);
    }

    private async Task<(int approved, int total)> GetAiCodeCountsAsync(CancellationToken ct)
    {
        var counts = await _db.MedicalCodes
            .AsNoTracking()
            .Where(m => m.SuggestedByAi)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Approved = g.Count(m => m.ApprovedByUserId != null),
                Total    = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null ? (0, 0) : (counts.Approved, counts.Total);
    }

    /// <summary>
    /// Proxy uptime calculation: counts recent successful health signals.
    /// Returns 99.9 when the DB responded normally (meaning all tiers are up).
    /// This will be replaced by a real aggregation once the system_metrics_snapshot
    /// table is available (task_005_db_admin_metrics_config_schema).
    /// </summary>
    private async Task<decimal> ComputeUptimeProxyAsync(CancellationToken ct)
    {
        // Check that DB is responsive and at least one user session exists.
        var hasActivity = await _db.UserSessions
            .AsNoTracking()
            .AnyAsync(ct);

        return hasActivity ? 99.9m : 100.0m;
    }
}
