namespace UPACIP.Service.Notifications;

/// <summary>
/// Delivers patient-facing notifications after a dynamic slot swap is confirmed (US_036 AC-2).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Send an email listing the old and new appointment times via the existing
///     <see cref="UPACIP.Service.Auth.IEmailService.SendSwapCompletedEmailAsync"/> method.</item>
///   <item>Send an SMS recap via <see cref="ISmsTransport"/> after consulting the patient's
///     opt-out preference (EC-1 — invalid contact is logged without rolling back the swap).</item>
///   <item>Persist per-channel <c>NotificationLog</c> rows via
///     <see cref="INotificationEmailService"/> and <see cref="INotificationSmsService"/> for
///     auditable delivery outcomes.</item>
/// </list>
///
/// Contract: never throws.  All outcomes encoded in <see cref="SlotSwapNotificationResult"/>.
/// </summary>
public interface ISlotSwapNotificationService
{
    /// <summary>
    /// Delivers a slot-swap notification (email + SMS) for the patient described by
    /// <paramref name="request"/>.
    /// </summary>
    /// <param name="request">Swap completion payload including old and new appointment times.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Per-channel outcome.  Never throws — delivery failures are encoded in the result.
    /// </returns>
    Task<SlotSwapNotificationResult> SendSwapNotificationAsync(
        SlotSwapNotificationRequest request,
        CancellationToken cancellationToken = default);
}
