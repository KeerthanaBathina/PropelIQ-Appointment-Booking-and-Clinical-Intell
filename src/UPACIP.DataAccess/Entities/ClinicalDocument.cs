using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Represents a clinical document (lab result, prescription, imaging report, etc.)
/// uploaded by a staff member or patient.  Document content is stored on the file system
/// at <see cref="FilePath"/>; only metadata is persisted in PostgreSQL.
/// </summary>
public sealed class ClinicalDocument : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> this document belongs to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Clinical category used to route the document to the correct AI parser.</summary>
    public DocumentCategory DocumentCategory { get; set; }

    /// <summary>
    /// Relative or absolute server-side file path.
    /// Must never be returned to clients without authorization checks (path-traversal guard).
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the document was uploaded.</summary>
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;

    /// <summary>FK to the <see cref="ApplicationUser"/> who uploaded the document.</summary>
    public Guid UploaderUserId { get; set; }

    /// <summary>Current AI processing pipeline state for this document.</summary>
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Queued;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;

    public ApplicationUser UploaderUser { get; set; } = null!;

    public ICollection<ExtractedData> ExtractedData { get; set; } = [];
}
