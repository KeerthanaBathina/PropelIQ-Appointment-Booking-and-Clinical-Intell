namespace UPACIP.Service.Appointments;

/// <summary>Outcome of an atomic appointment reschedule operation (US_021 AC-1).</summary>
public enum RescheduleStatus
{
    /// <summary>Appointment was moved to the new slot successfully.</summary>
    Succeeded,
    /// <summary>Appointment not found.</summary>
    NotFound,
    /// <summary>Optimistic-concurrency conflict — caller should retry next candidate (EC-1).</summary>
    ConcurrencyConflict,
    /// <summary>New slot is unavailable (taken by another patient).</summary>
    SlotUnavailable,
}

/// <summary>Result of <see cref="IAppointmentBookingService.RescheduleAppointmentAsync"/>.</summary>
public sealed record RescheduleResult(
    RescheduleStatus Status,
    DateTime?        OldAppointmentTime = null,
    DateTime?        NewAppointmentTime = null)
{
    public static RescheduleResult Succeeded(DateTime oldTime, DateTime newTime)
        => new(RescheduleStatus.Succeeded, oldTime, newTime);

    public static RescheduleResult NotFound()
        => new(RescheduleStatus.NotFound);

    public static RescheduleResult Conflict()
        => new(RescheduleStatus.ConcurrencyConflict);

    public static RescheduleResult SlotTaken()
        => new(RescheduleStatus.SlotUnavailable);
}
