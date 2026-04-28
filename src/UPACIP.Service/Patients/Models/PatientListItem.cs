namespace UPACIP.Service.Patients.Models;

/// <summary>
/// Lightweight patient projection returned by the admin "include deleted" query
/// (US_087 AC-3).
///
/// The <see cref="IsDeleted"/> and <see cref="DeletedAt"/> fields provide the visual
/// indication of soft-deleted status required by AC-3.  All other fields are the same
/// as the standard patient list projection so that UIs can render both active and
/// deleted patients in the same grid without a second data model.
/// </summary>
public sealed class PatientListItem
{
    /// <summary>Patient's unique identifier.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Patient's full display name.</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Patient's email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the patient record was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// <c>true</c> when the patient has been soft-deleted (AC-3 visual indicator).
    /// UI can use this to apply a "deleted" badge, row highlight, or opacity change.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// UTC timestamp when the patient was soft-deleted.
    /// <c>null</c> for active patients.
    /// </summary>
    public DateTime? DeletedAt { get; init; }
}
