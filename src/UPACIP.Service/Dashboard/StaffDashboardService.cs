using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Dashboard;

/// <summary>
/// Aggregates stats, today's appointment schedule, and pending work items for the
/// Staff Dashboard (SCR-010). Results are Redis-cached per user per calendar day
/// with a 5-second TTL so the polling frontend (5 s interval) gets fresh data
/// without hammering the database on every request (US_057 AC-4, NFR-030).
///
/// All queries use AsNoTracking — this service is read-only.
/// </summary>
public sealed class StaffDashboardService : IStaffDashboardService
{
    private readonly ApplicationDbContext          _db;
    private readonly ICacheService                 _cache;
    private readonly ILogger<StaffDashboardService> _logger;

    public StaffDashboardService(
        ApplicationDbContext           db,
        ICacheService                  cache,
        ILogger<StaffDashboardService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<StaffDashboardResponse> GetDashboardAsync(
        string            userId,
        CancellationToken ct = default)
    {
        var cacheKey = $"staff:dashboard:{userId}:{DateTime.UtcNow:yyyyMMdd}";

        var cached = await _cache.GetAsync<StaffDashboardResponse>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("StaffDashboard cache hit for user {UserId}.", userId);
            return cached;
        }

        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var startUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc   = today.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // ── Stats ───────────────────────────────────────────────────────────

        var todayAppointments = await _db.Appointments
            .AsNoTracking()
            .CountAsync(a =>
                a.AppointmentTime >= startUtc &&
                a.AppointmentTime <= endUtc   &&
                a.Status != AppointmentStatus.Cancelled,
                ct);

        var inQueue = await _db.QueueEntries
            .AsNoTracking()
            .CountAsync(q =>
                q.Appointment.AppointmentTime >= startUtc &&
                q.Appointment.AppointmentTime <= endUtc   &&
                (q.Status == QueueStatus.Waiting || q.Status == QueueStatus.InVisit),
                ct);

        var completedToday = await _db.Appointments
            .AsNoTracking()
            .CountAsync(a =>
                a.AppointmentTime >= startUtc &&
                a.AppointmentTime <= endUtc   &&
                a.Status == AppointmentStatus.Completed,
                ct);

        var pendingCodes = await _db.MedicalCodes
            .AsNoTracking()
            .CountAsync(m => m.SuggestedByAi && m.ApprovedByUserId == null, ct);

        var pendingConflicts = await _db.ClinicalConflicts
            .AsNoTracking()
            .CountAsync(c =>
                c.Status == ConflictStatus.Detected ||
                c.Status == ConflictStatus.UnderReview,
                ct);

        var pendingDocReviews = await _db.ExtractedData
            .AsNoTracking()
            .CountAsync(e => e.FlaggedForReview && e.VerifiedByUserId == null, ct);

        var pendingReviews = pendingCodes + pendingConflicts + pendingDocReviews;

        var stats = new DashboardStatsDto(todayAppointments, inQueue, pendingReviews, completedToday);

        // ── Schedule ────────────────────────────────────────────────────────

        var schedule = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Where(a =>
                a.AppointmentTime >= startUtc &&
                a.AppointmentTime <= endUtc   &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Patient.DeletedAt == null)
            .OrderBy(a => a.AppointmentTime)
            .Select(a => new DashboardScheduleItemDto(
                a.Id,
                a.PatientId,
                a.Patient.FullName,
                a.AppointmentTime,
                a.AppointmentType,
                a.Status.ToString(),
                a.NoShowRiskScore,
                a.IsRiskEstimated))
            .ToListAsync(ct);

        // ── Pending Tasks ────────────────────────────────────────────────────
        // Up to 20 items per category, ordered by urgency / creation time.

        var codeTasks = await _db.MedicalCodes
            .AsNoTracking()
            .Include(m => m.Patient)
            .Where(m =>
                m.SuggestedByAi         &&
                m.ApprovedByUserId == null &&
                m.Patient.DeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(20)
            .Select(m => new DashboardPendingTaskDto(
                m.Id,
                "CodeApproval",
                m.PatientId,
                m.Patient.FullName,
                $"{m.CodeType}: {m.CodeValue} — {m.Description}",
                "SCR-014"))
            .ToListAsync(ct);

        var conflictTasks = await _db.ClinicalConflicts
            .AsNoTracking()
            .Include(c => c.Patient)
            .Where(c =>
                (c.Status == ConflictStatus.Detected ||
                 c.Status == ConflictStatus.UnderReview) &&
                c.Patient.DeletedAt == null)
            .OrderByDescending(c => c.IsUrgent)
            .ThenBy(c => c.CreatedAt)
            .Take(20)
            .Select(c => new DashboardPendingTaskDto(
                c.Id,
                "ConflictResolution",
                c.PatientId,
                c.Patient.FullName,
                $"Conflict: {c.ConflictType} ({c.Severity})",
                "SCR-013"))
            .ToListAsync(ct);

        var docTasks = await _db.ExtractedData
            .AsNoTracking()
            .Include(e => e.Document)
            .ThenInclude(d => d.Patient)
            .Where(e =>
                e.FlaggedForReview           &&
                e.VerifiedByUserId == null   &&
                e.Document.Patient.DeletedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(20)
            .Select(e => new DashboardPendingTaskDto(
                e.Id,
                "DocumentReview",
                e.Document.PatientId,
                e.Document.Patient.FullName,
                $"Document: {e.Document.OriginalFileName}",
                "SCR-012"))
            .ToListAsync(ct);

        var pendingTasks = new List<DashboardPendingTaskDto>(
            codeTasks.Count + conflictTasks.Count + docTasks.Count);
        pendingTasks.AddRange(codeTasks);
        pendingTasks.AddRange(conflictTasks);
        pendingTasks.AddRange(docTasks);

        var response = new StaffDashboardResponse(stats, schedule, pendingTasks);

        await _cache.SetAsync(cacheKey, response, TimeSpan.FromSeconds(5), ct);

        _logger.LogInformation(
            "StaffDashboard built for user {UserId}: " +
            "appointments={Today}, inQueue={InQueue}, pendingTasks={Tasks}.",
            userId, todayAppointments, inQueue, pendingTasks.Count);

        return response;
    }
}
