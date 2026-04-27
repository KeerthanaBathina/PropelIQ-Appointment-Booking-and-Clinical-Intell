namespace UPACIP.Service.Admin;

/// <summary>
/// Per-template CRUD operations for notification templates (US_060 AC-1, AC-2).
///
/// Separate from <see cref="IConfigurationService"/> to encapsulate per-ID
/// access patterns, variable placeholder validation, and forward-only update semantics.
/// </summary>
public interface INotificationTemplateService
{
    /// <summary>
    /// Returns the notification template with the given <paramref name="id"/>,
    /// or <c>null</c> if not found.
    /// </summary>
    Task<NotificationTemplateDto?> GetByIdAsync(
        string id, CancellationToken ct = default);

    /// <summary>
    /// Updates the notification template identified by <paramref name="id"/> with
    /// the supplied <paramref name="request"/> fields.
    ///
    /// <para>
    /// Throws <see cref="KeyNotFoundException"/> when the ID does not exist.
    /// The caller (controller) is responsible for running
    /// <c>IValidator&lt;UpdateNotificationTemplateByIdRequest&gt;</c> before invoking
    /// this method.
    /// </para>
    /// <para>
    /// Template updates are forward-only: already-sent <c>NotificationLog</c> entries
    /// are never modified (AC-2).
    /// </para>
    /// </summary>
    Task<NotificationTemplateDto> UpdateByIdAsync(
        string                                  id,
        UpdateNotificationTemplateByIdRequest   request,
        Guid                                    adminUserId,
        CancellationToken                       ct = default);
}
