using UPACIP.Service.Consolidation;

namespace UPACIP.Service.AI.ConflictDetection;

/// <summary>
/// Contract for the AI-powered clinical conflict detection service (US_043, AC-1, AIR-005).
///
/// After the consolidation service deduplicates extracted data, this service performs
/// a deeper semantic analysis using GPT-4o-mini (primary) with Claude 3.5 Sonnet fallback
/// to identify medication contraindications, conflicting diagnoses, chronologically
/// implausible events, and near-duplicates.
///
/// RAG grounding (AIR-R02):
///   Top-5 medical terminology chunks from pgvector (cosine similarity ≥ 0.75) are
///   retrieved and included in the prompt to improve contraindication accuracy.
///
/// Safety (AIR-S09):
///   Critical-severity conflicts (medication contraindications) set
///   <see cref="DetectedConflict.RequiresUrgentReview"/> = true so the caller can trigger
///   urgent staff notification workflows.
///
/// Resilience (AIR-O04, AIR-O08):
///   A Polly circuit breaker opens after 5 consecutive AI provider failures (30-second break).
///   Exponential backoff retries 3 times before falling back to Anthropic, then to an
///   empty <see cref="ConflictAnalysisResult"/> so the consolidation pipeline never blocks.
/// </summary>
public interface IConflictDetectionService
{
    /// <summary>
    /// Analyzes merged clinical data points for conflicts and contraindications.
    ///
    /// The analysis is always non-blocking — if both AI providers fail, an empty
    /// <see cref="ConflictAnalysisResult"/> is returned so the consolidation pipeline
    /// can complete successfully.
    ///
    /// PII is redacted from prompts before any API call (AIR-S01).
    /// All prompts and responses are logged to the structured audit trail with
    /// correlation IDs (AIR-S04 — PII never appears in log events).
    /// </summary>
    /// <param name="mergedDataPoints">
    /// The full set of merged and deduplicated data points from the current consolidation run.
    /// </param>
    /// <param name="patientId">
    /// Used only as an opaque correlation ID in audit logs — never included in AI prompts.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConflictAnalysisResult"/> containing all detected conflicts sorted by
    /// severity (Critical first). Returns <see cref="ConflictAnalysisResult.Empty"/> when
    /// all providers fail.
    /// </returns>
    Task<ConflictAnalysisResult> DetectConflictsAsync(
        IReadOnlyList<MergedDataPoint> mergedDataPoints,
        Guid                           patientId,
        CancellationToken              ct = default);
}
