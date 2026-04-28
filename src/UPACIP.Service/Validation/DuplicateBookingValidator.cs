using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Validation;

// ─────────────────────────────────────────────────────────────────────────────
// Exception thrown when a duplicate (patient_id, appointment_time) is detected.
// Caught by GlobalExceptionHandlerMiddleware → HTTP 409 Conflict.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thrown when an appointment already exists for the same patient at the same time (AC-3, DR-014).
/// Provides a user-friendly message and the conflicting appointment ID before the request reaches
/// the database unique constraint, preventing a raw PostgreSQL 23505 error from surfacing.
/// </summary>
public sealed class DuplicateBookingException : Exception
{
    public Guid PatientId             { get; }
    public DateTime AppointmentTime   { get; }
    public Guid? ExistingAppointmentId { get; }

    public DuplicateBookingException(Guid patientId, DateTime appointmentTime, Guid? existingAppointmentId)
        : base($"A booking already exists for this patient at {appointmentTime:yyyy-MM-dd HH:mm}. " +
               "Please choose a different time slot.")
    {
        PatientId              = patientId;
        AppointmentTime        = appointmentTime;
        ExistingAppointmentId  = existingAppointmentId;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Application-level duplicate booking check (US_085 AC-3).
/// Runs before the booking transaction so users receive a descriptive 409 Conflict
/// instead of a raw PostgreSQL 23505 constraint violation message.
/// The database unique constraint (from US_010) remains as the safety net for race conditions.
/// </summary>
public interface IDuplicateBookingValidator
{
    /// <summary>
    /// Verifies that no active appointment exists for <paramref name="patientId"/> at
    /// <paramref name="appointmentTime"/>. Throws <see cref="DuplicateBookingException"/>
    /// if a duplicate is found.
    /// </summary>
    /// <param name="patientId">Patient whose bookings are checked.</param>
    /// <param name="appointmentTime">Proposed UTC appointment time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="DuplicateBookingException">
    /// Thrown when an active booking for the same (patientId, appointmentTime) already exists.
    /// </exception>
    Task ValidateNoDuplicateAsync(Guid patientId, DateTime appointmentTime, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EF Core implementation of <see cref="IDuplicateBookingValidator"/>.
///
/// Uses <c>AsNoTracking()</c> on a lightweight projection to minimize read overhead.
/// Filters out <see cref="AppointmentStatus.Cancelled"/> records so a patient can
/// rebook a previously cancelled slot at the same time.
/// </summary>
public sealed class DuplicateBookingValidator : IDuplicateBookingValidator
{
    private readonly ApplicationDbContext                 _db;
    private readonly ILogger<DuplicateBookingValidator>  _logger;

    public DuplicateBookingValidator(
        ApplicationDbContext                db,
        ILogger<DuplicateBookingValidator>  logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ValidateNoDuplicateAsync(
        Guid              patientId,
        DateTime          appointmentTime,
        CancellationToken ct = default)
    {
        // Normalise to UTC so the comparison matches the stored timestamp kind (DR-014).
        var utcTime = DateTime.SpecifyKind(appointmentTime, DateTimeKind.Utc);

        // Lightweight projection — only the appointment ID is needed for the error payload.
        var existingId = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId      == patientId
                     && a.AppointmentTime == utcTime
                     && a.Status          != AppointmentStatus.Cancelled)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId.HasValue)
        {
            _logger.LogWarning(
                "Duplicate booking blocked: patientId={PatientId} appointmentTime={AppointmentTime} " +
                "existingAppointmentId={ExistingId}",
                patientId, utcTime, existingId.Value);

            throw new DuplicateBookingException(patientId, utcTime, existingId.Value);
        }
    }
}
