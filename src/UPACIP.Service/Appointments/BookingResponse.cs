namespace UPACIP.Service.Appointments;

/// <summary>
/// Response DTO returned on successful appointment booking (AC-4, US_018).
///
/// Contains all information required for the booking confirmation screen:
/// appointment date, time, provider, type, and the unique booking reference number.
/// </summary>
public sealed record BookingResponse(
    /// <summary>Unique identifier of the newly created appointment.</summary>
    Guid AppointmentId,

    /// <summary>
    /// Human-readable booking reference number.
    /// Format: BK-{YYYYMMDD}-{6-char-uppercase-alphanumeric} (e.g. BK-20260421-X7R2KP).
    /// Derived from the appointment creation date with a cryptographically random suffix (AC-4).
    /// </summary>
    string BookingReference,

    /// <summary>Appointment date in ISO-8601 format (YYYY-MM-DD, UTC).</summary>
    string AppointmentDate,

    /// <summary>Appointment start time in 24-hour UTC format (HH:mm).</summary>
    string AppointmentTime,

    /// <summary>Full display name of the assigned provider.</summary>
    string ProviderName,

    /// <summary>Appointment type label (e.g. "General Checkup").</summary>
    string AppointmentType,

    /// <summary>Current lifecycle status — always "scheduled" immediately after booking.</summary>
    string Status,

    /// <summary>UTC timestamp when the appointment record was created.</summary>
    DateTimeOffset CreatedAt);
