using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace UPACIP.Service.PatientRights.Deletion;

/// <summary>
/// Purges all Redis cache entries keyed by the patient's ID (US_094, AC-3, phase 5).
///
/// Patterns cleared:
///   patient:{patientId}:*         — demographic / profile cache
///   appointments:{patientId}:*    — per-patient appointment list cache
///   intake:{patientId}:*          — intake form responses
///
/// The operation is fail-open: a Redis outage is logged as a warning but does not abort
/// the deletion pipeline (the patient data is deleted from the primary store regardless).
/// </summary>
public sealed class CacheCleanupService
{
    private readonly IConnectionMultiplexer       _redis;
    private readonly ILogger<CacheCleanupService> _logger;

    // Pattern prefixes to clear for the patient.
    private static readonly string[] Prefixes = ["patient", "appointments", "intake"];

    public CacheCleanupService(
        IConnectionMultiplexer       redis,
        ILogger<CacheCleanupService> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    /// <summary>
    /// Removes all cache keys for the patient across all configured prefixes.
    /// Returns the total number of keys deleted; returns 0 and logs a warning on failure.
    /// </summary>
    public async Task<int> PurgePatientCacheAsync(Guid patientId, CancellationToken ct)
    {
        try
        {
            var db     = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints()[0]);
            var total  = 0;

            foreach (var prefix in Prefixes)
            {
                var pattern = $"{prefix}:{patientId}:*";
                var keys    = server.Keys(pattern: pattern).ToArray();

                if (keys.Length == 0)
                    continue;

                await db.KeyDeleteAsync(keys);
                total += keys.Length;

                _logger.LogInformation(
                    "DELETION_CACHE_PURGED: Prefix={Prefix}, PatientId={PatientId}, KeysDeleted={Count}",
                    prefix, patientId, keys.Length);
            }

            return total;
        }
        catch (RedisException ex)
        {
            // Fail-open — Redis outage must not prevent the primary-store deletion.
            _logger.LogWarning(ex,
                "DELETION_CACHE_PURGE_FAILED: Redis unavailable for PatientId={PatientId}. " +
                "Cache will expire naturally.", patientId);
            return 0;
        }
    }
}
