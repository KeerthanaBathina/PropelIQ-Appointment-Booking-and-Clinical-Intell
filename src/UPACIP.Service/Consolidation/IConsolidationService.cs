namespace UPACIP.Service.Consolidation;

/// <summary>
/// Contract for the patient profile consolidation service (US_043, FR-052, FR-056).
///
/// Implementations merge extracted clinical data (medications, diagnoses, procedures, allergies)
/// from one or more parsed documents into a unified patient profile, write a versioned
/// <c>PatientProfileVersion</c> record, and deduplicate conflicting entries by confidence score.
///
/// Two consolidation modes:
/// <list type="bullet">
///   <item>
///     <see cref="ConsolidatePatientProfileAsync"/> — Full consolidation over all parsed documents
///     for the patient.  Used on first-time consolidation (ConsolidationType.Initial) and
///     when a full rebuild is explicitly requested.
///   </item>
///   <item>
///     <see cref="IncrementalConsolidateAsync"/> — Merges a specific subset of new documents into
///     an existing profile without re-processing documents that have already been incorporated
///     (AC-4 — no loss of previously verified entries).
///   </item>
/// </list>
/// </summary>
public interface IConsolidationService
{
    /// <summary>
    /// Performs a full consolidation over all parsed documents for <paramref name="patientId"/>.
    ///
    /// <list type="number">
    ///   <item>Loads all non-archived extracted data rows for the patient, sorted by upload date.</item>
    ///   <item>Processes documents in chronological batches of 10 (edge case: 50+ docs).</item>
    ///   <item>Deduplicates each data type using type-specific matching rules.</item>
    ///   <item>Creates a new <c>PatientProfileVersion</c> row with user attribution and source list.</item>
    /// </list>
    /// </summary>
    /// <param name="patientId">Patient whose profile to consolidate.</param>
    /// <param name="triggeredByUserId">
    /// Staff user who initiated this run; NULL for automated pipeline triggers.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Consolidation result including version number, merged counts, and duration.</returns>
    Task<ConsolidationResult> ConsolidatePatientProfileAsync(
        Guid              patientId,
        Guid?             triggeredByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Incrementally merges a specific set of new documents into the patient's existing profile.
    ///
    /// <list type="number">
    ///   <item>Loads pre-verified entries from the latest profile version; these are never overwritten.</item>
    ///   <item>Loads extracted data only from <paramref name="newDocumentIds"/>.</item>
    ///   <item>Deduplicates new data against existing entries, appending non-duplicate new points.</item>
    ///   <item>Creates a new incremental <c>PatientProfileVersion</c> row (AC-2).</item>
    /// </list>
    /// </summary>
    /// <param name="patientId">Patient whose profile to update.</param>
    /// <param name="newDocumentIds">IDs of newly parsed documents to incorporate.</param>
    /// <param name="triggeredByUserId">Staff user who initiated this run; NULL for automated triggers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Consolidation result for the incremental run.</returns>
    Task<ConsolidationResult> IncrementalConsolidateAsync(
        Guid              patientId,
        IReadOnlyList<Guid> newDocumentIds,
        Guid?             triggeredByUserId,
        CancellationToken ct = default);
}
