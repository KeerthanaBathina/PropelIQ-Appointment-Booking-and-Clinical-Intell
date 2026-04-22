using UPACIP.DataAccess.Enums;
using UPACIP.Service.Consolidation;

namespace UPACIP.Service.AI.ClinicalExtraction;

/// <summary>
/// Contract for the confidence threshold gate that post-processes AI extraction results
/// (US_046 AC-1, AIR-010, AIR-Q07, AIR-Q08).
///
/// Called after the AI extraction pipeline returns its raw result. The gate:
/// <list type="number">
///   <item>Evaluates each extracted item's confidence score against the 0.80 threshold.</item>
///   <item>Computes aggregate (mean) confidence for the batch.</item>
///   <item>Flags individual entries and the batch when thresholds are exceeded.</item>
///   <item>Returns a <see cref="ConfidenceGateResult"/> consumed by <c>ConsolidationConfidenceService</c>.</item>
/// </list>
/// </summary>
public interface IConfidenceThresholdGate
{
    /// <summary>
    /// Evaluates a list of extracted-data candidate items against the confidence threshold.
    ///
    /// Each item in <paramref name="items"/> must provide a <see cref="ConfidenceEntryInput"/>
    /// including the correlation ID, data type, normalized value, and raw confidence score
    /// reported by the model.
    /// </summary>
    /// <param name="correlationId">Consolidation or document batch correlation ID.</param>
    /// <param name="items">Extracted-data candidates from the AI model.</param>
    /// <returns>
    /// A <see cref="ConfidenceGateResult"/> with per-entry flags, aggregate mean, and
    /// <see cref="ConfidenceGateResult.RequiresBatchManualReview"/>.
    /// </returns>
    ConfidenceGateResult Evaluate(Guid correlationId, IReadOnlyList<ConfidenceEntryInput> items);
}

/// <summary>
/// Input projection for confidence gate evaluation — decouples the gate from EF entity types.
/// </summary>
public sealed record ConfidenceEntryInput
{
    /// <summary>Temporary correlation ID (may be <see cref="Guid.Empty"/> before DB persistence).</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Clinical category.</summary>
    public DataType DataType { get; init; }

    /// <summary>Normalized value (for audit logging only — not evaluated by the gate).</summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Confidence score reported by the AI model. Null is treated as 0.</summary>
    public float? ConfidenceScore { get; init; }
}
