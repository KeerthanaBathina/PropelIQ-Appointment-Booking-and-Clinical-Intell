using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Per-provider AI cost budget configuration and rate card (US_071 TASK_001, AC-1, AC-2).
///
/// <para>
/// One row per <see cref="AiProvider"/> (enforced by a unique constraint on the
/// <see cref="Provider"/> column).  Each row serves two related purposes:
/// <list type="number">
///   <item>
///     <strong>Budget threshold</strong> — <see cref="DailyBudgetThreshold"/> (USD/day).
///     When the daily cost for this provider exceeds the threshold the alert service
///     generates an admin notification if <see cref="AlertEnabled"/> is <c>true</c> (AC-2).
///   </item>
///   <item>
///     <strong>Rate card fallback</strong> — <see cref="CostPer1kInputTokens"/> and
///     <see cref="CostPer1kOutputTokens"/> are used to estimate request cost when the
///     provider API does not return cost data, flagging the result as
///     <see cref="AiCostSource.Approximate"/> in <see cref="AiRequestLog"/> (edge case).
///   </item>
/// </list>
/// </para>
///
/// <para>Default seed data (inserted by migration <c>AddAiCostTrackingTables</c>):</para>
/// <list type="bullet">
///   <item>OpenAI GPT-4o-mini: $0.00015/1K input, $0.00060/1K output, $5.00/day budget.</item>
///   <item>Anthropic Claude 3.5 Sonnet: $0.00300/1K input, $0.01500/1K output, $20.00/day budget.</item>
/// </list>
///
/// Extends <see cref="BaseEntity"/> for <c>Id</c>, <c>CreatedAt</c>, and <c>UpdatedAt</c>.
/// </summary>
public sealed class AiCostBudgetConfig : BaseEntity
{
    /// <summary>
    /// AI provider this configuration applies to.
    /// Unique constraint — one configuration row per provider.
    /// </summary>
    public AiProvider Provider { get; set; }

    /// <summary>
    /// Maximum acceptable daily estimated cost (USD) for this provider.
    /// The alert service compares today's total <see cref="AiRequestLog.EstimatedCost"/>
    /// aggregation against this value and fires an admin notification on breach (AC-2).
    /// Precision: <c>numeric(10,2)</c> — supports budgets up to $99,999,999.99/day.
    /// </summary>
    public decimal DailyBudgetThreshold { get; set; }

    /// <summary>
    /// When <c>true</c>, the alert service sends an admin notification whenever the
    /// daily estimated cost exceeds <see cref="DailyBudgetThreshold"/> (AC-2).
    /// Default: <c>true</c>.
    /// </summary>
    public bool AlertEnabled { get; set; } = true;

    /// <summary>
    /// Estimated cost per 1 000 input (prompt) tokens in USD.
    /// Used as the rate card fallback when provider API cost data is unavailable.
    /// Example: GPT-4o-mini → $0.00015 (= $0.15 / 1 000 000 tokens).
    /// Precision: <c>numeric(10,6)</c>.
    /// </summary>
    public decimal CostPer1kInputTokens { get; set; }

    /// <summary>
    /// Estimated cost per 1 000 output (completion) tokens in USD.
    /// Example: GPT-4o-mini → $0.00060 (= $0.60 / 1 000 000 tokens).
    /// Precision: <c>numeric(10,6)</c>.
    /// </summary>
    public decimal CostPer1kOutputTokens { get; set; }
}
