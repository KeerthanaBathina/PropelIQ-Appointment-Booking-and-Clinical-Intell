using UPACIP.Service.AI.ClinicalExtraction;

namespace UPACIP.Service.Documents;

/// <summary>
/// Normalised service-layer result returned by <see cref="IExtractedDataPersistenceService"/>
/// after a clinical extraction envelope has been processed (US_040 AC-1–AC-5, EC-1, EC-2;
/// US_041 AC-1, AC-2, EC-1, EC-2).
///
/// Downstream callers (workers, controllers, event emitters) use this record to:
/// - Display extraction statistics without re-reading raw rows.
/// - Determine whether to surface a manual-review notification (SCR-013).
/// - Count items requiring staff verification by review reason (US_041).
/// </summary>
public sealed record ClinicalExtractionOutcome
{
    /// <summary>Number of medication rows persisted (US_040 AC-1).</summary>
    public int MedicationCount { get; init; }

    /// <summary>Number of diagnosis rows persisted (US_040 AC-2).</summary>
    public int DiagnosisCount { get; init; }

    /// <summary>Number of procedure rows persisted (US_040 AC-3).</summary>
    public int ProcedureCount { get; init; }

    /// <summary>Number of allergy rows persisted (US_040 AC-4).</summary>
    public int AllergyCount { get; init; }

    /// <summary>Total extracted rows added to <c>extracted_data</c>.</summary>
    public int TotalPersisted => MedicationCount + DiagnosisCount + ProcedureCount + AllergyCount;

    /// <summary>Outcome classification from the AI extraction envelope (EC-1, EC-2).</summary>
    public ExtractionOutcome Outcome { get; init; }

    /// <summary>True when the document was flagged for staff intervention.</summary>
    public bool RequiresManualReview { get; init; }

    /// <summary>Human-readable reason for manual review; null when not required.</summary>
    public string? ManualReviewReason { get; init; }

    // ── US_041 confidence-review summary fields ────────────────────────────────

    /// <summary>
    /// Number of persisted rows with confidence below 0.80 (US_041 AC-2).
    /// These rows are flagged for mandatory staff verification.
    /// </summary>
    public int LowConfidenceCount { get; init; }

    /// <summary>
    /// Number of persisted rows where the AI could not assign a confidence score (US_041 EC-1).
    /// Stored with <c>ReviewReason = ConfidenceUnavailable</c> and score defaulted to 0.00.
    /// </summary>
    public int ConfidenceUnavailableCount { get; init; }

    /// <summary>Total rows requiring staff review (LowConfidence + ConfidenceUnavailable).</summary>
    public int TotalFlaggedForReview => LowConfidenceCount + ConfidenceUnavailableCount;
}
