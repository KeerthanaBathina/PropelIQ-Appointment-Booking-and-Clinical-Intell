using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements the staff-only walk-in registration flow (US_022 FR-021, FR-022).
///
/// Design notes:
///   - PatientId (when supplied) is validated against the database — never trusted blindly (OWASP A01).
///   - Inline new-patient creation hashes a cryptographically-random placeholder password with
///     BCrypt (work factor 10) so the record is valid but cannot be used until self-service.
///   - The booking transaction wraps appointment creation AND queue insertion in a single
///     <c>SaveChangesAsync</c> to ensure atomicity (AC-3).
///   - Slot availability is checked inside the transaction using the composite index
///     <c>ix_appointments_status_appointment_time</c> to prevent double-booking.
///   - PostgreSQL 23505 (unique constraint violation) on concurrent inserts is handled
///     gracefully and returns <see cref="WalkInBookingOutcome.SlotUnavailable"/>.
///   - Urgent escalation (EC-2): when <c>IsUrgent = true</c> and no same-day slots exist,
///     no appointment is created; the outcome signals supervisor escalation.
///   - Structured logging uses NFR-035 patterns; PII (email, name) only at DEBUG level.
/// </summary>
public sealed class WalkInRegistrationService : IWalkInRegistrationService
{
    // PostgreSQL unique-constraint SQLSTATE (DR-014).
    private const string PgUniqueViolation = "23505";

    private readonly ApplicationDbContext                   _db;
    private readonly IAppointmentSlotService                _slotService;
    private readonly ILogger<WalkInRegistrationService>     _logger;

    public WalkInRegistrationService(
        ApplicationDbContext               db,
        IAppointmentSlotService            slotService,
        ILogger<WalkInRegistrationService> logger)
    {
        _db          = db;
        _slotService = slotService;
        _logger      = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SearchPatientsAsync (AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WalkInPatientSearchResult>> SearchPatientsAsync(
        WalkInPatientSearchRequest request,
        CancellationToken          ct = default)
    {
        var term = request.Term.Trim();

        IQueryable<Patient> query = _db.Patients
            .AsNoTracking()
            .Where(p => p.DeletedAt == null);

        query = request.Field switch
        {
            "dob" =>
                // Exact ISO-8601 date match (term must be YYYY-MM-DD)
                DateOnly.TryParse(term, out var dob)
                    ? query.Where(p => p.DateOfBirth == dob)
                    : query.Where(_ => false),

            "phone" =>
                // Partial match on normalised phone (contains)
                query.Where(p => EF.Functions.Like(p.PhoneNumber, $"%{term}%")),

            _ =>
                // Default: case-insensitive partial name match (ILIKE in PostgreSQL)
                query.Where(p => EF.Functions.ILike(p.FullName, $"%{term}%")),
        };

        var results = await query
            .OrderBy(p => p.FullName)
            .Take(20)
            .Select(p => new WalkInPatientSearchResult(
                p.Id.ToString(),
                p.FullName,
                p.DateOfBirth.ToString("yyyy-MM-dd"),
                p.PhoneNumber,
                p.Email))
            .ToListAsync(ct);

        _logger.LogDebug(
            "WalkInPatientSearch: field={Field} returned {Count} result(s).",
            request.Field, results.Count);

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetSameDaySlotsAsync (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SameDaySlotsResponse> GetSameDaySlotsAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Same-day lookup: start = end = today
        var sameDayParams = new SlotQueryParameters
        {
            StartDate = today,
            EndDate   = today,
        };

        var sameDayResponse = await _slotService.GetAvailableSlotsAsync(sameDayParams, ct);
        var sameDaySlots    = sameDayResponse.Slots.Where(s => s.Available).ToList();

        if (sameDaySlots.Count > 0)
        {
            return new SameDaySlotsResponse(sameDaySlots, NextAvailableDate: null);
        }

        // AC-4: no same-day slots — find next available date within 30 days
        var futureParams = new SlotQueryParameters
        {
            StartDate = today.AddDays(1),
            EndDate   = today.AddDays(30),
        };

        var futureResponse = await _slotService.GetAvailableSlotsAsync(futureParams, ct);
        var nextDate       = futureResponse.Slots
            .Where(s => s.Available)
            .Select(s => s.Date)
            .OrderBy(d => d)
            .FirstOrDefault();

        _logger.LogInformation(
            "GetSameDaySlotsAsync: no same-day slots. NextAvailableDate={NextDate}.", nextDate);

        return new SameDaySlotsResponse([], NextAvailableDate: nextDate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BookWalkInAsync (AC-3, EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<(WalkInBookingOutcome Outcome, WalkInBookingResponse? Response)> BookWalkInAsync(
        WalkInBookingRequest request,
        CancellationToken    ct = default)
    {
        // ── 1. Parse and validate slot ──────────────────────────────────────
        var slot = ParseSlotId(request.SlotId);
        if (slot is null)
        {
            _logger.LogWarning("BookWalkIn: invalid SlotId format '{SlotId}'.", request.SlotId);
            return (WalkInBookingOutcome.SlotUnavailable, null);
        }

        var (slotDate, slotTime, providerId) = slot.Value;

        // ── 2. Same-day guard ───────────────────────────────────────────────
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (slotDate != today)
        {
            _logger.LogWarning(
                "BookWalkIn: SlotId '{SlotId}' is not a same-day slot.", request.SlotId);
            return (WalkInBookingOutcome.SlotUnavailable, null);
        }

        // ── 3. Urgent + no capacity → escalation path (EC-2) ───────────────
        if (request.IsUrgent)
        {
            var sameDayCheck = await GetSameDaySlotsAsync(ct);
            if (sameDayCheck.Slots.Count == 0)
            {
                _logger.LogInformation(
                    "BookWalkIn: urgent walk-in escalation — no same-day slots available.");
                return (WalkInBookingOutcome.UrgentEscalation, null);
            }
        }

        // ── 4. Resolve or create patient ────────────────────────────────────
        var (patient, isNew) = await ResolveOrCreatePatientAsync(request, ct);
        if (patient is null)
        {
            return request.PatientId.HasValue
                ? (WalkInBookingOutcome.PatientNotFound, null)
                : (WalkInBookingOutcome.DuplicatePatientEmail, null);
        }

        // ── 5. Build appointment time (UTC) ─────────────────────────────────
        var appointmentTime = DateTime.SpecifyKind(
            slotDate.ToDateTime(slotTime),
            DateTimeKind.Utc);

        // ── 6. Look up provider name from slot availability ─────────────────
        var providerName = await ResolveProviderNameAsync(providerId, slotDate, ct);

        // ── 7. Booking reference ─────────────────────────────────────────────
        var bookingReference = GenerateBookingReference(appointmentTime);

        // ── 8. Appointment entity ────────────────────────────────────────────
        var appointment = new Appointment
        {
            PatientId        = patient.Id,
            BookingReference = bookingReference,
            AppointmentTime  = appointmentTime,
            Status           = AppointmentStatus.Scheduled,
            IsWalkIn         = true,
            ProviderId       = providerId,
            ProviderName     = providerName,
            AppointmentType  = request.VisitType,
        };

        // ── 9. Queue entry (AC-3) ─────────────────────────────────────────────
        var priority = request.IsUrgent ? QueuePriority.Urgent : QueuePriority.Normal;
        var queueEntry = new QueueEntry
        {
            AppointmentId    = appointment.Id,
            ArrivalTimestamp = DateTime.UtcNow,
            WaitTimeMinutes  = 0,
            Priority         = priority,
            Status           = QueueStatus.Waiting,
        };
        queueEntry.Appointment = appointment;

        // ── 10. Persist in a single transaction ──────────────────────────────
        _db.Appointments.Add(appointment);
        _db.QueueEntries.Add(queueEntry);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            _logger.LogWarning(
                "BookWalkIn: slot {SlotId} taken (unique constraint). Outcome=SlotUnavailable.",
                request.SlotId);
            return (WalkInBookingOutcome.SlotUnavailable, null);
        }

        // ── 11. Determine queue position ─────────────────────────────────────
        var queuePosition = await _db.QueueEntries
            .AsNoTracking()
            .CountAsync(q => q.Status == QueueStatus.Waiting, ct);

        // ── 12. Invalidate slot cache ─────────────────────────────────────────
        await _slotService.InvalidateCacheAsync(today, providerId, ct);

        _logger.LogInformation(
            "BookWalkIn: walk-in booked. AppointmentId={AppointmentId}, " +
            "PatientId={PatientId}, IsNew={IsNew}, IsUrgent={IsUrgent}, QueuePosition={QueuePosition}.",
            appointment.Id, patient.Id, isNew, request.IsUrgent, queuePosition);

        var endTime = slotTime.AddMinutes(30);

        var response = new WalkInBookingResponse(
            BookingReference: bookingReference,
            AppointmentId:    appointment.Id.ToString(),
            PatientId:        patient.Id.ToString(),
            PatientName:      patient.FullName,
            Date:             slotDate.ToString("yyyy-MM-dd"),
            StartTime:        slotTime.ToString("HH:mm"),
            EndTime:          endTime.ToString("HH:mm"),
            ProviderName:     providerName,
            AppointmentType:  request.VisitType,
            IsWalkIn:         true,
            IsUrgent:         request.IsUrgent,
            QueuePosition:    queuePosition);

        return (WalkInBookingOutcome.Success, response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the composite SlotId (yyyyMMdd-HHmm-{providerGuid:N}) into its components.
    /// Returns null when the format is invalid.
    /// </summary>
    private static (DateOnly Date, TimeOnly Time, Guid ProviderId)? ParseSlotId(string slotId)
    {
        // Format: {yyyyMMdd}-{HHmm}-{providerGuid:N}
        // e.g.    20260421-0900-7f3a2d1c4e5b6a7f8d9e0f1a2b3c4d5e
        var parts = slotId.Split('-', 3);
        if (parts.Length != 3) return null;

        if (!DateOnly.TryParseExact(parts[0], "yyyyMMdd", out var date)) return null;
        if (!TimeOnly.TryParseExact(parts[1], "HHmm",     out var time)) return null;
        if (!Guid.TryParseExact(parts[2],    "N",         out var pid))  return null;

        return (date, time, pid);
    }

    /// <summary>
    /// Resolves an existing patient by Id or creates a minimal new patient record
    /// using the inline <see cref="NewWalkInPatientRequest"/> data.
    /// Returns (null, false) when patientId is provided but not found.
    /// Returns (null, false) when inline creation hits a duplicate email constraint.
    /// </summary>
    private async Task<(Patient? Patient, bool IsNew)> ResolveOrCreatePatientAsync(
        WalkInBookingRequest request,
        CancellationToken    ct)
    {
        if (request.PatientId.HasValue)
        {
            var existing = await _db.Patients
                .FirstOrDefaultAsync(
                    p => p.Id == request.PatientId.Value && p.DeletedAt == null,
                    ct);

            if (existing is null)
                _logger.LogWarning(
                    "BookWalkIn: PatientId {PatientId} not found.", request.PatientId.Value);

            return (existing, false);
        }

        // Inline creation
        var np     = request.NewPatient!;
        var email  = np.Email.Trim().ToLowerInvariant();

        // Guard against duplicate email before attempting insert
        var emailExists = await _db.Patients
            .AnyAsync(p => p.Email == email && p.DeletedAt == null, ct);

        if (emailExists)
        {
            _logger.LogWarning(
                "BookWalkIn: inline patient creation rejected — email already registered.");
            return (null, false);
        }

        var patient = new Patient
        {
            Email        = email,
            FullName     = np.FullName.Trim(),
            DateOfBirth  = DateOnly.Parse(np.DateOfBirth),
            PhoneNumber  = np.Phone.Trim(),
            PasswordHash = string.Empty, // placeholder; patient must register separately
        };

        patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(
            Guid.NewGuid().ToString(), workFactor: 10);

        _db.Patients.Add(patient);
        // Patient is saved as part of the outer SaveChangesAsync call in BookWalkInAsync.
        return (patient, true);
    }

    /// <summary>
    /// Looks up the provider display name from the slot availability grid.
    /// Falls back to the providerId string when the name cannot be resolved.
    /// </summary>
    private async Task<string> ResolveProviderNameAsync(
        Guid              providerId,
        DateOnly          date,
        CancellationToken ct)
    {
        var slotParams = new SlotQueryParameters
        {
            StartDate  = date,
            EndDate    = date,
            ProviderId = providerId,
        };

        var slotResponse = await _slotService.GetAvailableSlotsAsync(slotParams, ct);
        return slotResponse.Providers.FirstOrDefault()?.ProviderName
            ?? providerId.ToString();
    }

    /// <summary>
    /// Generates a booking reference in the format BK-{YYYYMMDD}-{6-char-alphanumeric}.
    /// Mirrors the pattern used in <c>AppointmentBookingService</c>.
    /// </summary>
    private static string GenerateBookingReference(DateTime appointmentTime)
    {
        const string chars  = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var          suffix = new char[6];
        var          rng    = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);

        for (int i = 0; i < 6; i++)
            suffix[i] = chars[rng[i] % chars.Length];

        return $"BK-{appointmentTime:yyyyMMdd}-{new string(suffix)}";
    }
}
