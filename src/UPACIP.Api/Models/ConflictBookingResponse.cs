using UPACIP.Service.Appointments;

namespace UPACIP.Api.Models;

/// <summary>
/// Response body for 409 Conflict on appointment booking (US_018, AC-2).
/// Includes the conflict reason and up to 3 alternative available slots.
/// </summary>
public sealed record ConflictBookingResponse(
    /// <summary>Human-readable conflict message — always "Slot no longer available." (AC-2).</summary>
    string Message,

    /// <summary>Up to 3 alternative available slots for the same provider/type (AC-2).</summary>
    IReadOnlyList<SlotItem> AlternativeSlots);
