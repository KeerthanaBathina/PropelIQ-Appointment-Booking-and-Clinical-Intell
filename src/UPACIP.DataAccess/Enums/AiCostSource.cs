namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Indicates whether the estimated cost recorded in <c>AiRequestLog</c> was
/// sourced directly from the provider's API response or was calculated from
/// the token count multiplied by the configured rate card (US_071 TASK_001, edge case).
///
/// Approximate entries are flagged to alert cost-monitoring consumers that the
/// figures may deviate from actual provider billing.
///
/// Stored as a string via EF Core <c>HasConversion&lt;string&gt;()</c>.
/// </summary>
public enum AiCostSource
{
    /// <summary>
    /// Cost was reported directly by the provider API response.
    /// No estimation error.
    /// </summary>
    Actual = 1,

    /// <summary>
    /// Cost was estimated using <c>token_count × configured_rate_card</c> because
    /// the provider API did not return cost data.
    /// Flagged as approximate in cost reports and dashboard visualisations.
    /// </summary>
    Approximate = 2,
}
