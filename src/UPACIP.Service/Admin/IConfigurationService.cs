namespace UPACIP.Service.Admin;

/// <summary>
/// Contract for admin configuration CRUD operations (US_058 AC-4).
///
/// All methods persist changes to <see cref="UPACIP.DataAccess.Entities.SystemConfig"/>
/// (key-value JSON store) or <see cref="UPACIP.DataAccess.Entities.ProviderAvailabilityTemplate"/>,
/// cache-bust after save, and append an audit log entry (NFR-012).
/// </summary>
public interface IConfigurationService
{
    // ── Slot templates ────────────────────────────────────────────────────────

    Task<SlotTemplatesConfigDto>    GetSlotTemplatesAsync(CancellationToken ct = default);
    Task<SlotTemplatesConfigDto>    UpdateSlotTemplatesAsync(SlotTemplatesConfigDto dto, Guid adminUserId, CancellationToken ct = default);

    // ── Notification templates ────────────────────────────────────────────────

    Task<NotificationTemplatesConfigDto>    GetNotificationTemplatesAsync(CancellationToken ct = default);
    Task<NotificationTemplatesConfigDto>    UpdateNotificationTemplateAsync(NotificationTemplateDto dto, Guid adminUserId, CancellationToken ct = default);
    Task<NotificationTemplateDto>           AddNotificationTemplateAsync(NotificationTemplateDto dto, Guid adminUserId, CancellationToken ct = default);

    // ── Business hours ────────────────────────────────────────────────────────

    Task<BusinessHoursConfigDto>    GetBusinessHoursAsync(CancellationToken ct = default);
    Task<BusinessHoursConfigDto>    UpdateBusinessHoursAsync(BusinessHoursConfigDto dto, Guid adminUserId, CancellationToken ct = default);

    // ── Risk thresholds ───────────────────────────────────────────────────────

    Task<RiskThresholdsConfigDto>   GetRiskThresholdsAsync(CancellationToken ct = default);
    Task<RiskThresholdsConfigDto>   UpdateRiskThresholdsAsync(RiskThresholdsConfigDto dto, Guid adminUserId, CancellationToken ct = default);
}
