using System.Text.Json.Serialization;

namespace UPACIP.Service.AI.ConflictDetection;

/// <summary>
/// Strongly-typed schema for the <c>analyze_clinical_conflicts</c> tool-call response (US_044, AIR-007, AIR-Q07).
///
/// This schema is the canonical contract between the LLM and the conflict detection service.
/// It extends the base pairwise schema to support multi-source citation chains (3+ documents, Edge Case)
/// and per-conflict-type source citation fields required for the staff review UI (AIR-007).
///
/// The output validator in <see cref="ConflictDetectionService"/> maps parsed instances of this
/// schema into <see cref="ConflictAnalysisResult"/> / <see cref="DetectedConflict"/> domain objects.
/// </summary>
public sealed class ConflictDetectionOutput
{
    /// <summary>True when at least one conflict with confidence ≥ <see cref="MinReportableConfidence"/> was found.</summary>
    [JsonPropertyName("conflicts_detected")]
    public bool ConflictsDetected { get; init; }

    /// <summary>Total number of conflicts returned in the <see cref="Conflicts"/> array.</summary>
    [JsonPropertyName("conflict_count")]
    public int ConflictCount { get; init; }

    /// <summary>
    /// Overall analysis confidence score in [0.0, 1.0].
    /// When below 0.80 the consuming service flags the entire batch for manual verification (AC-4, AIR-010).
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    /// <summary>
    /// Ordered list of detected conflicts, Critical first.
    /// Each item conforms to <see cref="ConflictOutputItem"/> which carries per-type source citations.
    /// </summary>
    [JsonPropertyName("conflicts")]
    public IReadOnlyList<ConflictOutputItem> Conflicts { get; init; } = [];
}

/// <summary>
/// Single conflict entry in the <see cref="ConflictDetectionOutput.Conflicts"/> list.
///
/// Extends the base conflict record to include <see cref="AdditionalSourceIds"/> which enables
/// 3+ document conflicts to surface all involved extraction IDs in the staff review UI (AIR-007, Edge Case).
///
/// Per-type source citation requirements:
/// <list type="table">
///   <listheader><term>Type</term><description>Citation fields populated</description></listheader>
///   <item><term>MedicationDiscrepancy / MedicationContraindication</term>
///         <description><see cref="DataPointAId"/> + <see cref="DataPointBId"/> + <see cref="AdditionalSourceIds"/> (3+ docs)</description></item>
///   <item><term>DuplicateDiagnosis / ConflictingDiagnosis</term>
///         <description><see cref="DataPointAId"/> + <see cref="DataPointBId"/> + <see cref="AdditionalSourceIds"/> (3+ docs)</description></item>
///   <item><term>DateInconsistency</term>
///         <description><see cref="DataPointAId"/> = event with bad date; <see cref="DataPointBId"/> = reference event (may be empty)</description></item>
/// </list>
/// </summary>
public sealed class ConflictOutputItem
{
    /// <summary>
    /// Conflict classification.
    /// Valid values: <c>MedicationContraindication</c>, <c>MedicationDiscrepancy</c>,
    /// <c>DuplicateDiagnosis</c>, <c>ConflictingDiagnosis</c>, <c>DateInconsistency</c>, <c>Duplicate</c>.
    /// </summary>
    [JsonPropertyName("conflict_type")]
    public string ConflictType { get; init; } = string.Empty;

    /// <summary>
    /// Severity band. Valid values: <c>Critical</c>, <c>High</c>, <c>Medium</c>, <c>Low</c>.
    /// Must match <see cref="ConflictSeverity"/> enum names (case-insensitive).
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    /// <summary>
    /// UUID of the primary <c>ExtractedData</c> row involved in the conflict (AIR-007).
    /// Must be a valid UUID present in the analysis input data points.
    /// </summary>
    [JsonPropertyName("data_point_a_id")]
    public string DataPointAId { get; init; } = string.Empty;

    /// <summary>
    /// UUID of the secondary <c>ExtractedData</c> row that conflicts with <see cref="DataPointAId"/> (AIR-007).
    /// May be an empty string for single-entry issues (e.g., future-dated event with no counterpart).
    /// </summary>
    [JsonPropertyName("data_point_b_id")]
    public string DataPointBId { get; init; } = string.Empty;

    /// <summary>
    /// UUIDs of all further <c>ExtractedData</c> rows involved when 3 or more documents contribute
    /// to the same conflict (AIR-007, Edge Case — 3+ document conflicts).
    ///
    /// Empty array for pairwise (2-document) conflicts.
    /// When populated, the staff review UI shows ALL listed sources in the comparison view,
    /// not just the primary pair.
    /// </summary>
    [JsonPropertyName("additional_source_ids")]
    public IReadOnlyList<string> AdditionalSourceIds { get; init; } = [];

    /// <summary>
    /// LLM-generated plain-English explanation of the conflict for the audit trail (AIR-S04).
    ///
    /// MUST contain: conflict type description, specific values/dates involved, clinical entities by name.
    /// MUST NOT contain: patient name, date of birth, contact details, or any PII.
    ///
    /// For <c>DateInconsistency</c> conflicts, this field must describe: (1) violation type,
    /// (2) the dates involved, (3) clinical entities, and (4) expected chronological constraint (AC-5).
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// True when this conflict requires immediate staff review.
    /// Automatically true for all <c>Critical</c> severity conflicts (medication contraindications — AIR-S09).
    /// </summary>
    [JsonPropertyName("requires_urgent_review")]
    public bool RequiresUrgentReview { get; init; }

    /// <summary>
    /// Confidence score for this specific conflict in [0.0, 1.0] (AIR-Q07).
    ///
    /// Calibration guidelines per template:
    /// <list type="bullet">
    ///   <item>0.95–1.00 — Confirmed (identical ICD code, known interaction, mathematically impossible date)</item>
    ///   <item>0.75–0.94 — Likely (strong semantic match, highly plausible interaction)</item>
    ///   <item>0.50–0.74 — Possible (semantic similarity, unusual but not impossible)</item>
    ///   <item>Below 0.50 — Suppressed (not reported)</item>
    /// </list>
    ///
    /// For multi-source conflicts this is the MINIMUM confidence across all contributing pairs.
    /// When the overall <see cref="ConflictDetectionOutput.Confidence"/> is below 0.80,
    /// the consuming service flags the entire batch for manual verification (AC-4, AIR-010).
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }
}

/// <summary>
/// Validation result produced by the output schema validator in <see cref="ConflictDetectionService"/>.
/// Carries the parsed output and any validation warnings for structured logging (AIR-Q07).
/// </summary>
public sealed record ConflictOutputValidationResult
{
    /// <summary>The parsed and validated output, or null when parsing failed entirely.</summary>
    public ConflictDetectionOutput? Output { get; init; }

    /// <summary>True when parsing succeeded and all required fields are present.</summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// True when <see cref="ConflictDetectionOutput.Confidence"/> is below the 0.80 manual-review
    /// threshold (AC-4, AIR-010).
    /// </summary>
    public bool RequiresManualVerification { get; init; }

    /// <summary>
    /// List of validation warnings generated during schema validation (missing fields,
    /// out-of-range values, unrecognised enum strings).
    /// Populated for structured logging; does not block processing.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
