namespace UPACIP.Service.Appointments;

/// <summary>
/// Discriminated result type returned by
/// <see cref="IAppointmentCancellationService.CancelAppointmentAsync"/>.
///
/// Mirrors the pattern established by <see cref="BookingResult"/> so the controller can produce
/// the correct HTTP response without catching exceptions (OWASP A09 — no stack-trace leakage).
/// </summary>
public sealed record CancellationResult
{
    /// <summary>Outcome of the cancellation attempt.</summary>
    public CancellationResultStatus Status { get; init; }

    /// <summary>Patient-visible message (populated for all non-success outcomes).</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Identifier of the appointment targeted by the request.
    /// Populated for <see cref="CancellationResultStatus.Success"/> and
    /// <see cref="CancellationResultStatus.AlreadyCancelled"/>.
    /// </summary>
    public Guid? AppointmentId { get; init; }

    /// <summary>UTC timestamp of the cancellation. Populated on success only.</summary>
    public DateTime? CancelledAt { get; init; }

    // ── Factory methods ─────────────────────────────────────────────────────

    /// <summary>Cancellation completed. Slot is released and audit log written (AC-1, AC-4).</summary>
    public static CancellationResult Succeeded(Guid appointmentId, DateTime cancelledAt)
        => new()
        {
            Status        = CancellationResultStatus.Success,
            AppointmentId = appointmentId,
            CancelledAt   = cancelledAt,
        };

    /// <summary>
    /// Request blocked by the 24-hour UTC policy (AC-2) or the appointment is in a
    /// terminal non-cancellable state (Completed / NoShow).
    /// </summary>
    public static CancellationResult PolicyBlocked(string message)
        => new()
        {
            Status  = CancellationResultStatus.PolicyBlocked,
            Message = message,
        };

    /// <summary>
    /// Appointment was already cancelled — idempotent response (EC-1).
    /// Message matches the spec: "This appointment has already been cancelled."
    /// </summary>
    public static CancellationResult AlreadyCancelled(Guid appointmentId)
        => new()
        {
            Status        = CancellationResultStatus.AlreadyCancelled,
            AppointmentId = appointmentId,
            Message       = "This appointment has already been cancelled.",
        };

    /// <summary>
    /// Appointment not found or not owned by the requesting patient (OWASP A01).
    /// Identical response for both cases prevents IDOR-based enumeration.
    /// </summary>
    public static CancellationResult NotFound()
        => new()
        {
            Status  = CancellationResultStatus.NotFound,
            Message = "Appointment not found.",
        };
}
