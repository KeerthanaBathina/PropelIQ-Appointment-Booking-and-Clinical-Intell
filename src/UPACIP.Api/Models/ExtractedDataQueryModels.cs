namespace UPACIP.Api.Models;

/// <summary>
/// Single extracted clinical data row response returned by GET /api/extracted-data queries.
/// Carries all fields SCR-012 and SCR-013 need to render confidence badges, review indicators,
/// and verification status without reloading the full document pipeline state.
/// </summary>
public sealed record ExtractedDataRow
{
    public Guid ExtractedDataId { get; init; }
    public Guid DocumentId { get; init; }
    public string DataType { get; init; } = string.Empty;
    public ExtractedDataContentDto? DataContent { get; init; }

    /// <summary>Confidence score in [0, 1], or null when the AI could not assign one (EC-1).</summary>
    public float? ConfidenceScore { get; init; }

    public bool FlaggedForReview { get; init; }
    public string ReviewReason { get; init; } = string.Empty;
    public string VerificationStatus { get; init; } = string.Empty;
    public DateTime? VerifiedAtUtc { get; init; }
    public string? VerifiedByName { get; init; }
    public int PageNumber { get; init; }
    public string ExtractionRegion { get; init; } = string.Empty;
}

/// <summary>
/// Flattened projection of <c>ExtractedDataContent</c> JSONB for API responses.
/// </summary>
public sealed record ExtractedDataContentDto
{
    public string? RawText { get; init; }
    public string? NormalizedValue { get; init; }
    public string? Unit { get; init; }
    public string? SourceSnippet { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Response for GET /api/extracted-data/flagged-counts.
/// Returns remaining pending-review counts keyed by document GUID string.
/// </summary>
public sealed record FlaggedCountsResponse
{
    public IReadOnlyDictionary<string, int> FlaggedCounts { get; init; } =
        new Dictionary<string, int>();
}
