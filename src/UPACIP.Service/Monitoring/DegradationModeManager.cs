using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Monitoring.Models;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Thread-safe singleton that owns the runtime degradation state for the UPACIP platform
/// (US_083 task_002, AC-1, AC-2, AC-3, EC-1).
///
/// <para>
/// <b>Health state</b>: stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// <see cref="DependencyCategory"/>.  All mutations are atomic; readers always see a consistent
/// snapshot via <see cref="GetCurrentState"/>.
/// </para>
///
/// <para>
/// <b>Redis persistence</b>: when a category becomes unhealthy the flag
/// <c>system:degradation:status</c> is written to Redis with a 5-minute TTL so other
/// application instances can observe the signal.  Redis failure is non-fatal — the in-memory
/// state is authoritative (EC-1 graceful fallback).
/// </para>
///
/// <para>
/// <b>Feature availability</b>: derived by checking whether all categories listed in
/// <see cref="DegradationOptions.FeatureDependencyMap"/> for a given feature are healthy.
/// Unknown features default to <c>true</c> (fail-open, OWASP A04 least-privilege trade-off
/// acknowledged — features are operational until proven degraded).
/// </para>
/// </summary>
public sealed class DegradationModeManager : IDegradationModeManager
{
    private const string RedisFlagKey = "system:degradation:status";

    private readonly ConcurrentDictionary<DependencyCategory, bool> _health;
    private readonly IDistributedCache _cache;
    private readonly IOptions<DegradationOptions> _options;
    private readonly ILogger<DegradationModeManager> _logger;

    private DateTime? _degradedSince;
    private readonly object _stateLock = new();

    public DegradationModeManager(
        IDistributedCache cache,
        IOptions<DegradationOptions> options,
        ILogger<DegradationModeManager> logger)
    {
        _cache   = cache;
        _options = options;
        _logger  = logger;

        // All categories start healthy.
        _health = new ConcurrentDictionary<DependencyCategory, bool>();
        foreach (DependencyCategory cat in Enum.GetValues<DependencyCategory>())
        {
            _health[cat] = true;
        }
    }

    // ── IDegradationModeManager ───────────────────────────────────────────────

    /// <inheritdoc />
    public DegradationState GetCurrentState()
    {
        var healthSnapshot = _health.ToDictionary(kv => kv.Key, kv => kv.Value);
        var anyUnhealthy   = healthSnapshot.Values.Any(v => !v);
        var mode           = anyUnhealthy ? SystemMode.Degraded : SystemMode.Normal;

        DateTime? since;
        lock (_stateLock) { since = _degradedSince; }

        var featureAvailability = BuildFeatureAvailability(healthSnapshot);

        return new DegradationState
        {
            Mode                = mode,
            DependencyHealth    = healthSnapshot,
            FeatureAvailability = featureAvailability,
            DegradedSince       = mode == SystemMode.Degraded ? since : null,
        };
    }

    /// <inheritdoc />
    public void ActivateDegradation(DependencyCategory category)
    {
        var wasHealthy = _health.TryGetValue(category, out var prev) && prev;
        _health[category] = false;

        if (wasHealthy)
        {
            DateTime degradedAt;
            lock (_stateLock)
            {
                _degradedSince ??= DateTime.UtcNow;
                degradedAt = _degradedSince.Value;
            }

            _logger.LogWarning(
                "DEGRADATION_ACTIVATED: Category={Category}, DegradedSince={DegradedSince:O}",
                category, degradedAt);

            if (_options.Value.StaffNotificationEnabled)
            {
                _logger.LogWarning(
                    "STAFF_NOTIFICATION: System degradation activated. " +
                    "Category={Category}, Message={Message}",
                    category, _options.Value.FallbackMessage);
            }

            PersistRedisFlagFireAndForget(degraded: true);
        }
    }

    /// <inheritdoc />
    public void DeactivateDegradation(DependencyCategory category)
    {
        var wasUnhealthy = _health.TryGetValue(category, out var prev) && !prev;
        _health[category] = true;

        if (wasUnhealthy)
        {
            var allHealthy = _health.Values.All(v => v);

            if (allHealthy)
            {
                lock (_stateLock) { _degradedSince = null; }

                _logger.LogInformation(
                    "DEGRADATION_RESOLVED: All dependencies healthy again. Category={Category}",
                    category);

                if (_options.Value.StaffNotificationEnabled)
                {
                    _logger.LogInformation(
                        "STAFF_NOTIFICATION: System degradation resolved. Category={Category}",
                        category);
                }

                PersistRedisFlagFireAndForget(degraded: false);
            }
            else
            {
                _logger.LogInformation(
                    "DEGRADATION_PARTIAL_RECOVERY: Category={Category} recovered but other dependencies still unhealthy.",
                    category);
            }
        }
    }

    /// <inheritdoc />
    public bool IsFeatureAvailable(string featureName)
    {
        var map = _options.Value.FeatureDependencyMap;

        if (!map.TryGetValue(featureName, out var deps) || deps.Length == 0)
        {
            return true; // Unknown feature — fail-open.
        }

        foreach (var dep in deps)
        {
            if (Enum.TryParse<DependencyCategory>(dep, ignoreCase: true, out var cat) &&
                _health.TryGetValue(cat, out var healthy) && !healthy)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool GetDependencyStatus(DependencyCategory category)
        => _health.TryGetValue(category, out var healthy) && healthy;

    // ── Private helpers ───────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, bool> BuildFeatureAvailability(
        Dictionary<DependencyCategory, bool> healthSnapshot)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var map    = _options.Value.FeatureDependencyMap;

        foreach (var (feature, deps) in map)
        {
            var available = deps.All(dep =>
                Enum.TryParse<DependencyCategory>(dep, ignoreCase: true, out var cat)
                    ? healthSnapshot.GetValueOrDefault(cat, true)
                    : true);

            result[feature] = available;
        }

        return result;
    }

    private void PersistRedisFlagFireAndForget(bool degraded)
    {
        // Fire-and-forget: Redis failure must never block the request path (EC-1).
        _ = Task.Run(async () =>
        {
            try
            {
                var value = Encoding.UTF8.GetBytes(degraded ? "1" : "0");
                await _cache.SetAsync(
                    RedisFlagKey,
                    value,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    });
            }
            catch (Exception ex)
            {
                // Swallow — Redis is non-authoritative; in-memory state is the source of truth.
                _logger.LogDebug(ex, "Could not persist degradation flag to Redis.");
            }
        });
    }
}
