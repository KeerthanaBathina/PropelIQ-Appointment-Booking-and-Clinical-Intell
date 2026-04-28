using Microsoft.Extensions.Logging;
using UPACIP.Service.Caching.Models;
using UPACIP.Service.Performance;

namespace UPACIP.Service.Caching;

/// <summary>
/// Cache-aside wrapper for PHI-minimal patient profile snapshots
/// (US_084 task_002, AC-2, AC-3).
///
/// <para>
/// Stores <see cref="CachedPatientProfile"/> under Redis key
/// <c>patient:profile:{patientId}</c> with a 5-minute absolute TTL (NFR-030).
/// The service follows the cache-aside pattern:
/// <list type="number">
///   <item>Consumer calls <see cref="GetProfileAsync"/> — returns <c>null</c> on miss.</item>
///   <item>Consumer fetches from PostgreSQL and calls <see cref="SetProfileAsync"/>.</item>
///   <item>On state changes (booking/cancellation), <see cref="InvalidateProfileAsync"/>
///     evicts the stale entry so the next read gets fresh data.</item>
/// </list>
/// </para>
///
/// <para>
/// Redis unavailability is handled transparently by the underlying
/// <see cref="ICacheService"/> circuit breaker — all operations fail-open.
/// </para>
/// </summary>
public sealed class PatientProfileCacheService
{
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    private const string KeyPrefix        = "patient:profile:";
    private const string MetricHit        = "cache.patient_profile.hit";
    private const string MetricMiss       = "cache.patient_profile.miss";

    private readonly ICacheService                      _cache;
    private readonly IPerformanceTracker                _tracker;
    private readonly ILogger<PatientProfileCacheService> _logger;

    public PatientProfileCacheService(
        ICacheService                       cache,
        IPerformanceTracker                 performanceTracker,
        ILogger<PatientProfileCacheService> logger)
    {
        _cache   = cache;
        _tracker = performanceTracker;
        _logger  = logger;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the cached <see cref="CachedPatientProfile"/> for <paramref name="patientId"/>,
    /// or <c>null</c> on a cache miss or when Redis is unavailable.
    /// </summary>
    public async Task<CachedPatientProfile?> GetProfileAsync(
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        var key    = BuildKey(patientId);
        var cached = await _cache.GetAsync<CachedPatientProfile>(key, cancellationToken);

        if (cached is not null)
        {
            _tracker.RecordLatency(MetricHit, 1);
            _logger.LogDebug(
                "Patient profile cache HIT for patient={PatientId}.", patientId);
            return cached;
        }

        _tracker.RecordLatency(MetricMiss, 1);
        _logger.LogDebug(
            "Patient profile cache MISS for patient={PatientId}.", patientId);
        return null;
    }

    /// <summary>
    /// Stores <paramref name="profile"/> in Redis under
    /// <c>patient:profile:{patientId}</c> with a 5-minute absolute TTL.
    /// Failures are swallowed (Redis unavailability must not block the response).
    /// </summary>
    public async Task SetProfileAsync(
        Guid                  patientId,
        CachedPatientProfile  profile,
        CancellationToken     cancellationToken = default)
    {
        var key = BuildKey(patientId);
        await _cache.SetAsync(key, profile, ProfileCacheTtl, cancellationToken);

        _logger.LogDebug(
            "Patient profile cached for patient={PatientId} TTL={TtlMinutes}m.",
            patientId, ProfileCacheTtl.TotalMinutes);
    }

    /// <summary>
    /// Evicts the cached profile for <paramref name="patientId"/>.
    /// Failures are swallowed.
    /// </summary>
    public async Task InvalidateProfileAsync(
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(patientId);
        await _cache.RemoveAsync(key, cancellationToken);

        _logger.LogDebug(
            "Patient profile cache evicted for patient={PatientId}.", patientId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildKey(Guid patientId) => $"{KeyPrefix}{patientId}";
}
