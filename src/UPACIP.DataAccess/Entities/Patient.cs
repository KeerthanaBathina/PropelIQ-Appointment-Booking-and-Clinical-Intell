using UPACIP.DataAccess.Entities.OwnedTypes;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Represents a registered patient in the platform.
/// Soft-delete is implemented via the nullable <see cref="DeletedAt"/> sentinel (DR-021).
/// Hard deletes are forbidden to preserve audit trails.
/// </summary>
public sealed class Patient : BaseEntity
{
    /// <summary>Unique patient email address — used as login identifier.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt-hashed password (work factor 10). Never stored in plain text.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Patient's full display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Date of birth used for eligibility and age-based clinical rules.</summary>
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Primary contact phone number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional emergency contact name and/or phone number.</summary>
    public string? EmergencyContact { get; set; }

    /// <summary>
    /// Soft-delete sentinel. Null = active patient; non-null = logically deleted.
    /// Records with a non-null value are excluded by global query filters.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ICollection<Appointment> Appointments { get; set; } = [];

    public ICollection<IntakeData> IntakeRecords { get; set; } = [];

    public ICollection<ClinicalDocument> ClinicalDocuments { get; set; } = [];

    public ICollection<MedicalCode> MedicalCodes { get; set; } = [];
}
