using UPACIP.Service.AI.ConflictDetection;

namespace UPACIP.Service.Conflict;

/// <summary>
/// Contract for the clinical conflict management service (US_044, AC-1, AC-3, AC-4, FR-053).
///
/// Orchestrates the full conflict lifecycle:
/// <list type="bullet">
///   <item>Persisting AI-detected conflicts from <see cref="IConflictDetectionService"/> results.</item>
///   <item>Escalating medication contraindication conflicts to urgent status (AC-3).</item>
///   <item>Flagging low-confidence batches for manual verification (AC-4).</item>
///   <item>Resolving or dismissing conflicts with staff attribution.</item>
///   <item>Re-evaluating conflicts on new document upload without reopening resolved items.</item>
///   <item>Returning the sorted conflict review queue for staff dashboards.</item>
/// </list>
/// </summary>
public interface IConflictManagementService
{
    /// <summary>
    /// Persists the AI-detected conflicts from <paramref name="aiResult"/> into the
    /// <c>clinical_conflicts</c> table for the given patient and profile version (AC-1, AC-3).
    ///
    /// Behaviour:
    /// <list type="bullet">
    ///   <item>Maps each <see cref="DetectedConflict"/> to a <c>ClinicalConflict</c> row.</item>
    ///   <item>Aggregates conflicts that share the same type and involve 3+ source documents
    ///         into a single record with all source IDs in the JSONB arrays (Edge Case).</item>
    ///   <item>Automatically calls <see cref="EscalateUrgentConflictsAsync"/> for the batch
    ///         to promote contraindication conflicts (AC-3).</item>
    ///   <item>When any conflict in the batch has confidence &lt; 0.80, marks all batch
    ///         conflicts as <c>UnderReview</c> and returns <c>RequiresManualReview = true</c> (AC-4).</item>
    /// </list>
    /// </summary>
    /// <param name="aiResult">The envelope result from <see cref="IConflictDetectionService"/>.</param>
    /// <param name="patientId">Patient whose profile is being consolidated.</param>
    /// <param name="profileVersionId">The profile version created by this consolidation run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of new conflict rows persisted and whether manual review is required.</returns>
    Task<(int PersistedCount, bool RequiresManualReview)> PersistDetectedConflictsAsync(
        ConflictAnalysisResult aiResult,
        Guid                   patientId,
        Guid                   profileVersionId,
        CancellationToken      ct = default);

    /// <summary>
    /// Escalates unresolved <c>MedicationContraindication</c> conflicts for the given patient
    /// to urgent status (<c>is_urgent = true</c>) so they appear at the top of the review queue (AC-3).
    ///
    /// Only conflicts with <c>Status = Detected</c> or <c>UnderReview</c> are affected;
    /// already-resolved or dismissed conflicts are not modified.
    /// </summary>
    /// <param name="patientId">Patient whose open contraindication conflicts should be escalated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Escalation result with counts and IDs of promoted conflicts.</returns>
    Task<ConflictEscalationResult> EscalateUrgentConflictsAsync(
        Guid              patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the specified conflict as <c>Resolved</c> with staff attribution and notes (FR-053).
    ///
    /// Sets <c>status = Resolved</c>, <c>resolved_by_user_id</c>, <c>resolution_notes</c>,
    /// and <c>resolved_at = UtcNow</c>. Invalidates the patient's Redis-cached profile.
    ///
    /// Throws <see cref="InvalidOperationException"/> when the conflict is already resolved
    /// or does not exist.
    /// </summary>
    /// <param name="request">Resolution details including conflict ID, user, and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResolveConflictAsync(ConflictResolutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Marks the specified conflict as <c>Dismissed</c> (false positive) with staff attribution (FR-053).
    ///
    /// Sets <c>status = Dismissed</c>, <c>resolved_by_user_id</c>, <c>resolution_notes</c>,
    /// and <c>resolved_at = UtcNow</c>. Invalidates the patient's Redis-cached profile.
    ///
    /// Throws <see cref="InvalidOperationException"/> when the conflict is already closed
    /// or does not exist.
    /// </summary>
    /// <param name="request">Dismissal details including conflict ID, user, and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DismissConflictAsync(ConflictResolutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Re-evaluates whether new document data contradicts any previously resolved conflicts
    /// for the patient (Edge Case — resolved conflict preservation).
    ///
    /// Only conflicts with <c>Status = Resolved</c> or <c>Dismissed</c> are evaluated.
    /// A resolved conflict is reopened (<c>Status = Detected</c>) only when the new
    /// extracted data directly contradicts the resolution — determined by comparing
    /// the new extracted data IDs against the conflict's <c>source_extracted_data_ids</c>.
    ///
    /// Newly detected conflicts from the new documents are NOT created here; they are
    /// created by <see cref="PersistDetectedConflictsAsync"/> called from ConsolidationService.
    /// </summary>
    /// <param name="patientId">Patient whose resolved conflicts should be re-evaluated.</param>
    /// <param name="newDocumentIds">IDs of the newly uploaded/parsed documents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of previously resolved conflicts that were reopened.</returns>
    Task<int> ReEvaluateOnNewDocumentAsync(
        Guid              patientId,
        IReadOnlyList<Guid> newDocumentIds,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paged, urgency-sorted conflict review queue for staff dashboards (AC-3, FR-053).
    ///
    /// Sort order: urgent items first (is_urgent = true), then by <c>created_at DESC</c>.
    /// Only conflicts with <c>Status = Detected</c> or <c>UnderReview</c> are included.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paged review queue result.</returns>
    Task<ReviewQueuePage> GetReviewQueueAsync(
        int               page,
        int               pageSize,
        CancellationToken ct = default);
}
