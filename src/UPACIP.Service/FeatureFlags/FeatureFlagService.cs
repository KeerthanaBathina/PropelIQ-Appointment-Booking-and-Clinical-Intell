using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.FeatureFlags;

/// <summary>
/// Singleton feature flag service backed by <see cref="IOptionsMonitor{T}"/> for
/// hot-reload and an in-memory fallback cache for corruption resilience
/// (US_101, AC-1, AC-2, edge cases 1 and 2).
///
/// Hot-reload (AC-2):
///   <see cref="IOptionsMonitor{T}.OnChange"/> fires within ~30–60 seconds whenever
///   <c>config/featureflags.json</c> is saved (driven by <c>reloadOnChange: true</c>
///   on the JSON configuration source and the underlying <c>PhysicalFileProvider</c>
///   change-token polling).  This satisfies the "within 1 minute" requirement.
///
/// Corruption fallback (edge case 1):
///   <c>_lastKnownGoodConfig</c> holds a deep clone of the last valid configuration.
///   If <see cref="IOptionsMonitor{T}.CurrentValue"/> returns null/empty (corrupt JSON),
///   the service falls back silently and logs a <c>Critical</c> event.
///
/// Thread safety:
///   <c>_lastKnownGoodConfig</c> is <c>volatile</c> — a single reference write from the
///   <c>OnChange</c> callback is immediately visible to reader threads without locking.
///   <see cref="CloneOptions"/> creates a deep copy, preventing mutation through the
///   stored reference.
///
/// Fail-closed (AC-1):
///   <see cref="IsEnabled"/> returns <c>false</c> for unknown flags.  A feature that has
///   not been explicitly registered in the configuration file is never accidentally active.
/// </summary>
public sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly IOptionsMonitor<FeatureFlagOptions> _optionsMonitor;
    private readonly ILogger<FeatureFlagService> _logger;

    // volatile: ensures the reference write in OnChange is immediately visible to all threads
    private volatile FeatureFlagOptions _lastKnownGoodConfig;

    public FeatureFlagService(
        IOptionsMonitor<FeatureFlagOptions> optionsMonitor,
        ILogger<FeatureFlagService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        // Snapshot the initial configuration as the first "last known good" value.
        _lastKnownGoodConfig = CloneOptions(optionsMonitor.CurrentValue);

        // Subscribe to hot-reload change events (AC-2).
        _optionsMonitor.OnChange(OnConfigurationChanged);
    }

    /// <inheritdoc />
    public bool IsEnabled(string featureName)
    {
        var options = GetCurrentOptions();

        if (options.Flags.TryGetValue(featureName, out var flag))
        {
            return flag.IsEnabled;
        }

        _logger.LogWarning(
            "FEATURE_FLAG_UNKNOWN: Flag '{FeatureName}' not found in configuration, defaulting to disabled (fail-closed)",
            featureName);

        return false; // Fail-closed: unknown flags are always disabled
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, FeatureFlagDefinition> GetAllFlags()
    {
        var options = GetCurrentOptions();
        return options.Flags.AsReadOnly();
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    private FeatureFlagOptions GetCurrentOptions()
    {
        try
        {
            var current = _optionsMonitor.CurrentValue;

            if (current?.Flags is null || current.Flags.Count == 0)
            {
                _logger.LogCritical(
                    "FEATURE_FLAG_CORRUPTION: Current configuration is null or empty, " +
                    "falling back to last known good configuration ({FlagCount} flags)",
                    _lastKnownGoodConfig.Flags.Count);

                return _lastKnownGoodConfig;
            }

            return current;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "FEATURE_FLAG_CORRUPTION: Exception reading current configuration, " +
                "falling back to last known good configuration ({FlagCount} flags)",
                _lastKnownGoodConfig.Flags.Count);

            return _lastKnownGoodConfig;
        }
    }

    private void OnConfigurationChanged(FeatureFlagOptions newOptions)
    {
        if (newOptions?.Flags is null || newOptions.Flags.Count == 0)
        {
            _logger.LogCritical(
                "FEATURE_FLAG_CORRUPTION: Change event delivered null/empty configuration, " +
                "retaining last known good configuration ({FlagCount} flags)",
                _lastKnownGoodConfig.Flags.Count);

            return;
        }

        var previous = _lastKnownGoodConfig;
        _lastKnownGoodConfig = CloneOptions(newOptions);

        // Log per-flag state transitions for audit trail.
        foreach (var (name, flag) in newOptions.Flags)
        {
            if (previous.Flags.TryGetValue(name, out var oldFlag)
                && oldFlag.IsEnabled != flag.IsEnabled)
            {
                _logger.LogInformation(
                    "FEATURE_FLAG_TOGGLED: '{FeatureName}' changed {OldState} → {NewState}",
                    name,
                    oldFlag.IsEnabled ? "Enabled" : "Disabled",
                    flag.IsEnabled ? "Enabled" : "Disabled");
            }
        }

        // Log newly added flags.
        foreach (var name in newOptions.Flags.Keys.Except(previous.Flags.Keys))
        {
            _logger.LogInformation(
                "FEATURE_FLAG_ADDED: '{FeatureName}' registered as {State}",
                name,
                newOptions.Flags[name].IsEnabled ? "Enabled" : "Disabled");
        }
    }

    private static FeatureFlagOptions CloneOptions(FeatureFlagOptions source)
    {
        return new FeatureFlagOptions
        {
            Flags = source.Flags.ToDictionary(
                kvp => kvp.Key,
                kvp => new FeatureFlagDefinition
                {
                    Name               = kvp.Value.Name,
                    IsEnabled          = kvp.Value.IsEnabled,
                    Description        = kvp.Value.Description,
                    CreatedUtc         = kvp.Value.CreatedUtc,
                    LastModifiedUtc    = kvp.Value.LastModifiedUtc,
                    IsPercentageBased  = kvp.Value.IsPercentageBased,
                    RolloutPercentage  = kvp.Value.RolloutPercentage,
                }),
        };
    }
}
