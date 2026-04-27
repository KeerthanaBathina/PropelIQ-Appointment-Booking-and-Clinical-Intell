using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Admin;

// ─── Slot Template Blocks ────────────────────────────────────────────────────

/// <summary>
/// One time-window block within a <c>SlotTemplate</c> defining appointment type and availability.
/// Used in both requests (upsert) and responses (GET).
/// </summary>
public sealed record SlotTemplateBlockDto(
    /// <summary>DB id — null in create requests, non-null in responses and update requests.</summary>
    Guid?                            BlockId,
    /// <summary>Block start time in the clinic's local time (no date component).</summary>
    [Required] TimeOnly              StartTime,
    /// <summary>Block end time — must be after StartTime.</summary>
    [Required] TimeOnly              EndTime,
    /// <summary>Appointment type offered in this window (max 50 chars).</summary>
    [Required, MaxLength(50)] string AppointmentType,
    /// <summary>Whether this block is open for booking.</summary>
    bool                             IsAvailable);

// ─── Slot Template Request / Response ────────────────────────────────────────

/// <summary>
/// Request body for <c>PUT api/admin/config/slots/{providerId}/{dayOfWeek}</c>.
/// Replaces all blocks on the template for the given provider/day combination.
/// </summary>
public sealed record UpsertSlotTemplateRequest(
    /// <summary>
    /// Client-held row version for optimistic concurrency.
    /// Omit (null) when creating a new template for the first time;
    /// must match the current DB version when updating to prevent lost-write anomalies.
    /// </summary>
    int? Version,
    /// <summary>
    /// Replacement set of time blocks.  Must not be empty and blocks must not overlap.
    /// </summary>
    [Required] IReadOnlyList<SlotTemplateBlockDto> Blocks);

/// <summary>GET response for a single slot template.</summary>
public sealed record SlotTemplateResponse(
    Guid                                    SlotTemplateId,
    Guid                                    ProviderId,
    int                                     DayOfWeek,
    /// <summary>Current row version — pass back to PUT to enable optimistic locking.</summary>
    int                                     Version,
    DateTime                                CreatedAt,
    DateTime                                UpdatedAt,
    IReadOnlyList<SlotTemplateBlockDto>     Blocks);

// ─── Upsert result (concurrency-safe return type) ────────────────────────────

/// <summary>Status codes for <see cref="SlotTemplateUpsertResult"/>.</summary>
public enum SlotTemplateUpsertStatus
{
    /// <summary>Template was created or updated successfully.</summary>
    Success,
    /// <summary>Optimistic-concurrency conflict — client Version is stale; re-fetch and retry.</summary>
    ConcurrencyConflict,
    /// <summary>Referenced provider (user) does not exist.</summary>
    ProviderNotFound,
}

/// <summary>
/// Discriminated result returned by <see cref="ISlotTemplateService.UpsertAsync"/>
/// to allow the controller to map each status to the correct HTTP code without leaking
/// EF Core exceptions across the service boundary.
/// </summary>
public sealed record SlotTemplateUpsertResult(
    SlotTemplateUpsertStatus Status,
    SlotTemplateResponse?    Template = null)
{
    public static SlotTemplateUpsertResult Success(SlotTemplateResponse t) =>
        new(SlotTemplateUpsertStatus.Success, t);

    public static SlotTemplateUpsertResult Conflict() =>
        new(SlotTemplateUpsertStatus.ConcurrencyConflict);

    public static SlotTemplateUpsertResult ProviderNotFound() =>
        new(SlotTemplateUpsertStatus.ProviderNotFound);
}

// ─── Affected-Appointments preview ───────────────────────────────────────────

/// <summary>
/// A compact appointment summary used in conflict-preview and holiday-impact responses.
/// </summary>
public sealed record AffectedAppointmentDto(
    Guid     AppointmentId,
    string?  BookingReference,
    DateTime AppointmentTime,
    string?  ProviderName,
    string?  AppointmentType);

/// <summary>
/// Response for <c>GET api/admin/config/slots/{providerId}/{dayOfWeek}/affected-appointments</c>.
/// </summary>
public sealed record AffectedAppointmentsResponse(
    int                                     Count,
    IReadOnlyList<AffectedAppointmentDto>   Appointments);
