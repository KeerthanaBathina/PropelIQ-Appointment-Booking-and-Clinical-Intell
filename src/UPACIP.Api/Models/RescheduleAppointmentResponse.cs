namespace UPACIP.Api.Models;

/// <summary>
/// HTTP response body for a successful patient-initiated appointment reschedule (US_023, AC-3).
///
/// Returned with HTTP 200 OK from <c>PUT /api/appointments/{appointmentId}/reschedule</c>.
///
/// Both original and new appointment times are included so the frontend can display the
/// before/after confirmation view required by AC-3.
/// </summary>
public sealed record RescheduleAppointmentResponse(
    /// <summary>UUID of the rescheduled appointment (same ID — updated record).</summary>
    Guid AppointmentId,

    /// <summary>Booking reference unchanged from the original booking (AC-4).</summary>
    string? BookingReference,

    /// <summary>UTC timestamp of the original appointment before rescheduling (AC-3).</summary>
    DateTimeOffset OldAppointmentTime,

    /// <summary>UTC timestamp of the new confirmed appointment time (AC-3).</summary>
    DateTimeOffset NewAppointmentTime,

    /// <summary>Provider display name for the new appointment.</summary>
    string ProviderName,

    /// <summary>Appointment type carried through from the original booking.</summary>
    string AppointmentType,

    /// <summary>Patient-visible confirmation message.</summary>
    string Message);
