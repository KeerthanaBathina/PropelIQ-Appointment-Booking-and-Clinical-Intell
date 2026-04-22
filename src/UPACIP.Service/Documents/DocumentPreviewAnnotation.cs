namespace UPACIP.Service.Documents;

/// <summary>
/// Single extraction annotation returned as part of <see cref="DocumentPreviewResponse"/>.
///
/// For document formats that support bounding-box overlays (PDF, PNG, JPG/JPEG), the
/// <see cref="Bounds"/> property is populated with fractional [0, 1] coordinates so the
/// frontend can position highlight boxes without knowing the original pixel dimensions
/// at serialization time (AC-1).
///
/// For text-only formats (TXT) or when the AI extraction did not report coordinates,
/// <see cref="Bounds"/> is null and the frontend falls back to inline annotation display (EC-1).
/// </summary>
public sealed record DocumentPreviewAnnotation
{
    /// <summary>PK of the <c>ExtractedData</c> row this annotation references.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical category of the extracted datum (Medication, Diagnosis, Procedure, Allergy).</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable primary label for tooltip display.
    /// Derived from <c>NormalizedValue</c> when present, otherwise <c>RawText</c>.
    /// Never null — at minimum an empty string (AC-4).
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Model confidence score in [0, 1].
    /// Null when the AI could not assign a score (<c>ReviewReason = ConfidenceUnavailable</c>) (AC-4, EC-1).
    /// </summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>Structured review reason string (maps to <c>ReviewReason</c> enum value).</summary>
    public string ReviewReason { get; init; } = string.Empty;

    /// <summary>Verification status string (maps to <c>VerificationStatus</c> enum value).</summary>
    public string VerificationStatus { get; init; } = string.Empty;

    /// <summary>1-based page number within the document where this data was found.</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>Coarse region label from the extraction pipeline (e.g. "table row 3").</summary>
    public string ExtractionRegion { get; init; } = string.Empty;

    /// <summary>
    /// Source text snippet from the original document for context in the tooltip (AC-4).
    /// Null when <c>ExtractedDataContent.SourceSnippet</c> is not populated.
    /// </summary>
    public string? SourceSnippet { get; init; }

    /// <summary>
    /// Fractional bounding box coordinates in [0, 1] relative to the page dimensions.
    /// Present only when the document format supports spatial highlights and the AI pipeline
    /// reported coordinates (AC-1).
    /// Null for text-only formats or when coordinates are unavailable (EC-1).
    /// </summary>
    public DocumentAnnotationBounds? Bounds { get; init; }
}

/// <summary>
/// Fractional bounding box for a single extraction annotation.
/// All values are in the range [0, 1] representing a fraction of the page width or height.
/// </summary>
public sealed record DocumentAnnotationBounds
{
    /// <summary>Left edge as a fraction of page width.</summary>
    public float X { get; init; }

    /// <summary>Top edge as a fraction of page height.</summary>
    public float Y { get; init; }

    /// <summary>Box width as a fraction of page width.</summary>
    public float Width { get; init; }

    /// <summary>Box height as a fraction of page height.</summary>
    public float Height { get; init; }
}
