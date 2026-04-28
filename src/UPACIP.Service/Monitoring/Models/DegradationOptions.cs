namespace UPACIP.Service.Monitoring.Models;

/// <summary>
/// Strongly-typed configuration for the graceful-degradation subsystem
/// (US_083 task_002, AC-2, EC-1).
///
/// Bound from the <c>"Degradation"</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class DegradationOptions
{
    /// <summary>Configuration section name used by <c>IConfiguration.GetSection</c>.</summary>
    public const string SectionName = "Degradation";

    /// <summary>
    /// Maps each logical feature name to the set of <see cref="DependencyCategory"/>
    /// names it depends on.  A feature is considered unavailable when any listed
    /// dependency is unhealthy.
    ///
    /// Example:
    /// <code>
    /// {
    ///   "ai_intake":  ["AiProviders"],
    ///   "core_crud":  ["Database"]
    /// }
    /// </code>
    /// </summary>
    public Dictionary<string, string[]> FeatureDependencyMap { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <c>true</c> the manager emits a <c>STAFF_NOTIFICATION</c> structured
    /// log event whenever degradation is activated or resolved.
    /// </summary>
    public bool StaffNotificationEnabled { get; set; } = true;

    /// <summary>
    /// Default human-readable message included in 503 responses when a feature
    /// is unavailable due to AI provider degradation.
    /// </summary>
    public string FallbackMessage { get; set; }
        = "AI services are temporarily unavailable. Please use manual workflows.";
}
