namespace UPACIP.Service.Auth;

/// <summary>
/// Represents the session state stored in Redis per authenticated user.
/// Serialized as JSON under the key <c>session:{userId}</c> with a 15-minute sliding TTL.
/// </summary>
public sealed class SessionData
{
    /// <summary>Unique session identifier (UUID), embedded as a claim in the JWT.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>UTC timestamp of the last successful authenticated request (used for inactivity tracking).</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>UTC timestamp when the session was first established (login time).</summary>
    public DateTime LoginAt { get; init; }

    /// <summary>IP address of the client that initiated the session (sanitized, PII-safe for logging).</summary>
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>User-Agent string of the client that initiated the session.</summary>
    public string UserAgent { get; init; } = string.Empty;
}
