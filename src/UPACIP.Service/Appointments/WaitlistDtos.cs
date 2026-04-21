namespace UPACIP.Service.Appointments;

/// <summary>
/// Request payload for joining the waitlist (US_020 AC-1).
/// All fields are resolved from the JWT and the request body — the backend never trusts
/// patient identity from the body (OWASP A01).
/// </summary>
public sealed record JoinWaitlistRequest(
    /// <summary>ISO-8601 preferred date (YYYY-MM-DD).</summary>
    string PreferredDate,
    /// <summary>Preferred slot start time as "HH:mm" (24-hour, inclusive).</summary>
    string PreferredTimeStart,
    /// <summary>Preferred slot end time as "HH:mm" (24-hour, exclusive).</summary>
    string PreferredTimeEnd,
    /// <summary>Optional preferred provider UUID; null = any provider.</summary>
    string? PreferredProviderId,
    /// <summary>Optional provider display name (not persisted — for confirmation copy only).</summary>
    string? PreferredProviderName,
    /// <summary>Visit type label (e.g. "General Checkup").</summary>
    string AppointmentType);

/// <summary>
/// Response returned to the frontend after successful waitlist registration (201 Created).
/// Matches the <c>WaitlistRegistration</c> TypeScript type in <c>useJoinWaitlist.ts</c>.
/// </summary>
public sealed record JoinWaitlistResponse(
    /// <summary>Unique waitlist entry identifier.</summary>
    Guid WaitlistId,
    /// <summary>Echo of the accepted preferred date (YYYY-MM-DD).</summary>
    string PreferredDate,
    string PreferredTimeStart,
    string PreferredTimeEnd,
    /// <summary>Provider display name; null when any-provider was requested.</summary>
    string? PreferredProviderName,
    string AppointmentType,
    /// <summary>UTC ISO-8601 timestamp of registration.</summary>
    DateTimeOffset RegisteredAt);
