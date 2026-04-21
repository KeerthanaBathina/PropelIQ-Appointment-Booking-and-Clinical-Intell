using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persists a patient's waitlist registration criteria and offer lifecycle state (US_020, AC-1).
///
/// Lifecycle:
///   Active → Offered (on slot opening) → Claimed (on link redemption) → Booked / Expired
///   Active → Removed (explicit patient action)
///
/// EC-1: Cancelling another appointment must NOT change <see cref="Status"/> on this entity.
///       Only <see cref="WaitlistService"/> and <see cref="WaitlistOfferProcessor"/> mutate status.
///
/// Indexing strategy (DR-007, DR-019):
///   - ix_waitlist_entries_patient_id           — patient self-service queries
///   - ix_waitlist_entries_status               — processor fans out only Active entries
///   - ix_waitlist_entries_preferred_date_provider — fast matching by date + provider on slot opening
///   - ix_waitlist_entries_claim_token          — O(1) claim-link resolution
/// </summary>
public sealed class WaitlistEntry : BaseEntity
{
    // ─── Patient relationship ──────────────────────────────────────────────

    /// <summary>FK to the owning <see cref="Patient"/>.</summary>
    public Guid PatientId { get; set; }

    // ─── Criteria fields ──────────────────────────────────────────────────

    /// <summary>
    /// Patient's preferred date as ISO-8601 (YYYY-MM-DD).
    /// The matching logic treats this as an exact date — if the patient wants flexibility
    /// they must register multiple entries.
    /// </summary>
    public DateOnly PreferredDate { get; set; }

    /// <summary>Preferred slot start time as "HH:mm" (24-hour, inclusive).</summary>
    public TimeOnly PreferredStartTime { get; set; }

    /// <summary>Preferred slot end time as "HH:mm" (24-hour, exclusive).</summary>
    public TimeOnly PreferredEndTime { get; set; }

    /// <summary>
    /// Optional preferred provider UUID.
    /// Null means any provider is acceptable.
    /// </summary>
    public Guid? PreferredProviderId { get; set; }

    /// <summary>Appointment type label (e.g. "General Checkup"). Max 50 chars.</summary>
    public string AppointmentType { get; set; } = string.Empty;

    // ─── Lifecycle state ───────────────────────────────────────────────────

    /// <summary>Current lifecycle state.</summary>
    public WaitlistStatus Status { get; set; } = WaitlistStatus.Active;

    // ─── Offer tracking ────────────────────────────────────────────────────

    /// <summary>
    /// Cryptographically random URL-safe claim token included in the offer notification link.
    /// Null until an offer is dispatched (Status = Offered).
    /// Max 64 chars (Base64Url of 48-byte random value).
    /// </summary>
    public string? ClaimToken { get; set; }

    /// <summary>
    /// Stable slot identifier for the offered slot (same format as <c>SlotItem.SlotId</c>).
    /// Null until an offer is dispatched.
    /// </summary>
    public string? OfferedSlotId { get; set; }

    /// <summary>UTC timestamp when the offer notification was dispatched. Null until offered.</summary>
    public DateTime? OfferedAtUtc { get; set; }

    /// <summary>UTC expiry of the claim token. Null until offered. Typically OfferedAtUtc + 1 min.</summary>
    public DateTime? ClaimExpiresAtUtc { get; set; }

    /// <summary>UTC timestamp when the patient redeemed the claim link. Null until claimed.</summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>UTC timestamp of the most recent notification dispatch. Used for EC-2 audit.</summary>
    public DateTime? LastNotifiedAtUtc { get; set; }

    // ─── Navigation ────────────────────────────────────────────────────────

    public Patient Patient { get; set; } = null!;
}
