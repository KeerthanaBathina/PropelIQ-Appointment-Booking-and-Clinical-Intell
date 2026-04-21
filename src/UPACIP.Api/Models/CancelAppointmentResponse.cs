namespace UPACIP.Api.Models;

/// <summary>
/// HTTP response body for a successful appointment cancellation (US_019, AC-1).
///
/// Returned with HTTP 200 OK from <c>DELETE /api/appointments/{appointmentId}</c>.
/// </summary>
public sealed record CancelAppointmentResponse(
    /// <summary>UUID of the cancelled appointment.</summary>
    Guid AppointmentId,

    /// <summary>New lifecycle status — always "Cancelled" for a successful cancellation.</summary>
    string Status,

    /// <summary>Confirmation message displayed to the patient.</summary>
    string Message,

    /// <summary>UTC timestamp when the cancellation was recorded.</summary>
    DateTimeOffset CancelledAt);
