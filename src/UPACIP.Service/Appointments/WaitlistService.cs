using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements waitlist registration, offer dispatch, and claim-link redemption (US_020).
///
/// Design notes:
///   - PatientId is always resolved from JWT email — never trusted from the request body (OWASP A01).
///   - Claim tokens are 48-byte CSPRNG values, Base64Url-encoded — 64 chars max, URL-safe, no collisions.
///   - Duplicate-active detection uses exact criteria comparison (date + provider + appointment type).
///     A patient may register for the same date with a different provider as a separate entry.
///   - The offer hold TTL is 60 seconds, consistent with the booking hold (AC-3, US_018).
///   - EC-1: This service is NEVER called from appointment cancellation flows.
///     Only <see cref="WaitlistOfferProcessor"/> and patient-initiated endpoints mutate entries.
///   - NFR-017 (PII in logs): email addresses are logged only at DEBUG; patient names never logged.
/// </summary>
public sealed class WaitlistService : IWaitlistService
{
    private static readonly TimeSpan ClaimHoldTtl = TimeSpan.FromSeconds(60); // AC-3

    private readonly ApplicationDbContext         _db;
    private readonly IEmailService                _emailService;
    private readonly ISlotHoldService             _holdService;
    private readonly IConfiguration               _configuration;
    private readonly ILogger<WaitlistService>     _logger;

    public WaitlistService(
        ApplicationDbContext         db,
        IEmailService                emailService,
        ISlotHoldService             holdService,
        IConfiguration               configuration,
        ILogger<WaitlistService>     logger)
    {
        _db            = db;
        _emailService  = emailService;
        _holdService   = holdService;
        _configuration = configuration;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RegisterAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JoinWaitlistResponse?> RegisterAsync(
        string               userEmail,
        JoinWaitlistRequest  request,
        CancellationToken    cancellationToken = default)
    {
        var (patient, user) = await ResolvePatientAsync(userEmail, cancellationToken);
        if (patient is null || user is null)
        {
            _logger.LogWarning("WaitlistService.RegisterAsync: patient not found for email.");
            return null;
        }

        var preferredDate      = DateOnly.Parse(request.PreferredDate);
        var preferredStartTime = TimeOnly.Parse(request.PreferredTimeStart);
        var preferredEndTime   = TimeOnly.Parse(request.PreferredTimeEnd);
        var providerId         = request.PreferredProviderId is not null
            ? Guid.Parse(request.PreferredProviderId)
            : (Guid?)null;

        // Duplicate-active guard — same criteria for same patient (AC-1).
        var existing = await _db.WaitlistEntries
            .Where(w =>
                w.PatientId       == patient.Id             &&
                w.Status          == WaitlistStatus.Active  &&
                w.PreferredDate   == preferredDate          &&
                w.AppointmentType == request.AppointmentType &&
                w.PreferredProviderId == providerId)
            .AnyAsync(cancellationToken);

        if (existing)
        {
            _logger.LogDebug(
                "Duplicate waitlist entry for patientId={PatientId}, date={Date}.",
                patient.Id, request.PreferredDate);
            return null; // Caller maps this to 409
        }

        var entry = new WaitlistEntry
        {
            PatientId            = patient.Id,
            PreferredDate        = preferredDate,
            PreferredStartTime   = preferredStartTime,
            PreferredEndTime     = preferredEndTime,
            PreferredProviderId  = providerId,
            AppointmentType      = request.AppointmentType,
            Status               = WaitlistStatus.Active,
            CreatedAt            = DateTime.UtcNow,
            UpdatedAt            = DateTime.UtcNow,
        };

        _db.WaitlistEntries.Add(entry);

        // Audit trail (NFR-012)
        _db.AuditLogs.Add(new AuditLog
        {
            UserId       = user.Id,
            Action       = AuditAction.WaitlistRegistered,
            ResourceType = "WaitlistEntry",
            ResourceId   = entry.Id,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = string.Empty,
            UserAgent    = string.Empty,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Waitlist entry registered: entryId={EntryId}, patientId={PatientId}, date={Date}.",
            entry.Id, patient.Id, request.PreferredDate);

        return new JoinWaitlistResponse(
            WaitlistId:          entry.Id,
            PreferredDate:       entry.PreferredDate.ToString("yyyy-MM-dd"),
            PreferredTimeStart:  entry.PreferredStartTime.ToString("HH:mm"),
            PreferredTimeEnd:    entry.PreferredEndTime.ToString("HH:mm"),
            PreferredProviderName: request.PreferredProviderName,
            AppointmentType:     entry.AppointmentType,
            RegisteredAt:        new DateTimeOffset(entry.CreatedAt, TimeSpan.Zero));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ClaimOfferAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ClaimWaitlistOfferResult> ClaimOfferAsync(
        string            claimToken,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        // Sanitise token (no PII logged) — avoid log injection.
        if (string.IsNullOrWhiteSpace(claimToken) || claimToken.Length > 64)
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.NotFound);

        var entry = await _db.WaitlistEntries
            .Include(w => w.Patient)
            .FirstOrDefaultAsync(w => w.ClaimToken == claimToken, cancellationToken);

        if (entry is null)
        {
            _logger.LogDebug("ClaimOffer: token not found.");
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.NotFound);
        }

        // Ownership check (OWASP A01 IDOR prevention)
        var (patient, user) = await ResolvePatientAsync(userEmail, cancellationToken);
        if (patient is null || patient.Id != entry.PatientId)
        {
            _logger.LogWarning("ClaimOffer: ownership mismatch for entry {EntryId}.", entry.Id);
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.NotFound);
        }

        // Already-claimed idempotency
        if (entry.Status == WaitlistStatus.Claimed || entry.Status == WaitlistStatus.Booked)
        {
            _logger.LogDebug("ClaimOffer: entry {EntryId} already claimed — idempotent return.", entry.Id);
            return BuildClaimResponse(entry, userEmail);
        }

        // Expiry check
        if (entry.ClaimExpiresAtUtc is null || DateTime.UtcNow > entry.ClaimExpiresAtUtc.Value)
        {
            _logger.LogDebug("ClaimOffer: entry {EntryId} expired.", entry.Id);
            entry.Status    = WaitlistStatus.Expired;
            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.Expired);
        }

        if (entry.OfferedSlotId is null)
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.NotFound);

        // Acquire hold on behalf of the patient (AC-3)
        var holdAcquired = await _holdService.AcquireHoldAsync(
            entry.OfferedSlotId, userEmail, cancellationToken);

        if (!holdAcquired)
        {
            // Another patient claimed the slot — mark as expired and surface error
            _logger.LogInformation(
                "ClaimOffer: slot {SlotId} hold denied for entry {EntryId} — taken by another patient.",
                entry.OfferedSlotId, entry.Id);
            entry.Status    = WaitlistStatus.Expired;
            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.Expired);
        }

        // Mark as claimed
        entry.Status       = WaitlistStatus.Claimed;
        entry.ClaimedAtUtc = DateTime.UtcNow;
        entry.UpdatedAt    = DateTime.UtcNow;

        if (user is not null)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId       = user.Id,
                Action       = AuditAction.WaitlistClaimed,
                ResourceType = "WaitlistEntry",
                ResourceId   = entry.Id,
                Timestamp    = DateTime.UtcNow,
                IpAddress    = string.Empty,
                UserAgent    = string.Empty,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Waitlist claim redeemed: entryId={EntryId}, slotId={SlotId}.",
            entry.Id, entry.OfferedSlotId);

        return BuildClaimResponse(entry, userEmail);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DispatchOffersForSlotAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task DispatchOffersForSlotAsync(
        SlotItem          openedSlot,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParse(openedSlot.Date, out var slotDate))
        {
            _logger.LogWarning("DispatchOffersForSlotAsync: invalid slot date {Date}.", openedSlot.Date);
            return;
        }

        if (!TimeOnly.TryParse(openedSlot.StartTime, out var slotStart))
        {
            _logger.LogWarning("DispatchOffersForSlotAsync: invalid slot start time {Time}.", openedSlot.StartTime);
            return;
        }

        var providerGuid = Guid.TryParse(openedSlot.ProviderId, out var pg) ? pg : (Guid?)null;

        // Find all Active entries where criteria match (AC-4 — notify all, first-confirm-wins)
        var candidates = await _db.WaitlistEntries
            .Include(w => w.Patient)
            .Where(w =>
                w.Status        == WaitlistStatus.Active   &&
                w.PreferredDate == slotDate                &&
                slotStart >= w.PreferredStartTime          &&
                slotStart <  w.PreferredEndTime            &&
                w.AppointmentType == openedSlot.AppointmentType &&
                (w.PreferredProviderId == null || w.PreferredProviderId == providerGuid))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "DispatchOffersForSlotAsync: no waitlist candidates for slot {SlotId}.",
                openedSlot.SlotId);
            return;
        }

        var now           = DateTime.UtcNow;
        var isWithin24Hrs = openedSlot.Date != null &&
            (new DateTime(slotDate.Year, slotDate.Month, slotDate.Day,
                slotStart.Hour, slotStart.Minute, 0, DateTimeKind.Utc) - now).TotalHours < 24;

        var frontendBase = (_configuration["AppSettings:FrontendBaseUrl"] ?? "http://localhost:3000")
            .TrimEnd('/');

        var appointmentDetails = $"{slotDate:MMMM d, yyyy} at {slotStart:h:mm tt} with {openedSlot.ProviderName}";

        _logger.LogInformation(
            "Dispatching waitlist offers for slot {SlotId} to {Count} candidate(s).",
            openedSlot.SlotId, candidates.Count);

        foreach (var entry in candidates)
        {
            await DispatchSingleOfferAsync(
                entry, openedSlot, now, isWithin24Hrs,
                frontendBase, appointmentDetails, cancellationToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RemoveEntryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> RemoveEntryAsync(
        Guid              waitlistId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var (patient, user) = await ResolvePatientAsync(userEmail, cancellationToken);
        if (patient is null) return false;

        var entry = await _db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == waitlistId && w.PatientId == patient.Id, cancellationToken);

        if (entry is null) return false;

        entry.Status    = WaitlistStatus.Removed;
        entry.UpdatedAt = DateTime.UtcNow;

        if (user is not null)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId       = user.Id,
                Action       = AuditAction.WaitlistRemoved,
                ResourceType = "WaitlistEntry",
                ResourceId   = entry.Id,
                Timestamp    = DateTime.UtcNow,
                IpAddress    = string.Empty,
                UserAgent    = string.Empty,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Waitlist entry removed: entryId={EntryId}, patientId={PatientId}.", entry.Id, patient.Id);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DispatchSingleOfferAsync(
        WaitlistEntry     entry,
        SlotItem          slot,
        DateTime          now,
        bool              isWithin24Hours,
        string            frontendBase,
        string            appointmentDetails,
        CancellationToken cancellationToken)
    {
        // Generate a cryptographically secure claim token (OWASP A07, NFR-013)
        var tokenBytes  = RandomNumberGenerator.GetBytes(48);
        var claimToken  = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // URL-safe Base64

        entry.ClaimToken        = claimToken;
        entry.OfferedSlotId     = slot.SlotId;
        entry.OfferedAtUtc      = now;
        entry.ClaimExpiresAtUtc = now.Add(ClaimHoldTtl);
        entry.LastNotifiedAtUtc = now;
        entry.Status            = WaitlistStatus.Offered;
        entry.UpdatedAt         = now;

        var claimLink = $"{frontendBase}/book?claim={Uri.EscapeDataString(claimToken)}";

        _db.AuditLogs.Add(new AuditLog
        {
            UserId       = null, // System action (NFR-017)
            Action       = AuditAction.WaitlistOfferDispatched,
            ResourceType = "WaitlistEntry",
            ResourceId   = entry.Id,
            Timestamp    = now,
            IpAddress    = string.Empty,
            UserAgent    = string.Empty,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Dispatch email (failures are caught per-patient so one bad address doesn't halt the batch)
        try
        {
            await _emailService.SendWaitlistOfferEmailAsync(
                entry.Patient.Email,
                entry.Patient.FullName,
                claimLink,
                appointmentDetails,
                isWithin24Hours,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send waitlist offer email for entryId={EntryId}. Offer token persisted.",
                entry.Id);
        }
    }

    private ClaimWaitlistOfferResult BuildClaimResponse(
        WaitlistEntry     entry,
        string            userEmail)
    {
        if (entry.OfferedSlotId is null)
            return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.NotFound);

        // Parse slot details from the composite slot ID (format: {yyyyMMdd}-{HHmm}-{providerGuid:N})
        var parts = entry.OfferedSlotId.Split('-', 3);
        var date      = parts.Length > 0 ? $"{parts[0][..4]}-{parts[0][4..6]}-{parts[0][6..8]}" : entry.PreferredDate.ToString("yyyy-MM-dd");
        var startTime = parts.Length > 1 ? $"{parts[1][..2]}:{parts[1][2..4]}" : entry.PreferredStartTime.ToString("HH:mm");
        var endHour   = parts.Length > 1 ? int.Parse(parts[1][..2]) : entry.PreferredStartTime.Hour;
        var endMin    = parts.Length > 1 ? int.Parse(parts[1][2..4]) + 30 : entry.PreferredStartTime.Minute + 30;
        if (endMin >= 60) { endHour++; endMin -= 60; }
        var endTime = $"{endHour:D2}:{endMin:D2}";

        var isWithin24Hrs = (entry.OfferedAtUtc.HasValue &&
            (DateTime.UtcNow - entry.OfferedAtUtc.Value).TotalHours > 0) ||
            ((new DateTime(
                entry.PreferredDate.Year, entry.PreferredDate.Month, entry.PreferredDate.Day,
                entry.PreferredStartTime.Hour, entry.PreferredStartTime.Minute, 0, DateTimeKind.Utc)
              - DateTime.UtcNow).TotalHours < 24);

        var holdAcquiredAt = entry.ClaimedAtUtc.HasValue
            ? new DateTimeOffset(entry.ClaimedAtUtc.Value, TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var providerName = entry.Patient?.FullName ?? string.Empty; // placeholder — real provider resolved from slot

        var slot = new ClaimedSlot(
            SlotId:          entry.OfferedSlotId,
            Date:            date,
            StartTime:       startTime,
            EndTime:         endTime,
            ProviderName:    providerName,
            ProviderId:      entry.PreferredProviderId?.ToString("N") ?? string.Empty,
            AppointmentType: entry.AppointmentType,
            Available:       true);

        var response = new ClaimWaitlistOfferResponse(
            Slot:            slot,
            IsWithin24Hours: isWithin24Hrs,
            HoldAcquiredAt:  holdAcquiredAt,
            ProviderName:    providerName);

        return new ClaimWaitlistOfferResult(ClaimWaitlistOfferStatus.Success, response);
    }

    private async Task<(Patient? patient, ApplicationUser? user)> ResolvePatientAsync(
        string            userEmail,
        CancellationToken cancellationToken)
    {
        var patient = await _db.Patients
            .FirstOrDefaultAsync(p => p.Email == userEmail && p.DeletedAt == null, cancellationToken);

        if (patient is null) return (null, null);

        var user = await _db.Users
            .Where(u => u.NormalizedEmail == userEmail.ToUpperInvariant())
            .FirstOrDefaultAsync(cancellationToken);

        return (patient, user);
    }
}
