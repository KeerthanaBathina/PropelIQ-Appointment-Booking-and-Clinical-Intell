using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements the appointment cancellation flow (US_019) with:
///
///   - Patient ownership verification resolved from JWT email claim (OWASP A01 IDOR prevention).
///   - Idempotent already-cancelled detection (EC-1).
///   - UTC 24-hour cutoff enforcement — no client-side timezone computation (AC-2, EC-2).
///   - Atomic status mutation + audit log write in a single EF Core SaveChanges call (AC-1, AC-4).
///   - EF Core optimistic concurrency handling on the <c>Appointment.Version</c> token.
///   - Redis slot cache invalidation immediately after commit (AC-3, NFR-030).
///   - Structured Serilog logging for all key cancellation events (NFR-035).
///
/// Implementation notes:
///   - The 24-hour window is evaluated as <c>(AppointmentTime - DateTime.UtcNow) > 24h</c>.
///     <c>AppointmentTime</c> is stored in UTC by the booking service; no conversion is performed here.
///   - AuditLog.UserId is resolved via <c>ApplicationUser.NormalizedEmail</c> lookup.
///     If the user cannot be resolved (edge case: account deleted after booking), the audit entry
///     is still written with a null UserId to preserve the audit trail (DR-016).
///   - IP address and user agent are not available in the service layer; they are left empty.
///     The Serilog structured log includes full request context via CorrelationIdMiddleware.
/// </summary>
public sealed class AppointmentCancellationService : IAppointmentCancellationService
{
    // 24-hour cancellation cutoff constant (AC-2).
    private static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(24);

    // Patient-visible policy message (AC-2). Exact wording required by spec.
    private const string PolicyBlockedMessage =
        "Cancellations within 24 hours are not permitted. Please contact the clinic.";

    private readonly ApplicationDbContext                     _db;
    private readonly IAppointmentSlotService                  _slotService;
    private readonly IWaitlistOfferQueue                      _waitlistQueue;
    private readonly IPreferredSlotSwapQueue                  _swapQueue;
    private readonly ILogger<AppointmentCancellationService>  _logger;

    public AppointmentCancellationService(
        ApplicationDbContext                    db,
        IAppointmentSlotService                 slotService,
        IWaitlistOfferQueue                     waitlistQueue,
        IPreferredSlotSwapQueue                 swapQueue,
        ILogger<AppointmentCancellationService> logger)
    {
        _db             = db;
        _slotService    = slotService;
        _waitlistQueue  = waitlistQueue;
        _swapQueue      = swapQueue;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAppointmentCancellationService — GetPatientAppointmentsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientAppointmentSummary>> GetPatientAppointmentsAsync(
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == userEmail && p.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "GetPatientAppointments: patient not found for email {Email}.", userEmail);
            return [];
        }

        // EC-2: Cancellable flag computed in UTC on the server — client must not re-evaluate.
        var now = DateTime.UtcNow;

        var appointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patient.Id)
            .OrderByDescending(a => a.AppointmentTime)
            .Select(a => new PatientAppointmentSummary(
                a.Id,
                a.BookingReference ?? string.Empty,
                a.AppointmentTime,
                a.ProviderName ?? "Unknown Provider",
                a.AppointmentType ?? "General",
                a.Status.ToString(),
                // Cancellable = Scheduled AND more than 24 hours remain (EC-2)
                a.Status == AppointmentStatus.Scheduled
                    && (a.AppointmentTime - now) > CancellationCutoff))
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "GetPatientAppointments: returned {Count} appointments for patient {PatientId}.",
            appointments.Count, patient.Id);

        return appointments;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAppointmentCancellationService — CancelAppointmentAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CancellationResult> CancelAppointmentAsync(
        Guid              appointmentId,
        string            userEmail,
        CancellationToken cancellationToken = default)
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
                "CancelAppointment: patient not found for email {Email}.", userEmail);
            return CancellationResult.NotFound();
        }

        // ── 2. Load appointment with ownership check (OWASP A01) ─────────────
        // Identical 404 response for "not found" and "not owned" — prevents IDOR enumeration.
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(
                a => a.Id == appointmentId && a.PatientId == patient.Id,
                cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "CancelAppointment: appointment {AppointmentId} not found or not owned by patient {PatientId}.",
                appointmentId, patient.Id);
            return CancellationResult.NotFound();
        }

        // ── 3. Idempotent check — already cancelled (EC-1) ───────────────────
        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            _logger.LogInformation(
                "CancelAppointment: appointment {AppointmentId} is already cancelled (idempotent).",
                appointmentId);
            return CancellationResult.AlreadyCancelled(appointment.Id);
        }

        // ── 4. Non-scheduled terminal state check ────────────────────────────
        // Completed / NoShow appointments cannot be cancelled.
        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "CancelAppointment: appointment {AppointmentId} has status {Status} — cannot cancel.",
                appointmentId, appointment.Status);
            return CancellationResult.PolicyBlocked(
                $"This appointment cannot be cancelled because it has status '{appointment.Status}'.");
        }

        // ── 5. UTC 24-hour cutoff check (AC-2, EC-2) ─────────────────────────
        // AppointmentTime is stored in UTC by the booking service.
        // All comparisons are UTC → no timezone conversion is performed here (EC-2).
        var timeUntilAppointment = appointment.AppointmentTime - DateTime.UtcNow;
        if (timeUntilAppointment <= CancellationCutoff)
        {
            _logger.LogInformation(
                "CancelAppointment: appointment {AppointmentId} is within the 24-hour cutoff " +
                "(timeUntilHours={TimeUntilHours:F1}). Policy blocked.",
                appointmentId, timeUntilAppointment.TotalHours);
            return CancellationResult.PolicyBlocked(PolicyBlockedMessage);
        }

        // ── 6. Mutate appointment status ─────────────────────────────────────
        var cancelledAt = DateTime.UtcNow;
        appointment.Status    = AppointmentStatus.Cancelled;
        appointment.UpdatedAt = cancelledAt;

        // ── 7. Resolve ApplicationUser for audit log (DR-016) ────────────────
        // Uses NormalizedEmail (upper-case) for consistent lookup.
        var applicationUserId = await _db.Users
            .Where(u => u.NormalizedEmail == userEmail.ToUpperInvariant())
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // ── 8. Append immutable audit log entry (AC-4, NFR-012) ─────────────
        // Written in the same EF Core change-tracking scope as the appointment update
        // so both are committed atomically in a single transaction (AC-4).
        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = applicationUserId,
            Action       = AuditAction.AppointmentCancelled,
            ResourceType = "Appointment",
            ResourceId   = appointment.Id,
            Timestamp    = cancelledAt,
            IpAddress    = string.Empty,  // not available in service layer
            UserAgent    = string.Empty,
        });

        // ── 9. Persist atomically ────────────────────────────────────────────
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Version mismatch: appointment was modified concurrently between our read and write.
            // Reload the row to determine the current state and return the appropriate result.
            _logger.LogWarning(ex,
                "Optimistic concurrency conflict on cancellation for appointment {AppointmentId}. " +
                "Reloading to determine current state.",
                appointmentId);

            var entry = ex.Entries.Single();
            await entry.ReloadAsync(cancellationToken);
            var reloaded = (Appointment)entry.Entity;

            // If the concurrent winner already cancelled it, report idempotent success (EC-1).
            return reloaded.Status == AppointmentStatus.Cancelled
                ? CancellationResult.AlreadyCancelled(reloaded.Id)
                : CancellationResult.NotFound();
        }

        // ── 10. Invalidate slot cache (AC-3, NFR-030) ────────────────────────
        // Must occur AFTER commit so stale entries are never re-added after invalidation.
        // Frees the slot within 1 minute by removing the cache entry for the affected date.
        await _slotService.InvalidateCacheAsync(
            DateOnly.FromDateTime(appointment.AppointmentTime),
            appointment.ProviderId,
            cancellationToken);

        // ── 11. Enqueue freed slot for waitlist matching (AC-2) ──────────────
        // Fire-and-forget: TryEnqueue is non-blocking; failures are logged inside the queue.
        // Slot details are reconstructed from the appointment record so we avoid a second DB read.
        var providerIdStr = appointment.ProviderId?.ToString("N") ?? Guid.Empty.ToString("N");
        var freedSlotId  = $"{appointment.AppointmentTime:yyyyMMdd}-{appointment.AppointmentTime:HHmm}-{providerIdStr}";
        var freedSlot = new SlotItem(
            SlotId:          freedSlotId,
            Date:            appointment.AppointmentTime.ToString("yyyy-MM-dd"),
            StartTime:       appointment.AppointmentTime.ToString("HH:mm"),
            EndTime:         appointment.AppointmentTime.AddMinutes(30).ToString("HH:mm"),
            ProviderName:    appointment.ProviderName ?? string.Empty,
            ProviderId:      appointment.ProviderId?.ToString() ?? string.Empty,
            AppointmentType: appointment.AppointmentType ?? string.Empty,
            Available:       true);
        _waitlistQueue.TryEnqueue(freedSlot);
        _swapQueue.TryEnqueue(freedSlot);    // US_021 — preferred-slot swap evaluation

        // ── 12. Structured audit log (NFR-035) ───────────────────────────────
        _logger.LogInformation(
            "Appointment cancelled: appointmentId={AppointmentId}, patientId={PatientId}, " +
            "appointmentTime={AppointmentTime}, cancelledAt={CancelledAt}.",
            appointment.Id,
            patient.Id,
            appointment.AppointmentTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            cancelledAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return CancellationResult.Succeeded(appointment.Id, cancelledAt);
    }
}
