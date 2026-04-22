using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Multipart/form-data request model for replacing an existing clinical document (US_042 AC-2).
///
/// The replacement file is received as an <c>IFormFile</c> part and the metadata fields
/// travel alongside it in the same multipart body, matching the initial upload pattern.
/// </summary>
public sealed class ReplaceClinicalDocumentRequest
{
    /// <summary>
    /// The replacement file. Must satisfy the same format and size constraints as the
    /// initial upload (PDF, DOCX, TXT, PNG, JPG/JPEG; max 10 MB).
    /// </summary>
    [Required]
    public IFormFile File { get; init; } = null!;

    /// <summary>Target patient GUID — must match the existing document's patient FK.</summary>
    [Required]
    public Guid PatientId { get; init; }

    /// <summary>
    /// Document category for the replacement.
    /// Should be the same as the original document's category in most cases.
    /// </summary>
    [Required]
    public string Category { get; init; } = string.Empty;

    /// <summary>Optional staff note explaining the reason for the replacement.</summary>
    [MaxLength(500)]
    public string? Notes { get; init; }
}
