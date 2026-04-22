using UPACIP.Service.Consolidation;

namespace UPACIP.Service.AI;

/// <summary>
/// Monitors AI provider availability and exposes a cached status endpoint (US_046 AC-4, NFR-030).
///
/// Status is stored in Redis with a 5-minute TTL so all instances share a single view.
/// When the AI pipeline fails (circuit breaker open per AIR-O04), it calls
/// <see cref="SetUnavailableAsync"/> to propagate the state immediately. The entry expires
/// automatically when the TTL elapses so a subsequent request triggers a fresh probe.
/// </summary>
public interface IAiHealthCheckService
{
    /// <summary>
    /// Returns the current AI availability status from Redis cache.
    /// When no cached entry exists, performs a lightweight probe of the AI gateway
    /// (OPTIONS /v1/models or equivalent) and caches the result.
    /// </summary>
    Task<AiHealthStatusDto> GetHealthStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Records that the AI service is unavailable (e.g. circuit breaker opened, request timed out).
    /// Writes a Redis entry with 5-minute TTL so subsequent status reads reflect the failure.
    /// </summary>
    /// <param name="reason">Short human-readable reason for unavailability.</param>
    Task SetUnavailableAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Records that the AI service is available (e.g. circuit breaker closed, health probe succeeded).
    /// Writes a Redis entry with 5-minute TTL.
    /// </summary>
    Task SetAvailableAsync(CancellationToken ct = default);
}
