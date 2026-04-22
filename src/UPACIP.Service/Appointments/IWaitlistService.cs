using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Orchestrates waitlist registration, slot-match offer dispatch, and claim-link redemption (US_020).
///
/// Contracts:
///   - <see cref="RegisterAsync"/>           : AC-1 — persist waitlist criteria for a patient.
///   - <see cref="ClaimOfferAsync"/>          : AC-3 — validate claim token and acquire hold.
///   - <see cref="DispatchOffersForSlotAsync"/>: AC-2, AC-4 — notify all Active entries that match an opened slot.
///   - <see cref="RemoveEntryAsync"/>         : EC-1 — explicit patient removal (NOT triggered by cancellation).
/// </summary>
public interface IWaitlistService
{
    /// <summary>
    /// Registers a new waitlist entry for the authenticated patient (AC-1).
    ///
    /// Returns <c>null</c> when an Active entry with identical criteria already exists (409 conflict).
    /// </summary>
    /// <param name="userEmail">JWT email claim — used to resolve PatientId server-side (OWASP A01).</param>
    /// <param name="request">Registration payload from the frontend.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<JoinWaitlistResponse?> RegisterAsync(
        string               userEmail,
        JoinWaitlistRequest  request,
        CancellationToken    cancellationToken = default);

    /// <summary>
    /// Redeems a claim token from a waitlist notification link, acquires a 60-second slot hold,
    /// and returns the held slot details for the booking UI (AC-3).
    /// </summary>
    /// <param name="claimToken">URL-safe token from <c>?claim=TOKEN</c>.</param>
    /// <param name="userEmail">JWT email claim — ownership check (OWASP A01).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClaimWaitlistOfferResult> ClaimOfferAsync(
        string            claimToken,
        string            userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans all Active waitlist entries for criteria matching <paramref name="openedSlot"/>
    /// and dispatches offer notifications via email + SMS (AC-2, AC-4).
    ///
    /// Idempotent — already-Offered entries are skipped.
    /// </summary>
    /// <param name="openedSlot">Slot that just became available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchOffersForSlotAsync(SlotItem openedSlot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly removes the waitlist entry identified by <paramref name="waitlistId"/>
    /// for the authenticated patient (EC-1).
    ///
    /// Returns <c>false</c> when the entry is not found or does not belong to the patient.
    /// </summary>
    Task<bool> RemoveEntryAsync(
        Guid              waitlistId,
        string            userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans all <c>Offered</c> waitlist entries whose <c>ClaimExpiresAtUtc</c> is in the
    /// past, marks them <c>Expired</c>, and advances to the next FIFO candidate for each
    /// affected slot (US_036 AC-4, EC-2).
    ///
    /// Called periodically by <see cref="WaitlistOfferProcessor"/>.
    /// Implementations must never throw — all errors are logged internally.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AdvanceExpiredOffersAsync(CancellationToken cancellationToken = default);
}
