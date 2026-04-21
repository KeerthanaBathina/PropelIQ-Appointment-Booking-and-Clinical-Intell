namespace UPACIP.Service.Appointments;

/// <summary>
/// Outcome codes for the appointment cancellation flow (US_019).
/// Maps 1-to-1 to the HTTP status codes returned by the cancellation endpoint.
/// </summary>
public enum CancellationResultStatus
{
    /// <summary>Cancellation completed; appointment status is now Cancelled (AC-1).</summary>
    Success,

    /// <summary>
    /// Request was made within the 24-hour UTC cutoff (AC-2).
    /// HTTP 422. Patient-visible message: "Cancellations within 24 hours are not permitted."
    /// </summary>
    PolicyBlocked,

    /// <summary>
    /// Appointment was already in Cancelled state when the request arrived (EC-1).
    /// HTTP 409. Patient-visible message: "This appointment has already been cancelled."
    /// </summary>
    AlreadyCancelled,

    /// <summary>
    /// Appointment does not exist or is not owned by the requesting patient (OWASP A01 IDOR guard).
    /// HTTP 404. Same message for both cases to prevent enumeration.
    /// </summary>
    NotFound,
}
