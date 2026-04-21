using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Request model for acquiring a temporary slot hold (POST /api/appointments/hold, US_018 AC-3).
///
/// The hold reserves the slot in Redis with a 60-second TTL before the patient commits
/// to a booking, preventing other patients from booking the same slot during checkout.
/// </summary>
public sealed record HoldRequest
{
    /// <summary>
    /// Stable slot identifier to hold.
    /// Format: {yyyyMMdd}-{HHmm}-{providerGuid:N} (from <see cref="SlotItem.SlotId"/>).
    /// </summary>
    [Required(ErrorMessage = "SlotId is required.")]
    [StringLength(50, ErrorMessage = "SlotId must not exceed 50 characters.")]
    public string SlotId { get; init; } = string.Empty;
}
