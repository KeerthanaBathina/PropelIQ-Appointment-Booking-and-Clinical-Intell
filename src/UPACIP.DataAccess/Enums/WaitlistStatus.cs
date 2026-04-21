namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Lifecycle states for a <see cref="Entities.WaitlistEntry"/> (US_020).
///
/// State machine:
///   Active  ──(slot opens)──► Offered ──(patient confirms)──► Booked
///                                      ──(hold expires/offer rejected)──► Active (re-queued)
///                                      ──(token expired)──► Expired
///   Active  ──(patient removes)──► Removed
///   Active  ──(system cleanup)──► Expired
/// </summary>
public enum WaitlistStatus
{
    /// <summary>Entry is actively waiting for a matching slot.</summary>
    Active,

    /// <summary>A matching slot was found; offer notification dispatched; claim hold pending.</summary>
    Offered,

    /// <summary>Patient redeemed the claim link and is in the booking confirmation flow.</summary>
    Claimed,

    /// <summary>Patient successfully confirmed the booking — waitlist entry fulfilled.</summary>
    Booked,

    /// <summary>Claim token expired or patient did not respond in time.</summary>
    Expired,

    /// <summary>Patient explicitly removed themselves from the waitlist (EC-1).</summary>
    Removed,
}
