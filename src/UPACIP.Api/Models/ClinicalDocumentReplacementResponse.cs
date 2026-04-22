namespace UPACIP.Api.Models;

/// <summary>
/// Response returned after a successful document replacement upload (US_042 AC-2).
///
/// The response confirms that the replacement file has been accepted and queued
/// for AI parsing/extraction. The previous document version remains active until
/// the replacement pipeline completes successfully (EC-1).
/// </summary>
public sealed record ClinicalDocumentReplacementResponse
{
    /// <summary>
    /// Document ID of the newly created replacement version.
    /// The frontend uses this to poll parsing status and update the SCR-012 file list.
    /// </summary>
    public Guid NewDocumentId { get; init; }

    /// <summary>
    /// Document ID of the document being replaced (the currently active version).
    /// The previous version remains authoritative until activation completes (EC-1).
    /// </summary>
    public Guid PreviousDocumentId { get; init; }

    /// <summary>1-based version number assigned to the replacement document.</summary>
    public int VersionNumber { get; init; }

    /// <summary>Sanitized original filename of the replacement file.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Document category string value (e.g. LabResult, Prescription).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the replacement was accepted.</summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>Display name of the staff member who initiated the replacement (AC-4 attribution).</summary>
    public string UploadedByName { get; init; } = string.Empty;

    /// <summary>
    /// Current processing lifecycle status of the replacement document.
    /// Will be <c>Uploaded</c> immediately after acceptance; transitions through
    /// <c>Queued → Processing → Completed</c> as the AI pipeline runs.
    /// </summary>
    public string Status { get; init; } = "Uploaded";
}
