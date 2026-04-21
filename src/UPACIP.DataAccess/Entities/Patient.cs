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
    // Auto-swap controls (US_021, AC-3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true the system may automatically swap this patient to a preferred slot.
    /// Defaults to true — all patients are eligible unless staff explicitly disables.
    /// </summary>
    public bool AutoSwapEnabled { get; set; } = true;

    /// <summary>
    /// Human-readable reason recorded when staff disables auto-swap (AC-3).
    /// Null when auto-swap is enabled or has never been disabled.
    /// </summary>
    public string? AutoSwapDisabledReason { get; set; }

    /// <summary>UTC timestamp when auto-swap was last disabled by staff. Null when enabled.</summary>
    public DateTime? AutoSwapDisabledAtUtc { get; set; }

    /// <summary>
    /// ApplicationUser.Id of the staff member who disabled auto-swap (audit trail, AC-3).
    /// Null when auto-swap is enabled or was never manually overridden.
    /// </summary>
    public Guid? AutoSwapDisabledByUserId { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ICollection<Appointment> Appointments { get; set; } = [];

    public ICollection<IntakeData> IntakeRecords { get; set; } = [];

    public ICollection<ClinicalDocument> ClinicalDocuments { get; set; } = [];

    public ICollection<MedicalCode> MedicalCodes { get; set; } = [];
}
