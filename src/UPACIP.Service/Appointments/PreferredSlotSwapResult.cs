namespace UPACIP.Service.Appointments;

/// <summary>
/// Outcome categories for a single preferred-slot swap evaluation (US_021).
/// </summary>
public enum PreferredSlotSwapStatus
{
    /// <summary>Appointment was automatically moved to the preferred slot (AC-1, AC-2).</summary>
    Swapped,

    /// <summary>
    /// Preferred slot opened within 24 hours — auto-swap skipped;
    /// a manual-confirmation notification was sent instead (AC-5).
    /// </summary>
    ManualConfirmationRequired,

    /// <summary>Auto-swap disabled by staff for this patient's account (AC-3).</summary>
    AutoSwapDisabled,

    /// <summary>
    /// Appointment is in an arrived or in-visit state — swap must not occur (EC-2).
    /// </summary>
    PatientCheckedIn,

    /// <summary>
    /// No active Scheduled appointment with matching preferred criteria was found
    /// for this patient / slot.
    /// </summary>
    NoCandidateFound,

    /// <summary>
    /// Optimistic-concurrency conflict on the swap write; the next eligible
    /// candidate will be tried (EC-1).
    /// </summary>
    ConcurrencyConflict,
}

/// <summary>
/// Result of evaluating and executing (or skipping) a preferred-slot swap for one candidate.
/// </summary>
public sealed record PreferredSlotSwapResult(
    PreferredSlotSwapStatus Status,

    /// <summary>The appointment that was or could have been swapped. Null for NoCandidateFound.</summary>
    Guid? AppointmentId     = null,

    /// <summary>The preferred slot that triggered the evaluation.</summary>
    string? PreferredSlotId = null,

    /// <summary>UTC time of the old (released) slot. Populated on Swapped.</summary>
    DateTime? OldSlotTime   = null,

    /// <summary>UTC time of the new (claimed) slot. Populated on Swapped and ManualConfirmationRequired.</summary>
    DateTime? NewSlotTime   = null,

    /// <summary>Human-readable skip/skip reason for log and audit purposes.</summary>
    string? SkipReason      = null);
