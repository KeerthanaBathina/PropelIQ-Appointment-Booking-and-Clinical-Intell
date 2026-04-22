using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Scoped implementation of <see cref="INotificationLogQueryService"/>.
///
/// All queries use <c>AsNoTracking</c> — this service is read-only and must not
/// materialise change-tracked entities.
///
/// Statistics follow EC-1: <see cref="NotificationStatus.OptedOut"/> and
/// <see cref="NotificationStatus.CancelledBeforeSend"/> are excluded from the
/// attempted-delivery denominator so success/failure rates reflect actual send attempts.
/// </summary>
public sealed class NotificationLogQueryService : INotificationLogQueryService
{
    private static readonly NotificationStatus[] NonAttemptedStatuses =
    [
        NotificationStatus.OptedOut,
        NotificationStatus.CancelledBeforeSend,
    ];

    private static readonly NotificationStatus[] FailedStatuses =
    [
        NotificationStatus.Failed,
        NotificationStatus.PermanentlyFailed,
        NotificationStatus.Bounced,
    ];

    private readonly ApplicationDbContext                     _db;
    private readonly ILogger<NotificationLogQueryService>     _logger;

    public NotificationLogQueryService(
        ApplicationDbContext                  db,
        ILogger<NotificationLogQueryService>  logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<NotificationLogPageDto> GetPageAsync(
        NotificationLogFilterRequest filter,
        CancellationToken            ct = default)
    {
        var query = BuildBaseQuery(filter);

        var totalCount = await query.CountAsync(ct);

        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page     = Math.Max(filter.Page, 1);
        var skip     = (page - 1) * pageSize;

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(n => new NotificationLogRowDto(
                n.NotificationId,
                n.AppointmentId,
                n.NotificationType,
                n.DeliveryChannel,
                n.RecipientAddress,
                n.Status,
                n.RetryCount,
                n.SentAt,
                n.FinalAttemptAt,
                n.IsStaffReviewRequired,
                n.IsContactValidationRequired,
                n.CreatedAt,
                n.DeliveryAttempts.Count))
            .ToListAsync(ct);

        return new NotificationLogPageDto(items, totalCount, page, pageSize);
    }

    /// <inheritdoc/>
    public async Task<NotificationLogSummaryDto> GetSummaryAsync(
        NotificationLogFilterRequest filter,
        CancellationToken            ct = default)
    {
        var query = BuildBaseQuery(filter);

        // Materialise counts grouped by status in one round-trip.
        var statusCounts = await query
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countByStatus = statusCounts.ToDictionary(x => x.Status, x => x.Count);

        int Get(NotificationStatus s) =>
            countByStatus.TryGetValue(s, out var c) ? c : 0;

        var totalSent               = Get(NotificationStatus.Sent);
        var totalFailed             = FailedStatuses.Sum(Get);
        var totalPermanentlyFailed  = Get(NotificationStatus.PermanentlyFailed);
        var totalOptedOut           = Get(NotificationStatus.OptedOut);
        var totalCancelledBeforeSend = Get(NotificationStatus.CancelledBeforeSend);
        var totalAttempted          = totalSent + totalFailed; // excludes opted-out / cancelled

        double? successRate = totalAttempted > 0
            ? Math.Round((double)totalSent / totalAttempted * 100, 2)
            : null;

        double? failureRate = totalAttempted > 0
            ? Math.Round((double)totalFailed / totalAttempted * 100, 2)
            : null;

        // Average delivery time: join attempts, filter successful rows with duration recorded.
        double? avgDeliveryMs = await _db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(a =>
                a.Status == NotificationStatus.Sent &&
                a.DurationMs != null &&
                query.Select(n => n.NotificationId).Contains(a.NotificationId))
            .AverageAsync(a => (double?)a.DurationMs, ct);

        var staffReviewPending = await query
            .CountAsync(n => n.IsStaffReviewRequired, ct);

        return new NotificationLogSummaryDto(
            TotalAttempted:           totalAttempted,
            TotalSent:                totalSent,
            TotalFailed:              totalFailed,
            TotalPermanentlyFailed:   totalPermanentlyFailed,
            TotalOptedOut:            totalOptedOut,
            TotalCancelledBeforeSend: totalCancelledBeforeSend,
            SuccessRatePct:           successRate,
            FailureRatePct:           failureRate,
            AverageDeliveryTimeMs:    avgDeliveryMs.HasValue
                ? Math.Round(avgDeliveryMs.Value, 2)
                : null,
            StaffReviewPending:       staffReviewPending);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private IQueryable<UPACIP.DataAccess.Entities.NotificationLog> BuildBaseQuery(
        NotificationLogFilterRequest filter)
    {
        var query = _db.NotificationLogs
            .AsNoTracking()
            .Include(n => n.DeliveryAttempts)
            .AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(n => n.Status == filter.Status.Value);

        if (filter.Channel.HasValue)
            query = query.Where(n => n.DeliveryChannel == filter.Channel.Value);

        if (filter.NotificationType.HasValue)
            query = query.Where(n => n.NotificationType == filter.NotificationType.Value);

        if (filter.StaffReviewRequired.HasValue)
            query = query.Where(n => n.IsStaffReviewRequired == filter.StaffReviewRequired.Value);

        if (filter.ContactValidationRequired.HasValue)
            query = query.Where(n => n.IsContactValidationRequired == filter.ContactValidationRequired.Value);

        if (filter.From.HasValue)
            query = query.Where(n => n.CreatedAt >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(n => n.CreatedAt <= filter.To.Value);

        return query;
    }
}
