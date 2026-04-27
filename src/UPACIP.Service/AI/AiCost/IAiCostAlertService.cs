namespace UPACIP.Service.AI.AiCost;

/// <summary>
/// Checks daily AI cost totals against configured budget thresholds and
/// emits structured admin alerts when a provider's spend exceeds its limit
/// (US_071 AC-2).
/// </summary>
public interface IAiCostAlertService
{
    /// <summary>
    /// Loads all <c>AiCostDailySummary</c> rows for <paramref name="targetDate"/>,
    /// groups by provider, sums costs, and compares each total against the
    /// corresponding <c>AiCostBudgetConfig.DailyBudgetThreshold</c>.
    /// Calls <see cref="SendBudgetBreachAlertAsync"/> for any provider whose
    /// <c>AlertEnabled</c> flag is <see langword="true"/> and whose cost exceeds
    /// the threshold (US_071 AC-2).
    /// </summary>
    Task CheckBudgetThresholdsAsync(DateOnly targetDate, CancellationToken ct = default);

    /// <summary>
    /// Emits a <c>LogCritical</c> structured event that includes provider name,
    /// threshold, actual cost, and percentage-over-budget so Serilog/Seq can
    /// surface it as an admin alert without requiring an external notification
    /// system (US_071 AC-2).
    /// </summary>
    Task SendBudgetBreachAlertAsync(
        string  provider,
        decimal threshold,
        decimal actualCost,
        CancellationToken ct = default);
}
