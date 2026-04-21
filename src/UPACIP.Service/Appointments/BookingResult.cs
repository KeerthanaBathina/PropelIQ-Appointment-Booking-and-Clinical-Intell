namespace UPACIP.Service.Appointments;

/// <summary>
/// Discriminated result type returned by <see cref="IAppointmentBookingService.BookAppointmentAsync"/>.
///
/// Allows the controller to produce the correct HTTP response without catching exceptions,
/// keeping exception handling contained within the service layer (OWASP A09 — avoid leaking
/// stack traces through exception propagation to the presentation layer).
/// </summary>
public sealed record BookingResult
{
    /// <summary>Outcome of the booking attempt.</summary>
    public BookingResultStatus Status { get; init; }

    /// <summary>
    /// Populated when <see cref="Status"/> is <see cref="BookingResultStatus.Success"/>.
    /// Contains the full booking confirmation to display to the patient (AC-4).
    /// </summary>
    public BookingResponse? Booking { get; init; }

    /// <summary>
    /// Up to 3 alternative available slots (AC-2).
    /// Populated when <see cref="Status"/> is <see cref="BookingResultStatus.Conflict"/>.
    /// </summary>
    public IReadOnlyList<SlotItem>? AlternativeSlots { get; init; }

    /// <summary>Human-readable error message for non-success results.</summary>
    public string? ErrorMessage { get; init; }

    // ── Factory methods ─────────────────────────────────────────────────────

    /// <summary>Booking completed successfully.</summary>
    public static BookingResult Succeeded(BookingResponse booking)
        => new() { Status = BookingResultStatus.Success, Booking = booking };

    /// <summary>
    /// Slot was taken concurrently — returns up to 3 alternatives (AC-2).
    /// Message matches the spec: "Slot no longer available."
    /// </summary>
    public static BookingResult Conflicted(IReadOnlyList<SlotItem> alternatives)
        => new()
        {
            Status           = BookingResultStatus.Conflict,
            AlternativeSlots = alternatives,
            ErrorMessage     = "Slot no longer available.",
        };

    /// <summary>Transient database failure persisted after retry exhaustion (EC-1, NFR-032).</summary>
    public static BookingResult Unavailable(string message)
        => new() { Status = BookingResultStatus.ServiceUnavailable, ErrorMessage = message };

    /// <summary>
    /// Redis hold not found or owned by a different patient (AC-3).
    /// The client must acquire a hold before confirming a booking.
    /// </summary>
    public static BookingResult HoldMismatch()
        => new()
        {
            Status       = BookingResultStatus.HoldNotOwned,
            ErrorMessage = "Hold not found or owned by a different patient. Acquire a hold before confirming.",
        };

    /// <summary>No active patient record exists for the authenticated user's email.</summary>
    public static BookingResult PatientNotFound()
        => new()
        {
            Status       = BookingResultStatus.PatientNotFound,
            ErrorMessage = "Patient record not found for the authenticated user.",
        };
}

/// <summary>Possible outcomes of an appointment booking attempt.</summary>
public enum BookingResultStatus
{
    /// <summary>Appointment created successfully.</summary>
    Success,

    /// <summary>Slot was taken by another booking — alternatives provided (AC-2).</summary>
    Conflict,

    /// <summary>Database unavailable after retry exhaustion (EC-1).</summary>
    ServiceUnavailable,

    /// <summary>No valid Redis hold exists for this patient/slot combination (AC-3).</summary>
    HoldNotOwned,

    /// <summary>No active Patient record found for the authenticated user's email.</summary>
    PatientNotFound,
}
