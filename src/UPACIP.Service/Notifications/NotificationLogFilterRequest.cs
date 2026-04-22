using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Query filter contract for the admin notification-log endpoint (US_037 AC-4, AC-1).
///
/// All fields are optional; omitting a field means the filter is not applied for that dimension.
/// </summary>
/// <param name="Status">
/// Restricts results to notifications with this exact status.
/// Null returns all statuses.
/// </param>
/// <param name="Channel">
/// Restricts results to a specific delivery channel.
/// Null returns all channels.
/// </param>
/// <param name="NotificationType">
/// Restricts results to a specific notification event type.
/// Null returns all types.
/// </param>
/// <param name="StaffReviewRequired">
/// When <c>true</c>, restricts results to notifications flagged for staff review
/// (i.e. permanently-failed records awaiting follow-up — US_037 AC-3).
/// Null returns all records regardless of review state.
/// </param>
/// <param name="ContactValidationRequired">
/// When <c>true</c>, restricts results to notifications where a bounced email triggered
/// a patient-contact-validation flag (US_037 EC-2).
/// Null returns all records.
/// </param>
/// <param name="From">
/// Inclusive UTC lower bound on <c>NotificationLog.CreatedAt</c>.
/// Null means no lower bound.
/// </param>
/// <param name="To">
/// Inclusive UTC upper bound on <c>NotificationLog.CreatedAt</c>.
/// Null means no upper bound.
/// </param>
/// <param name="Page">1-based page index. Defaults to 1.</param>
/// <param name="PageSize">Number of records per page. Capped at 200. Defaults to 50.</param>
public sealed record NotificationLogFilterRequest(
    NotificationStatus? Status                   = null,
    DeliveryChannel?    Channel                  = null,
    NotificationType?   NotificationType         = null,
    bool?               StaffReviewRequired      = null,
    bool?               ContactValidationRequired = null,
    DateTime?           From                     = null,
    DateTime?           To                       = null,
    int                 Page                     = 1,
    int                 PageSize                 = 50);
