using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Orchestrates the full document replacement lifecycle (US_042 AC-2, AC-3, EC-1, EC-2):
///
///   1. Validate and store the replacement file via the encrypted storage service.
///   2. Create a new <c>ClinicalDocument</c> version linked to the previous version.
///   3. Enqueue the replacement document for AI parsing + extraction.
///   4. Keep the prior version active until the replacement pipeline completes (EC-1).
///
/// The activation step (mark old as Superseded, archive old extracted rows, emit
/// reconsolidation-needed signal) is called by <c>ExtractedDataPersistenceService</c>
/// after successful replacement extraction persistence (AC-3).
/// </summary>
public interface IDocumentReplacementService
{
    /// <summary>
    /// Accepts a replacement file for an existing document, stores it encrypted, creates a new
    /// version row, and enqueues the replacement for AI parsing.
    ///
    /// The previous document version and its extracted rows remain active while the replacement
    /// processes, and are only superseded/archived after successful extraction (EC-1).
    ///
    /// Returns a <see cref="DocumentReplacementResult"/> on success.
    /// Throws <see cref="DocumentValidationException"/> for format/size failures (shared validation).
    /// Throws <see cref="InvalidOperationException"/> when the target document does not exist or
    /// is not in an activatable state.
    /// </summary>
    /// <param name="previousDocumentId">ID of the existing active document to replace.</param>
    /// <param name="fileStream">Readable stream of the replacement file bytes.</param>
    /// <param name="fileName">Original filename of the replacement file.</param>
    /// <param name="contentType">MIME type of the replacement file.</param>
    /// <param name="fileLength">File size in bytes for size validation before reading.</param>
    /// <param name="patientId">Patient the document belongs to.</param>
    /// <param name="category">Clinical category for the replacement document.</param>
    /// <param name="uploaderUserId">Authenticated staff user performing the replacement.</param>
    /// <param name="uploaderDisplayName">Display name for attribution in the response.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    Task<DocumentReplacementResult> StartReplacementAsync(
        Guid              previousDocumentId,
        Stream            fileStream,
        string            fileName,
        string            contentType,
        long              fileLength,
        Guid              patientId,
        DocumentCategory  category,
        Guid              uploaderUserId,
        string            uploaderDisplayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the replacement document after its parsing and extraction pipeline succeeds:
    ///   - Marks the previous version as <c>Superseded</c>.
    ///   - Archives all extracted rows of the previous version.
    ///   - Sets <c>ReconsolidationNeeded = true</c> on the new active document (EC-2).
    ///
    /// Called exclusively by <c>ExtractedDataPersistenceService</c> after successful
    /// replacement extraction persistence (AC-3).
    ///
    /// If the previous version is not found or is already superseded, the call is a safe no-op.
    /// </summary>
    /// <param name="newDocumentId">The replacement document that just completed extraction.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    Task ActivateReplacementAsync(
        Guid              newDocumentId,
        CancellationToken cancellationToken = default);
}

// ─── Result record ────────────────────────────────────────────────────────────

/// <summary>
/// Result of a successful <see cref="IDocumentReplacementService.StartReplacementAsync"/> call.
/// </summary>
public sealed record DocumentReplacementResult
{
    public Guid     NewDocumentId      { get; init; }
    public Guid     PreviousDocumentId { get; init; }
    public int      VersionNumber      { get; init; }
    public string   FileName           { get; init; } = string.Empty;
    public string   Category           { get; init; } = string.Empty;
    public DateTime UploadedAt         { get; init; }
    public string   UploadedByName     { get; init; } = string.Empty;
    public string   Status             { get; init; } = "Uploaded";
}
