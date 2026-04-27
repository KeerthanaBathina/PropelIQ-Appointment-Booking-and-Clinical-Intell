namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classification of how well an AI-generated medical justification is supported by the
/// source clinical documents reviewed by staff (US_074 task_002, AC-1, AIR-Q06).
///
/// <para>
/// Staff assign this classification when verifying each AI justification against the
/// source document linked via <c>ExtractedData.SourceAttribution</c>.  A value of
/// <see cref="Unsupported"/> indicates a hallucination — data present in the AI output
/// that is not present in the source document.
/// </para>
/// </summary>
public enum SourceSupportStatus
{
    /// <summary>
    /// The AI justification is fully backed by content in the source clinical document.
    /// </summary>
    Supported = 1,

    /// <summary>
    /// The AI justification contains data not found in the source document — a hallucination.
    /// Counted in the daily hallucination rate calculation as per AC-1.
    /// </summary>
    Unsupported = 2,

    /// <summary>
    /// The AI justification is partially backed by the source document; some content is
    /// supported and some is not.  Not counted as a full hallucination in rate calculations.
    /// </summary>
    PartiallySupported = 3,

    /// <summary>
    /// The justification has not yet been reviewed by staff.  Default state for newly
    /// generated AI codes.
    /// </summary>
    Pending = 4,
}
