namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// Owned type stored as a JSONB column (data_content) in the extracted_data table.
/// Holds the structured clinical data extracted from a document by the AI pipeline.
/// </summary>
public sealed class ExtractedDataContent
{
    /// <summary>Raw extracted text snippet from the source document.</summary>
    public string? RawText { get; set; }

    /// <summary>Normalized value after AI parsing (e.g. dosage, ICD code).</summary>
    public string? NormalizedValue { get; set; }

    /// <summary>Unit of measure when applicable (e.g. "mg", "mmHg").</summary>
    public string? Unit { get; set; }

    /// <summary>Source sentence or paragraph the value was extracted from.</summary>
    public string? SourceSnippet { get; set; }

    /// <summary>Additional key-value metadata produced by the extraction model.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
