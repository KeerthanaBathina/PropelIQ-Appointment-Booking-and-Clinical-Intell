namespace UPACIP.Service.FeatureFlags;

/// <summary>
/// Strongly-typed options that bind to the <c>FeatureFlags</c> section in
/// <c>config/featureflags.json</c> (US_101).
///
/// The dictionary key is the flag name, enabling O(1) lookup in
/// <see cref="IFeatureFlagService.IsEnabled"/>.
/// </summary>
public sealed class FeatureFlagOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// All registered feature flag definitions, keyed by flag name.
    /// Populated by <c>Microsoft.Extensions.Configuration</c> from <c>featureflags.json</c>.
    /// </summary>
    public Dictionary<string, FeatureFlagDefinition> Flags { get; set; } = new();
}
