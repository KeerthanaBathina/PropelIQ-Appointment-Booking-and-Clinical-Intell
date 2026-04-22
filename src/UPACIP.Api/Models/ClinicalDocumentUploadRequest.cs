using UPACIP.DataAccess.Enums;

namespace UPACIP.Api.Models;

/// <summary>
/// Multipart form-data upload request contract for clinical document ingestion (US_038 AC-1, AC-3).
///
/// All fields except <see cref="Notes"/> are required. The file must pass server-side
/// extension/MIME/size validation before any durable storage occurs (AC-1, AC-5).
/// </summary>
public sealed record ClinicalDocumentUploadRequest
{
    /// <summary>Patient the document belongs to — verified against the authenticated caller's role.</summary>
    public Guid PatientId { get; init; }

    /// <summary>
    /// Document category for routing to the AI parser in US_039.
    /// Accepted values: LabResult, Prescription, ClinicalNote, ImagingReport.
    /// </summary>
    public DocumentCategory Category { get; init; }

    /// <summary>Optional free-text note the staff member attaches to the document.</summary>
    public string? Notes { get; init; }
}
