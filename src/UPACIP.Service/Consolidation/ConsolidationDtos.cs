using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Consolidation;

/// <summary>
/// A single merged clinical data point produced by the consolidation pipeline (US_043, AC-1).
///
/// Each instance represents one entry in the patient's unified profile after deduplication
/// across all source documents.
/// </summary>
public sealed record MergedDataPoint
{
    /// <summary>Clinical category of this data point.</summary>
    public DataType DataType { get; init; }

    /// <summary>
    /// Normalized value of the data point (e.g., "Metformin 500mg", "E11.9", "CPT-99213").
    /// Sourced from <c>ExtractedDataContent.NormalizedValue</c>.
    /// </summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Raw text as extracted from the source document.</summary>
    public string? RawText { get; init; }

    /// <summary>Model confidence score [0.0, 1.0] for the retained entry.</summary>
    public float ConfidenceScore { get; init; }

    /// <summary>ID of the <c>ExtractedData</c> row that was retained as canonical for this point.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>ID of the source <c>ClinicalDocument</c> that contributed this entry.</summary>
    public Guid SourceDocumentId { get; init; }

    /// <summary>
    /// True when this data point was already present in the profile and was preserved
    /// (not newly added) during the consolidation run (AC-4 — no overwrite of prior entries).
    /// </summary>
    public bool WasPreexisting { get; init; }

    /// <summary>
    /// True when one or more lower-confidence duplicate entries were discarded in favour of
    /// this data point during deduplication (edge case: identical-data deduplication).
    /// </summary>
    public bool HadDuplicatesRemoved { get; init; }
}

/// <summary>
/// Describes a single deduplication decision made during consolidation.
/// </summary>
public sealed record DeduplicationResult
{
    /// <summary>The data point that was retained (higher confidence score or pre-verified).</summary>
    public MergedDataPoint Retained { get; init; } = null!;

    /// <summary>
    /// IDs of <c>ExtractedData</c> rows that were discarded as lower-confidence duplicates.
    /// </summary>
    public IReadOnlyList<Guid> DiscardedExtractedDataIds { get; init; } = [];

    /// <summary>
    /// Human-readable description of the matching criteria that identified the entries as
    /// duplicates (e.g., "drug_name=Metformin + dosage=500mg").
    /// </summary>
    public string MatchCriteria { get; init; } = string.Empty;
}

/// <summary>
/// Final result returned by <see cref="IConsolidationService"/> after a full or incremental
/// consolidation run (US_043, AC-1, AC-2, AC-4).
/// </summary>
public sealed record ConsolidationResult
{
    /// <summary>ID of the patient whose profile was consolidated.</summary>
    public Guid PatientId { get; init; }

    /// <summary>The new <c>PatientProfileVersion.VersionNumber</c> created by this run.</summary>
    public int NewVersionNumber { get; init; }

    /// <summary>Total number of merged data points in the resulting unified profile.</summary>
    public int TotalMergedCount { get; init; }

    /// <summary>Number of duplicate data points discarded during deduplication.</summary>
    public int DuplicatesRemovedCount { get; init; }

    /// <summary>
    /// Number of data point groups where a conflict was detected (different values from
    /// different documents for the same logical field).  Conflicts are flagged for staff
    /// review but do not block the consolidation from completing.
    /// </summary>
    public int ConflictsDetectedCount { get; init; }

    /// <summary>Number of new data points added beyond what was already in the profile.</summary>
    public int NewDataPointsAddedCount { get; init; }

    /// <summary>IDs of all source documents included in this consolidation run.</summary>
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = [];

    /// <summary>Wall-clock duration of the consolidation run in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Whether this was an initial or incremental consolidation.</summary>
    public bool IsIncremental { get; init; }
}
