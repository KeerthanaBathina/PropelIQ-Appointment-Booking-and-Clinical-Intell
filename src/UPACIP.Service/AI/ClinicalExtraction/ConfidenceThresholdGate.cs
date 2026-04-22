using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.ClinicalExtraction;

/// <summary>
/// Evaluates AI extraction result confidence scores against the 0.80 threshold
/// (US_046 AC-1, AIR-010, AIR-Q07, AIR-Q08).
///
/// Design:
///   - Threshold is <c>0.80</c> — matches <c>consolidation-guardrails.json § ConfidenceThreshold.AutoApproveAbove</c>
///     and the existing <c>ClinicalExtractionService</c> confidence tier documentation.
///   - Null scores are treated as 0 per guardrails.json § <c>PerItemConfidence.NullDefaultScore</c>.
///   - Aggregate mean is calculated over effective scores (post-null-normalisation).
///   - <see cref="ConfidenceGateResult.RequiresBatchManualReview"/> is true when mean &lt; 0.80
///     OR any item has a null score (fail-safe: unknown confidence → mandatory review).
///   - This class is pure computation with no I/O — registered Singleton in DI.
/// </summary>
public sealed class ConfidenceThresholdGate : IConfidenceThresholdGate
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Auto-approve threshold (AC-1, AIR-010). Items with effective scores ≥ this value
    /// are approved; items below are flagged for manual review.
    /// </summary>
    public const float Threshold = 0.80f;

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<ConfidenceThresholdGate> _logger;

    public ConfidenceThresholdGate(ILogger<ConfidenceThresholdGate> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConfidenceThresholdGate — Evaluate
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ConfidenceGateResult Evaluate(Guid correlationId, IReadOnlyList<ConfidenceEntryInput> items)
    {
        if (items.Count == 0)
        {
            _logger.LogDebug(
                "ConfidenceThresholdGate: no items to evaluate. CorrelationId={Id}", correlationId);
            return ConfidenceGateResult.Empty(correlationId);
        }

        // Build per-entry results
        var entries = items
            .OrderBy(i => i.DataType)
            .ThenBy(i => i.ConfidenceScore ?? 0f)
            .Select(i => new ConfidenceEntryResult
            {
                CorrelationId  = i.CorrelationId,
                DataType       = i.DataType,
                NormalizedValue = i.NormalizedValue,
                ConfidenceScore = i.ConfidenceScore,
            })
            .ToList();

        // Aggregate mean confidence (null → 0 per guardrails.json)
        var mean = entries.Count > 0
            ? entries.Average(e => e.EffectiveScore)
            : 1f;

        var hasNullScore          = entries.Any(e => e.HasNullScore);
        var requiresBatchReview   = (float)mean < Threshold || hasNullScore;

        var result = new ConfidenceGateResult
        {
            CorrelationId           = correlationId,
            Entries                 = entries,
            MeanConfidence          = mean,
            RequiresBatchManualReview = requiresBatchReview,
        };

        _logger.LogInformation(
            "ConfidenceThresholdGate: evaluation complete. CorrelationId={Id}, " +
            "Total={Total}, Flagged={Flagged}, MeanConfidence={Mean:F2}, BatchReview={Batch}",
            correlationId, result.TotalCount, result.FlaggedCount, mean, requiresBatchReview);

        return result;
    }
}
