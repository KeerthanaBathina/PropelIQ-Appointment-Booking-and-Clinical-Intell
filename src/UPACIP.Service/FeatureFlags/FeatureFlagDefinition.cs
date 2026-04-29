namespace UPACIP.Service.FeatureFlags;

/// <summary>
/// Represents a single feature flag definition (US_101, AC-1, AC-2).
///
/// Phase 1 supports boolean flags only (<see cref="IsEnabled"/> = true/false).
/// <see cref="IsPercentageBased"/> and <see cref="RolloutPercentage"/> are schema
/// placeholders reserved for Phase 2 percentage-based rollout; they are persisted in
/// <c>config/featureflags.json</c> but ignored by <see cref="IFeatureFlagService"/>
/// evaluation logic in Phase 1 (edge case 2).
/// </summary>
public sealed class FeatureFlagDefinition
{
    /// <summary>Unique flag identifier — matches the key in <c>config/featureflags.json</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the feature is active.  <c>false</c> = feature is invisible to users (AC-1).</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Human-readable description for admin dashboards and audit logs.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the flag was first created.</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent toggle.  <c>null</c> until first modification.</summary>
    public DateTime? LastModifiedUtc { get; set; }

    // ── Phase 2 placeholders — ignored in Phase 1 evaluation (edge case 2) ──────

    /// <summary>Reserved: when <c>true</c>, <see cref="RolloutPercentage"/> governs activation (Phase 2).</summary>
    public bool IsPercentageBased { get; init; }

    /// <summary>Reserved: percentage of users (0–100) for whom the flag is active (Phase 2).</summary>
    public int RolloutPercentage { get; init; }
}
