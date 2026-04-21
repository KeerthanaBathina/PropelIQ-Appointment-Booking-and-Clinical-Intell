namespace UPACIP.Service.Appointments;

/// <summary>
/// Response returned when a patient redeems a waitlist claim link (US_020 AC-3).
/// The backend has already acquired a 60-second slot hold; the UI uses
/// <c>HoldAcquiredAt</c> to compute remaining seconds for the countdown.
/// Matches the <c>ClaimedOffer</c> TypeScript type in <c>useWaitlistOfferClaim.ts</c>.
/// </summary>
public sealed record ClaimWaitlistOfferResponse(
    /// <summary>Slot details for the confirmed hold.</summary>
    ClaimedSlot Slot,
    /// <summary>True when the slot starts less than 24 hours from now (EC-2).</summary>
    bool IsWithin24Hours,
    /// <summary>UTC ISO-8601 timestamp when the hold was acquired.</summary>
    DateTimeOffset HoldAcquiredAt,
    /// <summary>Provider display name (may differ from Slot.ProviderName for clarity).</summary>
    string ProviderName);

/// <summary>
/// Slim slot descriptor returned inside <see cref="ClaimWaitlistOfferResponse"/>.
/// Matches the <c>AppointmentSlot</c> TypeScript type used by <c>TimeSlotGrid</c>.
/// </summary>
public sealed record ClaimedSlot(
    string SlotId,
    string Date,
    string StartTime,
    string EndTime,
    string ProviderName,
    string ProviderId,
    string AppointmentType,
    bool Available);

/// <summary>
/// Discriminated result for the claim-link validation operation.
/// </summary>
public enum ClaimWaitlistOfferStatus
{
    /// <summary>Token valid; slot hold acquired; claim response ready.</summary>
    Success,
    /// <summary>Token not found in the database (404).</summary>
    NotFound,
    /// <summary>Token has expired or the hold window elapsed (410).</summary>
    Expired,
    /// <summary>Token was already claimed by this patient in a prior request (idempotent 200).</summary>
    AlreadyClaimed,
}

public sealed record ClaimWaitlistOfferResult(
    ClaimWaitlistOfferStatus Status,
    ClaimWaitlistOfferResponse? Response = null);
