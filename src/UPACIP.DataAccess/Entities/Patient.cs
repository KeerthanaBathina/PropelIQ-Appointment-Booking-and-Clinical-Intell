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
    // Contact quality (EP-005 / EC-2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Set to <c>true</c> by the notification service when an email sent to this
    /// patient permanently bounces (5xx SMTP rejection), indicating the stored email
    /// address is invalid and requires staff review.
    /// Cleared by staff through the patient-management workflow once the address is corrected.
    /// </summary>
    public bool ContactUpdateRequired { get; set; }

    /// <summary>
    /// UTC timestamp when <see cref="ContactUpdateRequired"/> was last set to <c>true</c>.
    /// Null when no bounce has been recorded for this patient.
    /// </summary>
    public DateTime? ContactUpdateRequestedAt { get; set; }

    // -------------------------------------------------------------------------
    // SMS preferences (US_033 / AC-2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <c>true</c>, the patient has opted out of all SMS notifications.
    /// The notification service MUST skip SMS delivery and log <c>OptedOut</c>
    /// instead of invoking the Twilio transport (AC-2, EC-2).
    /// Defaults to <c>false</c> — patients are SMS-eligible unless they explicitly opt out.
    /// </summary>
    public bool SmsOptedOut { get; set; }

    /// <summary>
    /// UTC timestamp when <see cref="SmsOptedOut"/> was last set to <c>true</c>.
    /// Null when the patient has never opted out or has re-enrolled.
    /// </summary>
    public DateTime? SmsOptedOutAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ICollection<Appointment> Appointments { get; set; } = [];

    public ICollection<IntakeData> IntakeRecords { get; set; } = [];

    public ICollection<ClinicalDocument> ClinicalDocuments { get; set; } = [];

    public ICollection<MedicalCode> MedicalCodes { get; set; } = [];
}
