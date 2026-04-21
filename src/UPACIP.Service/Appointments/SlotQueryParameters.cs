using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Query parameters for <c>GET /api/appointments/slots</c>.
///
/// Validation rules (FR-013, EC-2):
///   - <see cref="StartDate"/> must be today or in the future (no past queries).
///   - <see cref="EndDate"/> must be ≥ <see cref="StartDate"/>.
///   - <see cref="EndDate"/> must be ≤ today + 90 days (90-day advance-booking window).
///
/// FluentValidation: <see cref="UPACIP.Service.Validation.SlotQueryParametersValidator"/>
/// enforces these rules before the request reaches the controller.
/// </summary>
public sealed record SlotQueryParameters
{
    /// <summary>
    /// Inclusive start of the date range (ISO-8601 date: YYYY-MM-DD).
    /// Must be today or a future date.
    /// </summary>
    [Required]
    public DateOnly StartDate { get; init; }

    /// <summary>
    /// Inclusive end of the date range (ISO-8601 date: YYYY-MM-DD).
    /// Defaults to <see cref="StartDate"/> when omitted (single-day query).
    /// Must be ≤ today + 90 days (FR-013).
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// Optional provider filter. When null, slots for all providers are returned.
    /// </summary>
    public Guid? ProviderId { get; init; }

    /// <summary>
    /// Optional appointment type filter (e.g. "General Checkup", "Follow-up").
    /// When null, all appointment types are included.
    /// </summary>
    public string? AppointmentType { get; init; }

    /// <summary>
    /// Resolved end date: <see cref="EndDate"/> if provided, otherwise <see cref="StartDate"/>.
    /// </summary>
    public DateOnly ResolvedEndDate => EndDate ?? StartDate;
}
