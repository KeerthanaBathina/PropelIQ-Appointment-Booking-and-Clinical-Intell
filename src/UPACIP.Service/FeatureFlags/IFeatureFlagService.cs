namespace UPACIP.Service.FeatureFlags;

/// <summary>
/// Provides feature flag state queries for controllers and services (US_101, AC-1, AC-2).
///
/// Implementations must be thread-safe and reflect configuration changes within
/// 1 minute of the source file being modified (AC-2).
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Returns <c>true</c> when the named feature flag is present and enabled.
    ///
    /// Fail-closed: unknown flag names return <c>false</c> so unregistered flags
    /// are never accidentally active in production.
    /// </summary>
    /// <param name="featureName">
    /// Flag name as defined in <c>config/featureflags.json</c>.
    /// Use constants from <c>UPACIP.Api.Configuration.FeatureFlags</c> to avoid magic strings.
    /// </param>
    bool IsEnabled(string featureName);

    /// <summary>
    /// Returns a read-only snapshot of all registered flags and their current state.
    /// Used by admin dashboards and diagnostic endpoints.
    /// </summary>
    IReadOnlyDictionary<string, FeatureFlagDefinition> GetAllFlags();
}
