using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Admin;

// ─── Slot Templates ───────────────────────────────────────────────────────────

/// <summary>
/// One available (or blocked) time cell for a provider's weekly template.
/// Matches the FE <c>SlotCell</c> type in adminConfig.ts.
/// </summary>
public sealed record SlotCellDto(
    /// <summary>Day index 0=Mon … 4=Fri.</summary>
    [Range(0, 4)] int Day,
    /// <summary>Time label e.g. "9:00 AM" (max 20 chars).</summary>
    [Required, MaxLength(20)] string Time,
    bool Available);

/// <summary>Single provider's weekly slot template.</summary>
public sealed record ProviderSlotTemplateDto(
    Guid                    ProviderId,
    [Required, MaxLength(100)] string ProviderName,
    IReadOnlyList<SlotCellDto> Slots);

/// <summary>Full slot-templates configuration sent to / received from the API.</summary>
public sealed record SlotTemplatesConfigDto(
    IReadOnlyList<ProviderSlotTemplateDto> Providers);

// ─── Notification Templates ───────────────────────────────────────────────────

/// <summary>Single notification template row.</summary>
public sealed record NotificationTemplateDto(
    string Id,
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(50)]  string Channel,
    [Required, MaxLength(200)] string Trigger,
    [Required, MaxLength(50)]  string Status,
    string? BodyTemplate,
    // US_060 — optional fields (null-safe for templates stored before US_060 migration)
    string?   Subject   = null,
    DateTime? UpdatedAt = null,
    string?   UpdatedBy = null);

/// <summary>Wrapper returned by GET /api/admin/config/notifications.</summary>
public sealed record NotificationTemplatesConfigDto(
    IReadOnlyList<NotificationTemplateDto> Templates);

// ─── Business Hours ───────────────────────────────────────────────────────────

/// <summary>Regular operating hours for a single day label (e.g. "Monday–Friday").</summary>
public sealed record DayHoursDto(
    [Required, MaxLength(30)] string Day,
    string? OpenTime,
    string? CloseTime);

/// <summary>Holiday entry.</summary>
public sealed record HolidayDto(
    string Id,
    [Required, MaxLength(100)] string Name,
    /// <summary>ISO date string "YYYY-MM-DD".</summary>
    [Required, MaxLength(10)]  string Date,
    [Required, MaxLength(20)]  string Status);

/// <summary>Business hours + holidays configuration.</summary>
public sealed record BusinessHoursConfigDto(
    IReadOnlyList<DayHoursDto> RegularHours,
    IReadOnlyList<HolidayDto>  Holidays);

// ─── Risk Thresholds ─────────────────────────────────────────────────────────

/// <summary>AI risk threshold configuration (matches FE <c>RiskThresholdsConfig</c>).</summary>
public sealed record RiskThresholdsConfigDto(
    [Range(0, 100)] int HighRiskThreshold,
    [Range(0, 100)] int MediumRiskThreshold,
    [Range(1, 9999)] int MinAppointmentsForAiScore,
    bool AutoOutreach);

// ─── User Management ─────────────────────────────────────────────────────────

/// <summary>Admin/staff user row returned by GET /api/admin/users.</summary>
public sealed record AdminUserDto(
    string   Id,
    string   FullName,
    string   Email,
    string   Role,
    string?  RoleSubtitle,
    string   Status,
    string?  LastLoginAt,
    /// <summary>ISO 8601 UTC timestamp of account creation (US_061 AC-2).</summary>
    string?  CreatedAt = null);

/// <summary>Wrapper returned by GET /api/admin/users.</summary>
public sealed record AdminUsersResponseDto(
    IReadOnlyList<AdminUserDto> Users,
    /// <summary>Total un-filtered count (US_061 AC-2 — pagination meta).</summary>
    int Total = 0);

/// <summary>Payload for POST /api/admin/users/invite.</summary>
public sealed record InviteUserRequestDto(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(100)]               string FullName,
    [Required, MaxLength(20)]                string Role);

/// <summary>Payload for PUT /api/admin/users/:id/status.</summary>
public sealed record SetUserStatusRequestDto(
    [Required, MaxLength(20)] string Status);

// ─── US_060: Notification Template per-ID update ──────────────────────────────

/// <summary>
/// Request body for PUT /api/admin/config/notifications/{id} (US_060 AC-1, AC-2).
/// Replaces a single template's editable fields.  <see cref="BodyTemplate"/> is validated
/// for unknown variable placeholders (422 on failure).
/// </summary>
public sealed record UpdateNotificationTemplateByIdRequest(
    [Required, MaxLength(50)]   string Channel,
    [MaxLength(200)]            string? Subject,
    [Required, MaxLength(5000)] string BodyTemplate,
    [Required, MaxLength(50)]   string Status);

// ─── US_060: Risk configuration with scoring parameters ───────────────────────

/// <summary>
/// Scoring parameter weights for the AI no-show risk model (US_060 AC-3, AC-4).
/// All three weights must be non-negative and sum to exactly 1.0 (± 0.01 tolerance).
/// </summary>
public sealed record ScoringParametersDto(
    decimal PriorNoShowsWeight,
    decimal CancellationHistoryWeight,
    decimal AppointmentLeadTimeWeight);

/// <summary>
/// Full risk configuration response including scoring parameters and deferred
/// recalculation status (US_060 AC-3, AC-4).
/// </summary>
public sealed record RiskConfigDto(
    [Range(0, 100)]  int  HighRiskThreshold,
    [Range(0, 100)]  int  MediumRiskThreshold,
    [Range(1, 9999)] int  MinAppointmentsForAiScore,
    bool             AutoOutreach,
    ScoringParametersDto ScoringParameters,
    /// <summary>
    /// True when thresholds/params have been updated but the batch recalculation
    /// job has not yet run.  Cleared by the background recalculation job.
    /// </summary>
    bool             RecalculationPending,
    DateTime?        UpdatedAt,
    string?          UpdatedBy);

/// <summary>
/// Request body for PUT /api/admin/config/risk (US_060 AC-3, AC-4).
/// Scoring parameter weights must sum to 1.0 (validated by FluentValidation).
/// </summary>
public sealed record UpdateRiskConfigRequest(
    [Range(0, 100)]  int  HighRiskThreshold,
    [Range(0, 100)]  int  MediumRiskThreshold,
    [Range(1, 9999)] int  MinAppointmentsForAiScore,
    bool             AutoOutreach,
    ScoringParametersDto ScoringParameters);
