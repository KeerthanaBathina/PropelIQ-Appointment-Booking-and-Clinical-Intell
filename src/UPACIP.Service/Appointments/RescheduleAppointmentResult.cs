namespace UPACIP.Service.Appointments;

/// <summary>
/// Outcome classification for a patient-initiated reschedule attempt (US_023).
/// </summary>
public enum RescheduleAppointmentStatus
{
    /// <summary>Appointment moved atomically to the new slot.</summary>
    Succeeded,

    /// <summary>Appointment not found or does not belong to the requesting patient (OWASP A01).</summary>
    NotFound,

    /// <summary>
    /// Reschedule is within 24 hours of the original appointment time (AC-2).
    /// Message: "Cannot reschedule within 24 hours of appointment."
    /// </summary>
    PolicyBlocked,

    /// <summary>
    /// Walk-in appointments cannot be rescheduled by patients (EC-2).
    /// Message: "Walk-in appointments cannot be rescheduled."
    /// </summary>
    WalkInRestricted,

    /// <summary>
    /// Selected replacement slot is no longer available — optimistic concurrency conflict (EC-1).
    /// Frontend should refresh slot options and let the patient pick again.
    /// </summary>
    SlotUnavailable,
}

/// <summary>
/// Result of <see cref="IAppointmentReschedulingService.RescheduleAppointmentAsync"/>.
///
/// On <see cref="RescheduleAppointmentStatus.Succeeded"/>:
///   <see cref="OldAppointmentTime"/>, <see cref="NewAppointmentTime"/>,
///   <see cref="ProviderName"/>, <see cref="AppointmentType"/>, and
///   <see cref="BookingReference"/> are populated for the confirmation response (AC-3).
/// </summary>
public sealed record RescheduleAppointmentResult(
    RescheduleAppointmentStatus Status,
    /// <summary>Human-readable outcome message for error cases.</summary>
    string?   Message           = null,
    Guid?     AppointmentId     = null,
    string?   BookingReference  = null,
    DateTime? OldAppointmentTime = null,
    DateTime? NewAppointmentTime = null,
    string?   ProviderName      = null,
    string?   AppointmentType   = null)
{
    public static RescheduleAppointmentResult Success(
        Guid     appointmentId,
        string?  bookingReference,
        DateTime oldTime,
        DateTime newTime,
        string   providerName,
        string   appointmentType)
        => new(
            RescheduleAppointmentStatus.Succeeded,
            AppointmentId:      appointmentId,
            BookingReference:   bookingReference,
            OldAppointmentTime: oldTime,
            NewAppointmentTime: newTime,
            ProviderName:       providerName,
            AppointmentType:    appointmentType);

    public static RescheduleAppointmentResult NotFound()
        => new(RescheduleAppointmentStatus.NotFound,
            "Appointment not found.");

    public static RescheduleAppointmentResult PolicyBlocked(string message)
        => new(RescheduleAppointmentStatus.PolicyBlocked, message);

    public static RescheduleAppointmentResult WalkInRestricted(string message)
        => new(RescheduleAppointmentStatus.WalkInRestricted, message);

    public static RescheduleAppointmentResult SlotUnavailable(string message)
        => new(RescheduleAppointmentStatus.SlotUnavailable, message);
}
