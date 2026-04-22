using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Notifications;

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
    /// <summary>Slot-hold TTL — how long the patient has to complete booking after claiming (AC-3, US_018).</summary>
    private static readonly TimeSpan ClaimHoldTtl     = TimeSpan.FromSeconds(60);

    /// <summary>Offer TTL — how long the patient has to claim the offer before the next candidate is advanced (AC-4).</summary>
    private static readonly TimeSpan OfferExpiryWindow = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext                     _db;
    private readonly IEmailService                            _emailService;
    private readonly IWaitlistOfferNotificationService        _offerNotificationService;
    private readonly ISlotHoldService                         _holdService;
    private readonly IConfiguration                           _configuration;
    private readonly ILogger<WaitlistService>                 _logger;

    public WaitlistService(
        ApplicationDbContext                     db,
        IEmailService                            emailService,
        IWaitlistOfferNotificationService        offerNotificationService,
        ISlotHoldService                         holdService,
        IConfiguration                           configuration,
        ILogger<WaitlistService>                 logger)
    {
        _db                       = db;
        _emailService             = emailService;
        _offerNotificationService = offerNotificationService;
        _holdService              = holdService;
        _configuration            = configuration;
        _logger                   = logger;
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

        // Select Active candidates in FIFO order (AC-3 — registration time ascending).
        // Only one candidate is notified at a time; if they have invalid contact details
        // the offer is skipped and the next candidate in order is tried (EC-1).
        var candidates = await _db.WaitlistEntries
            .Include(w => w.Patient)
            .Where(w =>
                w.Status        == WaitlistStatus.Active   &&
                w.PreferredDate == slotDate                &&
                slotStart >= w.PreferredStartTime          &&
                slotStart <  w.PreferredEndTime            &&
                w.AppointmentType == openedSlot.AppointmentType &&
                (w.PreferredProviderId == null || w.PreferredProviderId == providerGuid))
            .OrderBy(w => w.CreatedAt)  // FIFO: oldest registration first (AC-3)
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
            "DispatchOffersForSlotAsync: {Count} FIFO candidate(s) for slot {SlotId}.",
            candidates.Count, openedSlot.SlotId);

        // Advance through candidates in FIFO order until one successfully receives the offer
        // or all are exhausted (EC-1: skip invalid-contact patients).
        foreach (var entry in candidates)
        {
            var dispatched = await DispatchSingleOfferAsync(
                entry, openedSlot, now, isWithin24Hrs,
                frontendBase, appointmentDetails, cancellationToken);

            if (dispatched)
                return; // Offer sent to one candidate — first-confirm-wins (EC-2)

            // Invalid contact — continue to next candidate in FIFO order (EC-1)
        }

        _logger.LogWarning(
            "DispatchOffersForSlotAsync: all {Count} candidate(s) for slot {SlotId} had invalid contact details.",
            candidates.Count, openedSlot.SlotId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AdvanceExpiredOffersAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AdvanceExpiredOffersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;

            // Find all Offered entries whose 24-hour window has elapsed (AC-4)
            var expiredEntries = await _db.WaitlistEntries
                .Include(w => w.Patient)
                .Where(w =>
                    w.Status == WaitlistStatus.Offered &&
                    w.ClaimExpiresAtUtc != null        &&
                    w.ClaimExpiresAtUtc < now)
                .ToListAsync(cancellationToken);

            if (expiredEntries.Count == 0)
                return;

            _logger.LogInformation(
                "AdvanceExpiredOffersAsync: {Count} expired offer(s) to advance.",
                expiredEntries.Count);

            foreach (var entry in expiredEntries)
            {
                // Mark this entry as expired
                entry.Status    = WaitlistStatus.Expired;
                entry.UpdatedAt = now;

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId       = null,
                    Action       = AuditAction.WaitlistOfferDispatched, // reuse closest action for expiry
                    ResourceType = "WaitlistEntry",
                    ResourceId   = entry.Id,
                    Timestamp    = now,
                    IpAddress    = string.Empty,
                    UserAgent    = string.Empty,
                });

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "AdvanceExpiredOffersAsync: entry {EntryId} expired (slot {SlotId}). Advancing to next candidate.",
                    entry.Id, entry.OfferedSlotId);

                // Re-dispatch to the next FIFO candidate for the same slot criteria
                if (entry.OfferedSlotId is not null &&
                    entry.PreferredProviderId.HasValue)
                {
                    // Reconstruct a minimal SlotItem from stored entry data
                    var providerId   = entry.PreferredProviderId.Value;
                    var providerName = await _db.Users
                        .Where(u => u.Id == providerId)
                        .Select(u => u.FullName)
                        .FirstOrDefaultAsync(cancellationToken)
                        ?? "your selected provider";

                    var slot = new SlotItem(
                        SlotId:          entry.OfferedSlotId,
                        Date:            entry.PreferredDate.ToString("yyyy-MM-dd"),
                        StartTime:       entry.PreferredStartTime.ToString("HH:mm"),
                        EndTime:         entry.PreferredEndTime.ToString("HH:mm"),
                        ProviderName:    providerName,
                        ProviderId:      entry.PreferredProviderId?.ToString("N") ?? string.Empty,
                        AppointmentType: entry.AppointmentType,
                        Available:       true);

                    await DispatchOffersForSlotAsync(slot, cancellationToken);
                }
                else if (entry.OfferedSlotId is not null)
                {
                    // Any-provider entry — reconstruct slot without provider constraint
                    var slot = new SlotItem(
                        SlotId:          entry.OfferedSlotId,
                        Date:            entry.PreferredDate.ToString("yyyy-MM-dd"),
                        StartTime:       entry.PreferredStartTime.ToString("HH:mm"),
                        EndTime:         entry.PreferredEndTime.ToString("HH:mm"),
                        ProviderName:    string.Empty,
                        ProviderId:      string.Empty,
                        AppointmentType: entry.AppointmentType,
                        Available:       true);

                    await DispatchOffersForSlotAsync(slot, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdvanceExpiredOffersAsync: unhandled error during expiry advancement.");
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

    /// <summary>
    /// Dispatches a waitlist offer to a single candidate.
    /// Returns <c>true</c> when at least one channel succeeded (email or SMS),
    /// or when the contact is valid but opted-out of SMS (we still offered via email).
    /// Returns <c>false</c> when both channels confirmed the contact is permanently invalid (EC-1).
    /// </summary>
    private async Task<bool> DispatchSingleOfferAsync(
        WaitlistEntry     entry,
        SlotItem          slot,
        DateTime          now,
        bool              isWithin24Hours,
        string            frontendBase,
        string            appointmentDetails,
        CancellationToken cancellationToken)
    {
        // Generate a cryptographically secure claim token (OWASP A07, NFR-013)
        var tokenBytes = RandomNumberGenerator.GetBytes(48);
        var claimToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // URL-safe Base64

        entry.ClaimToken        = claimToken;
        entry.OfferedSlotId     = slot.SlotId;
        entry.OfferedAtUtc      = now;
        entry.ClaimExpiresAtUtc = now.Add(OfferExpiryWindow); // 24-hour offer window (AC-4)
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

        // Dispatch via the notification service (email + SMS with opt-out respect)
        var request = new WaitlistOfferNotificationRequest(
            WaitlistEntryId:    entry.Id,
            PatientId:          entry.PatientId,
            PatientEmail:       entry.Patient.Email,
            PatientPhoneNumber: entry.Patient.PhoneNumber ?? string.Empty,
            PatientFullName:    entry.Patient.FullName,
            SlotId:             slot.SlotId,
            AppointmentDetails: appointmentDetails,
            AppointmentTimeUtc: new DateTime(
                entry.PreferredDate.Year, entry.PreferredDate.Month, entry.PreferredDate.Day,
                entry.PreferredStartTime.Hour, entry.PreferredStartTime.Minute, 0, DateTimeKind.Utc),
            ProviderName:       slot.ProviderName,
            AppointmentType:    entry.AppointmentType,
            ClaimLink:          claimLink,
            IsWithin24Hours:    isWithin24Hours);

        var result = await _offerNotificationService.SendOfferAsync(request, cancellationToken);

        if (result.IsInvalidContact)
        {
            // Revert the offer state — this candidate cannot be reached (EC-1)
            entry.Status            = WaitlistStatus.Active;
            entry.ClaimToken        = null;
            entry.OfferedSlotId     = null;
            entry.OfferedAtUtc      = null;
            entry.ClaimExpiresAtUtc = null;
            entry.LastNotifiedAtUtc = null;
            entry.UpdatedAt         = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Waitlist offer skipped for entry {EntryId} — invalid contact details. " +
                "Advancing to next FIFO candidate.",
                entry.Id);

            return false; // Advance to next candidate
        }

        _logger.LogInformation(
            "Waitlist offer dispatched: entryId={EntryId}, slotId={SlotId}, " +
            "emailSent={EmailSent}, smsSent={SmsSent}.",
            entry.Id, slot.SlotId, result.EmailSent, result.SmsSent);

        return true;
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
