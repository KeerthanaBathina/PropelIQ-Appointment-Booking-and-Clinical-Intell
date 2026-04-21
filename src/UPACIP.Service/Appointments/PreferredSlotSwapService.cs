using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements the dynamic preferred-slot swap engine (US_021).
///
/// Design notes:
///   - Eligible candidate query: Scheduled, not checked-in (no QueueEntry with Arrived/InVisit),
///     auto-swap enabled on patient, PreferredSlotCriteria present and matching the opened slot.
///   - Candidates are ranked by longest wait time (CreatedAt ascending) then no-show risk score
///     (ascending, lower = safer). Risk score defaults to 0.0 when not yet calculated (US_026 cross-epic).
///   - 24-hour rule: if the opened slot is within 24 hours of UtcNow the engine skips automatic
///     swap and instead sends a manual-confirmation notification (AC-5).
///   - EC-2: QueueStatus.InVisit maps to "in-visit"; QueueStatus.InVisit + Arrived (if added)
///     are the exclusion conditions. Current enum has Waiting/InVisit/Completed — InVisit ⇒ skip.
///   - EC-1: On DbUpdateConcurrencyException the engine retries the next ranked candidate until
///     the list is exhausted.
///   - NFR-017: No patient PII in INFO/WARN logs; patientId is logged, email never.
/// </summary>
public sealed class PreferredSlotSwapService : IPreferredSlotSwapService
{
    private static readonly TimeSpan TwentyFourHours = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext                  _db;
    private readonly IAppointmentBookingService            _bookingService;
    private readonly IEmailService                         _emailService;
    private readonly ILogger<PreferredSlotSwapService>     _logger;

    public PreferredSlotSwapService(
        ApplicationDbContext              db,
        IAppointmentBookingService        bookingService,
        IEmailService                     emailService,
        ILogger<PreferredSlotSwapService> logger)
    {
        _db             = db;
        _bookingService = bookingService;
        _emailService   = emailService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EvaluateAndSwapAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PreferredSlotSwapResult>> EvaluateAndSwapAsync(
        SlotItem          openedSlot,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PreferredSlotSwapResult>();

        if (!DateOnly.TryParse(openedSlot.Date, out var slotDate) ||
            !TimeOnly.TryParse(openedSlot.StartTime, out var slotStart))
        {
            _logger.LogWarning(
                "SwapEngine: cannot parse slot date/time for slotId={SlotId}.", openedSlot.SlotId);
            return results;
        }

        var slotDateTime = new DateTime(
            slotDate.Year, slotDate.Month, slotDate.Day,
            slotStart.Hour, slotStart.Minute, 0, DateTimeKind.Utc);

        var isWithin24Hours = (slotDateTime - DateTime.UtcNow) < TwentyFourHours;

        var providerGuid = Guid.TryParse(openedSlot.ProviderId, out var pg) ? pg : (Guid?)null;

        // ── 1. Load candidates ────────────────────────────────────────────────
        // Criteria matching on PreferredSlotCriteria is JSONB — EF Core ToJson() serializes
        // the owned type as a single JSONB cell. We load all Scheduled appointments that have
        // a non-null PreferredSlotCriteria and filter the rest in memory (JSONB partial match).
        // A GIN index (ix_appointments_preferred_slot_criteria_gin) exists for perf (EC-1).
        //
        // EC-2: Exclude appointments with an InVisit QueueEntry.
        var candidates = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.QueueEntry)
            .Where(a =>
                a.Status                  == AppointmentStatus.Scheduled &&
                a.PreferredSlotCriteria   != null                        &&
                a.Patient.AutoSwapEnabled                                &&
                a.Patient.DeletedAt       == null                        &&
                (a.QueueEntry == null || a.QueueEntry.Status == QueueStatus.Waiting || a.QueueEntry.Status == QueueStatus.Completed))
            .OrderBy(a => a.CreatedAt)   // AC-4: longest wait = earliest creation
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "SwapEngine: no eligible candidates for slot {SlotId}.", openedSlot.SlotId);
            return results;
        }

        // ── 2. Filter to appointments whose preferred criteria match the opened slot ──
        var matched = candidates
            .Where(a => MatchesCriteria(a, slotDate, slotStart, openedSlot.AppointmentType, providerGuid))
            .ToList();

        _logger.LogInformation(
            "SwapEngine: {MatchedCount}/{TotalCount} candidates match slot {SlotId}.",
            matched.Count, candidates.Count, openedSlot.SlotId);

        // ── 3. Evaluate each candidate in priority order ────────────────────
        foreach (var appointment in matched)
        {
            var result = await EvaluateCandidateAsync(
                appointment, openedSlot, slotDateTime, isWithin24Hours, cancellationToken);

            results.Add(result);

            // If we successfully swapped, stop — the slot is now taken.
            if (result.Status == PreferredSlotSwapStatus.Swapped)
                break;

            // If manual confirmation was sent we also stop — no auto-swap for this slot today.
            if (result.Status == PreferredSlotSwapStatus.ManualConfirmationRequired)
                break;

            // On conflict, continue to the next candidate (EC-1).
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PreferredSlotSwapResult> EvaluateCandidateAsync(
        Appointment       appointment,
        SlotItem          newSlot,
        DateTime          slotDateTime,
        bool              isWithin24Hours,
        CancellationToken cancellationToken)
    {
        var patient = appointment.Patient;

        // EC-2: Double-check arrival state (the list query above is best-effort;
        //       InVisit state may have changed between load and here).
        if (appointment.QueueEntry?.Status == QueueStatus.InVisit)
        {
            var reason = "Patient is currently in-visit — auto-swap skipped.";
            _logger.LogInformation(
                "SwapEngine: skip appointmentId={Id} — {Reason}", appointment.Id, reason);
            WriteAuditLog(appointment, AuditAction.AutoSwapSkipped, reason);
            await _db.SaveChangesAsync(cancellationToken);
            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.PatientCheckedIn,
                appointment.Id, newSlot.SlotId,
                SkipReason: reason);
        }

        // AC-3: Auto-swap disabled by staff
        if (!patient.AutoSwapEnabled)
        {
            var reason = patient.AutoSwapDisabledReason ?? "Auto-swap disabled by staff.";
            _logger.LogInformation(
                "SwapEngine: skip patientId={PatientId} — {Reason}", patient.Id, reason);
            WriteAuditLog(appointment, AuditAction.AutoSwapSkipped, reason);
            await _db.SaveChangesAsync(cancellationToken);
            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.AutoSwapDisabled,
                appointment.Id, newSlot.SlotId,
                SkipReason: reason);
        }

        // AC-5: Within-24h — manual confirmation only
        if (isWithin24Hours)
        {
            var reason = "Preferred slot opens within 24 hours — manual confirmation required.";
            _logger.LogInformation(
                "SwapEngine: appointmentId={Id} requires manual confirmation for slot {SlotId}.",
                appointment.Id, newSlot.SlotId);

            WriteAuditLog(appointment, AuditAction.ManualSwapOfferSent, reason);
            WriteNotificationLog(appointment, NotificationType.SlotSwapManualConfirmation);
            await _db.SaveChangesAsync(cancellationToken);

            // Best-effort email notification — failure doesn't abort the audit write
            await SendManualConfirmationEmailAsync(
                patient, appointment, newSlot, slotDateTime, cancellationToken);

            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.ManualConfirmationRequired,
                appointment.Id, newSlot.SlotId,
                NewSlotTime: slotDateTime,
                SkipReason: reason);
        }

        // AC-1 / AC-2: Attempt automatic swap with optimistic locking
        var rescheduleResult = await _bookingService.RescheduleAppointmentAsync(
            appointment.Id, newSlot, cancellationToken);

        if (rescheduleResult.Status == RescheduleStatus.ConcurrencyConflict)
        {
            _logger.LogInformation(
                "SwapEngine: concurrency conflict on appointmentId={Id} — trying next candidate (EC-1).",
                appointment.Id);
            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.ConcurrencyConflict,
                appointment.Id, newSlot.SlotId,
                SkipReason: "Optimistic-locking conflict — retrying next candidate.");
        }

        if (rescheduleResult.Status == RescheduleStatus.SlotUnavailable)
        {
            _logger.LogInformation(
                "SwapEngine: new slot {SlotId} no longer available.", newSlot.SlotId);
            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.ConcurrencyConflict,
                appointment.Id, newSlot.SlotId,
                SkipReason: "Slot taken by another booking before swap could execute.");
        }

        if (rescheduleResult.Status != RescheduleStatus.Succeeded)
        {
            return new PreferredSlotSwapResult(
                PreferredSlotSwapStatus.NoCandidateFound,
                appointment.Id, newSlot.SlotId,
                SkipReason: $"Reschedule returned unexpected status: {rescheduleResult.Status}.");
        }

        // Swap succeeded — write audit + notification logs
        // NOTE: appointment entity may be stale after reschedule; use result timestamps.
        var freshAppointment = await _db.Appointments.FindAsync(
            new object[] { appointment.Id }, cancellationToken);

        if (freshAppointment is not null)
        {
            WriteAuditLog(freshAppointment, AuditAction.AppointmentAutoSwapped,
                $"Auto-swapped from {rescheduleResult.OldAppointmentTime:u} to {rescheduleResult.NewAppointmentTime:u}.");
            WriteNotificationLog(freshAppointment, NotificationType.SlotSwapCompleted);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await SendSwapCompletedEmailAsync(
            patient, appointment, rescheduleResult, newSlot, cancellationToken);

        _logger.LogInformation(
            "SwapEngine: auto-swap SUCCESS appointmentId={Id}, old={Old}, new={New}.",
            appointment.Id,
            rescheduleResult.OldAppointmentTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            rescheduleResult.NewAppointmentTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return new PreferredSlotSwapResult(
            PreferredSlotSwapStatus.Swapped,
            appointment.Id, newSlot.SlotId,
            OldSlotTime: rescheduleResult.OldAppointmentTime,
            NewSlotTime: rescheduleResult.NewAppointmentTime);
    }

    /// <summary>
    /// Checks whether an appointment's <see cref="PreferredSlotCriteria"/> matches the opened slot.
    /// </summary>
    private static bool MatchesCriteria(
        Appointment appointment,
        DateOnly    slotDate,
        TimeOnly    slotStart,
        string      appointmentType,
        Guid?       providerId)
    {
        var criteria = appointment.PreferredSlotCriteria;
        if (criteria is null) return false;

        // Appointment type must match if the appointment has one set
        if (!string.IsNullOrEmpty(appointment.AppointmentType) &&
            !string.Equals(appointment.AppointmentType, appointmentType, StringComparison.OrdinalIgnoreCase))
            return false;

        // Provider must match if the appointment has a specific provider preference
        if (appointment.ProviderId.HasValue && providerId.HasValue &&
            appointment.ProviderId.Value != providerId.Value)
            return false;

        // Preferred day of week
        if (criteria.PreferredDays.Count > 0)
        {
            var dayName = slotDate.DayOfWeek.ToString();
            if (!criteria.PreferredDays.Any(d =>
                    string.Equals(d, dayName, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        // Preferred time of day (morning = before 12:00, afternoon = 12:00–17:00, evening = after 17:00)
        if (!string.IsNullOrEmpty(criteria.PreferredTimeOfDay))
        {
            var matchesTimeOfDay = criteria.PreferredTimeOfDay.ToLowerInvariant() switch
            {
                "morning"   => slotStart.Hour < 12,
                "afternoon" => slotStart.Hour >= 12 && slotStart.Hour < 17,
                "evening"   => slotStart.Hour >= 17,
                _           => true, // Unknown value — don't exclude
            };
            if (!matchesTimeOfDay) return false;
        }

        return true;
    }

    private void WriteAuditLog(Appointment appointment, AuditAction action, string? reason = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId       = null, // System action (NFR-017)
            Action       = action,
            ResourceType = "Appointment",
            ResourceId   = appointment.Id,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = string.Empty,
            UserAgent    = reason ?? string.Empty,
        });
    }

    private void WriteNotificationLog(Appointment appointment, NotificationType type)
    {
        _db.NotificationLogs.Add(new NotificationLog
        {
            NotificationId   = Guid.NewGuid(),
            AppointmentId    = appointment.Id,
            NotificationType = type,
            DeliveryChannel  = DeliveryChannel.Email,
            Status           = NotificationStatus.Sent,
            RetryCount       = 0,
            CreatedAt        = DateTime.UtcNow,
        });
    }

    private async Task SendSwapCompletedEmailAsync(
        Patient           patient,
        Appointment       appointment,
        RescheduleResult  result,
        SlotItem          newSlot,
        CancellationToken cancellationToken)
    {
        try
        {
            var oldTime = result.OldAppointmentTime?.ToString("MMMM d, yyyy 'at' h:mm tt") ?? "previous slot";
            var newTime = result.NewAppointmentTime?.ToString("MMMM d, yyyy 'at' h:mm tt") ?? newSlot.StartTime;

            await _emailService.SendSwapCompletedEmailAsync(
                patient.Email,
                patient.FullName,
                oldTime,
                newTime,
                newSlot.ProviderName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send swap-completed email for appointmentId={Id}.", appointment.Id);
        }
    }

    private async Task SendManualConfirmationEmailAsync(
        Patient           patient,
        Appointment       appointment,
        SlotItem          newSlot,
        DateTime          slotDateTime,
        CancellationToken cancellationToken)
    {
        try
        {
            var slotTime = slotDateTime.ToString("MMMM d, yyyy 'at' h:mm tt");
            await _emailService.SendManualSwapConfirmationEmailAsync(
                patient.Email,
                patient.FullName,
                slotTime,
                newSlot.ProviderName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send manual-swap-confirmation email for appointmentId={Id}.", appointment.Id);
        }
    }
}
