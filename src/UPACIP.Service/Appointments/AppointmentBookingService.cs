using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements the appointment booking flow (US_018) with:
///
///   - Redis slot hold verification via <see cref="ISlotHoldService"/> (AC-3).
///   - Patient resolution from email (links ApplicationUser JWT identity → Patient.Id).
///   - DB-level slot availability check using composite index
///     <c>ix_appointments_appointment_time_status_provider_id</c> (US_017 TASK_003).
///   - EF Core optimistic concurrency: Appointment.Version is configured as
///     <c>IsConcurrencyToken()</c> — any stale UPDATE throws <see cref="DbUpdateConcurrencyException"/>.
///   - PostgreSQL unique-constraint conflict (23505) handling on concurrent inserts.
///   - Returns 3 alternative available slots on any conflict (AC-2).
///   - Booking reference generation: BK-{YYYYMMDD}-{6-char-alphanumeric} (AC-4).
///   - Redis slot cache invalidation on success.
///   - Polly single-retry (500 ms) for transient Npgsql failures; 503 on exhaustion (EC-1, NFR-032).
///   - Structured Serilog logging for all key booking events (NFR-035).
/// </summary>
public sealed class AppointmentBookingService : IAppointmentBookingService
{
    // PostgreSQL SQLSTATE for unique constraint violation (DR-014).
    private const string PgUniqueViolation = "23505";

    private readonly ApplicationDbContext            _db;
    private readonly ISlotHoldService                _holdService;
    private readonly IAppointmentSlotService         _slotService;
    private readonly ILogger<AppointmentBookingService> _logger;

    // Polly single-retry with 500 ms delay for transient Npgsql failures (EC-1, NFR-032).
    // Logs each attempt so ops can correlate retry bursts in Serilog output.
    private readonly IAsyncPolicy _retryPolicy;

    public AppointmentBookingService(
        ApplicationDbContext               db,
        ISlotHoldService                   holdService,
        IAppointmentSlotService            slotService,
        ILogger<AppointmentBookingService> logger)
    {
        _db          = db;
        _holdService = holdService;
        _slotService = slotService;
        _logger      = logger;

        _retryPolicy = Policy
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            .WaitAndRetryAsync(
                retryCount:            1,
                sleepDurationProvider: _ => TimeSpan.FromMilliseconds(500),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "Transient DB failure on booking attempt {Attempt}. Retrying in {DelayMs}ms.",
                        attempt, (int)delay.TotalMilliseconds));
    }

    /// <inheritdoc/>
    public async Task<BookingResult> BookAppointmentAsync(
        BookingRequest    request,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Resolve patient record from the JWT email claim ───────────────
        // Patient.Id differs from ApplicationUser.Id; the link is via matching email.
        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Email == userEmail && p.DeletedAt == null,
                cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "Booking attempt for email {Email} — no active Patient record found.",
                userEmail);
            return BookingResult.PatientNotFound();
        }

        var patientId = patient.Id;

        // ── 2. Verify the slot hold belongs to this user (AC-3) ─────────────
        var holdOwned = await _holdService.IsHeldByUserAsync(
            request.SlotId, userEmail, cancellationToken);

        if (!holdOwned)
        {
            _logger.LogWarning(
                "Booking denied — hold missing or expired: slot={SlotId}, patient={PatientId}.",
                request.SlotId, patientId);
            return BookingResult.HoldMismatch();
        }

        // ── 3. Execute core booking logic wrapped in Polly retry (EC-1) ──────
        try
        {
            return await _retryPolicy.ExecuteAsync(
                ct => PerformBookingAsync(request, patientId, userEmail, ct),
                cancellationToken);
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            // Retry exhausted — return 503 per EC-1 / NFR-032.
            _logger.LogError(ex,
                "Transient DB failure persists after retry: slot={SlotId}, patient={PatientId}.",
                request.SlotId, patientId);
            return BookingResult.Unavailable("Service temporarily unavailable. Please try again.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core booking logic (executed inside Polly retry scope)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<BookingResult> PerformBookingAsync(
        BookingRequest    request,
        Guid              patientId,        string            userEmail,        CancellationToken cancellationToken)
    {
        // Normalise to UTC so PostgreSQL timestamp comparisons are consistent.
        var appointmentTime = DateTime.SpecifyKind(request.AppointmentTime, DateTimeKind.Utc);

        // ── 4. Pre-insert slot availability check ────────────────────────────
        // Uses ix_appointments_appointment_time_status_provider_id (US_017 TASK_003).
        var slotTaken = await _db.Appointments
            .AsNoTracking()
            .AnyAsync(
                a => a.ProviderId    == request.ProviderId
                  && a.AppointmentTime == appointmentTime
                  && a.Status          != AppointmentStatus.Cancelled,
                cancellationToken);

        if (slotTaken)
        {
            _logger.LogInformation(
                "Slot conflict detected at pre-check: slot={SlotId}, provider={ProviderId}.",
                request.SlotId, request.ProviderId);

            var alternativesOnPreCheck =
                await FetchAlternativeSlotsAsync(request, cancellationToken);
            return BookingResult.Conflicted(alternativesOnPreCheck);
        }

        // ── 5. Resolve provider name (denormalised for fast display) ─────────
        // Provider name is stored in ProviderAvailabilityTemplate (US_017).
        var providerName = await _db.ProviderAvailabilityTemplates
            .AsNoTracking()
            .Where(t => t.ProviderId == request.ProviderId && t.IsActive)
            .Select(t => t.ProviderName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? "Unknown Provider";

        // ── 6. Create new appointment entity ─────────────────────────────────
        // Version = 0 is the initial concurrency token; EF Core increments it on
        // each subsequent UPDATE and includes it in the WHERE clause (FR-012, TR-015).
        var now = DateTime.UtcNow;
        var appointment = new Appointment
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            ProviderId      = request.ProviderId,
            ProviderName    = providerName,
            AppointmentTime = appointmentTime,
            AppointmentType = request.AppointmentType,
            Status          = AppointmentStatus.Scheduled,
            Version         = 0,
            CreatedAt       = now,
            UpdatedAt       = now,
        };

        _db.Appointments.Add(appointment);

        // ── 7. Persist with conflict handling ────────────────────────────────
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // EF Core Version mismatch — concurrent modification of the same row.
            _logger.LogWarning(ex,
                "Optimistic concurrency conflict: slot={SlotId}, patient={PatientId}.",
                request.SlotId, patientId);

            var alternativesOnConcurrency =
                await FetchAlternativeSlotsAsync(request, cancellationToken);
            return BookingResult.Conflicted(alternativesOnConcurrency);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // PostgreSQL 23505 — concurrent insert for the same slot.
            _logger.LogWarning(ex,
                "Unique constraint conflict on booking insert: slot={SlotId}, patient={PatientId}.",
                request.SlotId, patientId);

            var alternativesOnUnique =
                await FetchAlternativeSlotsAsync(request, cancellationToken);
            return BookingResult.Conflicted(alternativesOnUnique);
        }

        // ── 8. Generate and persist unique booking reference (AC-4) ─────────
        // Reference is stored on the appointment entity so it can be returned in the
        // patient's appointment list (US_019 GET /api/appointments).
        var bookingReference = GenerateBookingReference(appointment.CreatedAt);
        appointment.BookingReference = bookingReference;
        await _db.SaveChangesAsync(cancellationToken);

        // ── 9. Invalidate Redis slot cache for the affected date (US_017) ────
        await _slotService.InvalidateCacheAsync(
            DateOnly.FromDateTime(appointmentTime),
            request.ProviderId,
            cancellationToken);

        // ── 10. Release the slot hold (AC-3) ─────────────────────────────────
        await _holdService.ReleaseHoldAsync(request.SlotId, userEmail, cancellationToken);

        // ── 11. Audit log (NFR-035) ──────────────────────────────────────────
        _logger.LogInformation(
            "Booking confirmed: reference={BookingReference}, appointmentId={AppointmentId}, " +
            "patient={PatientId}, provider={ProviderId}, time={AppointmentTime}.",
            bookingReference, appointment.Id, patientId,
            request.ProviderId, appointmentTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return BookingResult.Succeeded(new BookingResponse(
            AppointmentId:   appointment.Id,
            BookingReference: bookingReference,
            AppointmentDate:  appointmentTime.ToString("yyyy-MM-dd"),
            AppointmentTime:  appointmentTime.ToString("HH:mm"),
            ProviderName:    providerName,
            AppointmentType: request.AppointmentType,
            Status:          "scheduled",
            CreatedAt:       new DateTimeOffset(appointment.CreatedAt, TimeSpan.Zero)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a booking reference in the format BK-{YYYYMMDD}-{6-char-uppercase-alphanumeric}.
    /// The 6-character suffix is derived from cryptographically random bytes for uniqueness (AC-4).
    /// </summary>
    private static string GenerateBookingReference(DateTime createdAt)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes  = RandomNumberGenerator.GetBytes(6);
        var suffix = new char[6];
        for (var i = 0; i < 6; i++)
            suffix[i] = Alphabet[bytes[i] % Alphabet.Length];
        return $"BK-{createdAt:yyyyMMdd}-{new string(suffix)}";
    }

    /// <summary>
    /// Fetches up to 3 alternative available slots when a conflict occurs (AC-2).
    /// Queries the 7-day window starting from the conflicted appointment's date.
    /// Swallows errors — alternative slots are best-effort for the conflict response.
    /// </summary>
    private async Task<IReadOnlyList<SlotItem>> FetchAlternativeSlotsAsync(
        BookingRequest    request,
        CancellationToken cancellationToken)
    {
        try
        {
            var startDate  = DateOnly.FromDateTime(request.AppointmentTime.Date);
            var endDate    = startDate.AddDays(7);
            var maxEndDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(90));
            if (endDate > maxEndDate) endDate = maxEndDate;

            var parameters = new SlotQueryParameters
            {
                StartDate       = startDate,
                EndDate         = endDate,
                ProviderId      = request.ProviderId,
                AppointmentType = request.AppointmentType,
            };

            var availability =
                await _slotService.GetAvailableSlotsAsync(parameters, cancellationToken);

            return availability.Slots
                .Where(s => s.Available)
                .Take(3)
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch alternative slots for conflict response — returning empty list.");
            return [];
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="ex"/> wraps a PostgreSQL 23505
    /// (unique_violation) error — indicating a concurrent insert collision.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var pgEx = ex.InnerException as PostgresException
                ?? ex.InnerException?.InnerException as PostgresException;
        return pgEx?.SqlState == PgUniqueViolation;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RescheduleAppointmentAsync (US_021 AC-1, EC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RescheduleResult> RescheduleAppointmentAsync(
        Guid              appointmentId,
        SlotItem          newSlot,
        CancellationToken cancellationToken = default)
    {
        var newTime = DateTime.SpecifyKind(
            DateTime.ParseExact(
                $"{newSlot.Date} {newSlot.StartTime}",
                "yyyy-MM-dd HH:mm",
                System.Globalization.CultureInfo.InvariantCulture),
            DateTimeKind.Utc);

        var newProviderId = Guid.TryParse(newSlot.ProviderId, out var pg) ? pg : (Guid?)null;

        // ── 1. Verify the new slot is still available ────────────────────────
        var slotTaken = await _db.Appointments
            .AsNoTracking()
            .AnyAsync(
                a => a.ProviderId      == newProviderId
                  && a.AppointmentTime == newTime
                  && a.Status          != AppointmentStatus.Cancelled,
                cancellationToken);

        if (slotTaken)
        {
            _logger.LogDebug(
                "Reschedule slot conflict: slot={SlotId} already taken.", newSlot.SlotId);
            return RescheduleResult.SlotTaken();
        }

        // ── 2. Load the appointment to move ─────────────────────────────────
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "RescheduleAppointmentAsync: appointmentId={Id} not found.", appointmentId);
            return RescheduleResult.NotFound();
        }

        var oldTime           = appointment.AppointmentTime;
        var oldProviderId     = appointment.ProviderId;

        // ── 3. Mutate the appointment row (Version is the optimistic-lock token) ──
        appointment.AppointmentTime = newTime;
        appointment.ProviderId      = newProviderId;
        appointment.ProviderName    = newSlot.ProviderName;
        appointment.AppointmentType = newSlot.AppointmentType;
        appointment.UpdatedAt       = DateTime.UtcNow;
        // Do NOT manually increment Version — EF Core handles it with IsConcurrencyToken().

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex,
                "Optimistic concurrency conflict on reschedule: appointmentId={Id}.", appointmentId);
            return RescheduleResult.Conflict();
        }

        // ── 4. Invalidate caches for both old and new slots ─────────────────
        var oldDate = DateOnly.FromDateTime(oldTime);
        var newDate = DateOnly.FromDateTime(newTime);

        await _slotService.InvalidateCacheAsync(oldDate, oldProviderId, cancellationToken);

        if (newDate != oldDate || newProviderId != oldProviderId)
            await _slotService.InvalidateCacheAsync(newDate, newProviderId, cancellationToken);

        _logger.LogInformation(
            "Appointment rescheduled: appointmentId={Id}, from={OldTime}, to={NewTime}, slot={SlotId}.",
            appointmentId,
            oldTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            newTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            newSlot.SlotId);

        return RescheduleResult.Succeeded(oldTime, newTime);
    }
}
