namespace UPACIP.Service.Configuration;

/// <summary>
/// Strongly-typed options for the Redis cache connection (US_101, AC-4).
/// Bound from the <c>Redis</c> section in <c>appsettings.json</c>.
///
/// Credentials MUST be supplied via environment variables
/// (<c>UPACIP_Redis__ConnectionString</c>) in Production (OWASP A07).
/// </summary>
public sealed class RedisOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string.  Provided via env var or user secrets.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Default cache entry TTL in minutes.  Default: 5.</summary>
    public int DefaultTtlMinutes { get; init; } = 5;

    /// <summary>Initial TCP connection timeout in milliseconds.  Default: 5000.</summary>
    public int ConnectTimeoutMs { get; init; } = 5000;

    /// <summary>Synchronous operation timeout in milliseconds.  Default: 1000.</summary>
    public int SyncTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Key prefix applied to all cache entries to prevent collisions between environments.
    /// Default: <c>UPACIP:</c>.
    /// </summary>
    public string InstanceName { get; init; } = "UPACIP:";
}
