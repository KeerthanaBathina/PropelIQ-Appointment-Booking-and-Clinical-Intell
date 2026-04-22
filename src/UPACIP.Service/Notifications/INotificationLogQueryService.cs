namespace UPACIP.Service.Notifications;

/// <summary>
/// Encapsulates admin-facing notification-log queries and aggregate reporting (US_037 AC-4, AC-1).
///
/// Both methods accept the same <see cref="NotificationLogFilterRequest"/> so the controller
/// can call them independently (list-only, stats-only, or both) without duplicating filter logic.
/// </summary>
public interface INotificationLogQueryService
{
    /// <summary>
    /// Returns a paginated page of notification-log rows matching the supplied filter.
    /// </summary>
    Task<NotificationLogPageDto> GetPageAsync(
        NotificationLogFilterRequest filter,
        CancellationToken            ct = default);

    /// <summary>
    /// Computes aggregate delivery statistics for all rows matching the supplied filter.
    ///
    /// <see cref="UPACIP.DataAccess.Enums.NotificationStatus.OptedOut"/> and
    /// <see cref="UPACIP.DataAccess.Enums.NotificationStatus.CancelledBeforeSend"/> are
    /// excluded from success/failure rate denominators (EC-1).
    /// </summary>
    Task<NotificationLogSummaryDto> GetSummaryAsync(
        NotificationLogFilterRequest filter,
        CancellationToken            ct = default);
}
