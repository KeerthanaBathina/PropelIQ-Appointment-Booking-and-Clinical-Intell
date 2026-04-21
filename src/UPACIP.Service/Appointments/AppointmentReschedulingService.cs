using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements patient-initiated atomic appointment rescheduling (US_023 FR-023).
///
/// Pipeline (each step runs in order; any rejection exits early):
///   1. Resolve patient from JWT email (OWASP A01 — IDOR prevention).
///   2. Load appointment with ownership guard (identical 404 for "not found" and "not owned").
///   3. Reject if IsWalkIn = true (EC-2).
///   4. Reject if appointment status is not Scheduled (terminal state guard).
///   5. Enforce UTC 24-hour reschedule cutoff against the ORIGINAL appointment time (AC-2).
///   6. Delegate atomic slot swap to <see cref="IAppointmentBookingService.RescheduleAppointmentAsync"/>
///      (optimistic locking, cache invalidation, slot availability check).
///   7. Write immutable audit log entry (AC-4, NFR-012, DR-016).
///   8. Enqueue downstream notifications (email/SMS, calendar sync) as fire-and-forget (AC-4).
///
/// Timezone semantics (AC-2, EC-2):
///   AppointmentTime is stored in UTC by the booking service.
///   All temporal comparisons in this service are UTC — no client-provided timezone is accepted.
///
/// Concurrency (AC-1, EC-1):
///   Optimistic locking is handled inside <see cref="AppointmentBookingService.RescheduleAppointmentAsync"/>.
///   When the new slot is taken during confirmation, <see cref="RescheduleStatus.SlotUnavailable"/>
///   or <see cref="RescheduleStatus.ConcurrencyConflict"/> is returned and surfaced as 409 to the frontend.
/// </summary>
public sealed class AppointmentReschedulingService : IAppointmentReschedulingService
{
    // ── Policy constants ────────────────────────────────────────────────────

    /// <summary>UTC window before the original appointment within which rescheduling is blocked (AC-2).</summary>
    private static readonly TimeSpan RescheduleCutoff = TimeSpan.FromHours(24);

    /// <summary>Exact patient-visible message required by AC-2.</summary>
    private const string PolicyBlockedMessage =
        "Cannot reschedule within 24 hours of appointment.";

    /// <summary>Patient-visible message for walk-in appointments (EC-2).</summary>
    private const string WalkInRestrictedMessage =
        "Walk-in appointments cannot be rescheduled.";

    /// <summary>Patient-visible message for slot-conflict outcomes (EC-1).</summary>
    private const string SlotUnavailableMessage =
        "The selected slot is no longer available. Please choose a different time.";

    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                      _db;
    private readonly IAppointmentBookingService                _bookingService;
    private readonly ILogger<AppointmentReschedulingService>   _logger;

    public AppointmentReschedulingService(
        ApplicationDbContext                     db,
        IAppointmentBookingService               bookingService,
        ILogger<AppointmentReschedulingService>  logger)
    {
        _db             = db;
        _bookingService = bookingService;
        _logger         = logger;
    }

    // ── IAppointmentReschedulingService ─────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RescheduleAppointmentResult> RescheduleAppointmentAsync(
        Guid                         appointmentId,
        RescheduleAppointmentRequest request,
        string                       userEmail,
        CancellationToken            cancellationToken = default)
    {
        // ── 1. Resolve patient from JWT email (OWASP A01) ────────────────────
        // PatientId is NEVER accepted from the request body.
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == userEmail && p.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "RescheduleAppointment: patient not found for email {Email}.", userEmail);
            return RescheduleAppointmentResult.NotFound();
        }

        // ── 2. Load appointment with ownership check (OWASP A01) ─────────────
        // Identical 404 for "not found" and "not owned" — prevents IDOR enumeration.
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == appointmentId && a.PatientId == patient.Id,
                cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "RescheduleAppointment: appointment {AppointmentId} not found or not owned by patient {PatientId}.",
                appointmentId, patient.Id);
            return RescheduleAppointmentResult.NotFound();
        }

        // ── 3. Walk-in restriction (EC-2) ─────────────────────────────────────
        // Walk-in appointments are created by staff and are outside the patient
        // self-service reschedule scope.
        if (appointment.IsWalkIn)
        {
            _logger.LogInformation(
                "RescheduleAppointment: appointment {AppointmentId} is a walk-in — blocked for patient.",
                appointmentId);
            return RescheduleAppointmentResult.WalkInRestricted(WalkInRestrictedMessage);
        }

        // ── 4. Status guard — only Scheduled appointments can be rescheduled ──
        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "RescheduleAppointment: appointment {AppointmentId} has status {Status} — cannot reschedule.",
                appointmentId, appointment.Status);
            return RescheduleAppointmentResult.PolicyBlocked(
                $"This appointment cannot be rescheduled because it has status '{appointment.Status}'.");
        }

        // ── 5. UTC 24-hour cutoff against the ORIGINAL appointment time (AC-2) ─
        // AppointmentTime is stored in UTC; no timezone conversion is performed (EC-2).
        var originalAppointmentTime = appointment.AppointmentTime;
        var timeUntilOriginal = originalAppointmentTime - DateTime.UtcNow;
        if (timeUntilOriginal <= RescheduleCutoff)
        {
            _logger.LogInformation(
                "RescheduleAppointment: appointment {AppointmentId} is within the 24-hour reschedule cutoff " +
                "(timeUntilHours={TimeUntilHours:F1}). Blocked.",
                appointmentId, timeUntilOriginal.TotalHours);
            return RescheduleAppointmentResult.PolicyBlocked(PolicyBlockedMessage);
        }

        // ── 6. Atomic slot swap via AppointmentBookingService (AC-1, EC-1) ────
        // AppointmentBookingService.RescheduleAppointmentAsync handles:
        //   - Slot availability verification
        //   - EF Core optimistic locking (Version concurrency token)
        //   - Cache invalidation for old + new slot dates
        //   - Structured logging of the slot move
        var slotItem = new SlotItem(
            SlotId:          request.SlotId,
            Date:            request.NewAppointmentTime.ToString("yyyy-MM-dd"),
            StartTime:       request.NewAppointmentTime.ToString("HH:mm"),
            EndTime:         request.NewAppointmentTime.AddMinutes(30).ToString("HH:mm"),
            ProviderName:    request.ProviderName,
            ProviderId:      request.ProviderId.ToString(),
            AppointmentType: request.AppointmentType,
            Available:       true);

        var rescheduleResult = await _bookingService.RescheduleAppointmentAsync(
            appointmentId, slotItem, cancellationToken);

        if (rescheduleResult.Status is RescheduleStatus.SlotUnavailable
                                    or RescheduleStatus.ConcurrencyConflict)
        {
            _logger.LogInformation(
                "RescheduleAppointment: slot conflict for appointment {AppointmentId}, slot {SlotId}.",
                appointmentId, request.SlotId);
            return RescheduleAppointmentResult.SlotUnavailable(SlotUnavailableMessage);
        }

        if (rescheduleResult.Status == RescheduleStatus.NotFound)
        {
            // Should not happen since we already loaded the appointment — guard for safety.
            return RescheduleAppointmentResult.NotFound();
        }

        // ── 7. Audit log — atomic with the reschedule in EF Core was handled
        //       inside AppointmentBookingService. We add a patient-layer audit entry here
        //       to record who initiated the reschedule (DR-016, NFR-012). ─────
        var applicationUserId = await _db.Users
            .Where(u => u.NormalizedEmail == userEmail.ToUpperInvariant())
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = applicationUserId,
            Action       = AuditAction.AppointmentRescheduled,
            ResourceType = "Appointment",
            ResourceId   = appointmentId,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = string.Empty,  // not available in service layer
            UserAgent    = string.Empty,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit log failure must NOT fail the reschedule — the appointment has already moved.
            // Log and continue so the patient receives a success response.
            _logger.LogError(ex,
                "RescheduleAppointment: audit log write failed for appointment {AppointmentId}. " +
                "Reschedule was committed successfully.",
                appointmentId);
        }

        // ── 8. Downstream notifications (AC-4) ────────────────────────────────
        // Fire-and-forget: notification and calendar-sync dispatching are out of scope
        // for this task (US_034, US_025). The log entry below serves as a structured
        // signal for future integration — simply log the intent and continue.
        _logger.LogInformation(
            "Appointment rescheduled (notification trigger pending US_034/US_025): " +
            "appointmentId={AppointmentId}, patientId={PatientId}, " +
            "from={OldTime}, to={NewTime}.",
            appointmentId,
            patient.Id,
            rescheduleResult.OldAppointmentTime!.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            rescheduleResult.NewAppointmentTime!.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return RescheduleAppointmentResult.Success(
            appointmentId:    appointmentId,
            bookingReference: appointment.BookingReference,
            oldTime:          rescheduleResult.OldAppointmentTime!.Value,
            newTime:          rescheduleResult.NewAppointmentTime!.Value,
            providerName:     request.ProviderName,
            appointmentType:  request.AppointmentType);
    }
}
