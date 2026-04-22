namespace UPACIP.Api.Models;

/// <summary>
/// Response returned to SCR-012 after a successful clinical document upload (US_038 AC-4).
///
/// Contains the metadata the frontend needs to render the attribution row:
/// confirmed filename, upload timestamp, uploader name, and processing status.
/// The server-side file path is intentionally excluded (OWASP A01 — path traversal guard).
/// </summary>
public sealed record ClinicalDocumentUploadResponse
{
    /// <summary>Persisted <c>ClinicalDocument</c> row identifier.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Original filename as submitted by the uploader.
    /// Sanitized — only the file name component is stored, never a path.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Document category saved to the <c>ClinicalDocument</c> record (AC-3).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the document was persisted (AC-4).</summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>Display name of the staff member who uploaded the document (AC-4).</summary>
    public string UploadedByName { get; init; } = string.Empty;

    /// <summary>Processing pipeline state immediately after upload — always <c>Uploaded</c> at this stage.</summary>
    public string Status { get; init; } = "Uploaded";
}
