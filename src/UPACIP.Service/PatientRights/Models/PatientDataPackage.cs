namespace UPACIP.Service.PatientRights.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Top-level aggregated export package (AC-1, AC-2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated patient data package containing all six data categories for export (US_094, AC-1).
/// Internal fields (password_hash, deleted_at, version) are intentionally excluded.
/// </summary>
public sealed class PatientDataPackage
{
    public PatientProfileData Profile { get; set; } = new();
    public List<AppointmentData> Appointments { get; set; } = new();
    public List<IntakeDataRecord> IntakeRecords { get; set; } = new();
    public List<ClinicalDocumentData> ClinicalDocuments { get; set; } = new();
    public List<MedicalCodeData> MedicalCodes { get; set; } = new();
    public Guid PatientId { get; set; }
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string ExportVersion { get; set; } = "1.0";
}

// ─────────────────────────────────────────────────────────────────────────────
// Patient profile
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Patient demographic data for export — excludes password_hash and deleted_at.</summary>
public sealed class PatientProfileData
{
    public Guid PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? EmergencyContact { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Appointments
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Appointment export record including nested queue entries and notification history.</summary>
public sealed class AppointmentData
{
    public Guid AppointmentId { get; set; }
    public string? BookingReference { get; set; }
    public DateTime AppointmentTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsWalkIn { get; set; }
    public string? ProviderName { get; set; }
    public string? AppointmentType { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<QueueEntryData> QueueEntries { get; set; } = new();
    public List<NotificationLogData> NotificationLogs { get; set; } = new();
}

/// <summary>Queue entry data for an appointment.</summary>
public sealed class QueueEntryData
{
    public Guid QueueEntryId { get; set; }
    public DateTime ArrivalTimestamp { get; set; }
    public int WaitTimeMinutes { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>Notification delivery log entry for an appointment.</summary>
public sealed class NotificationLogData
{
    public Guid NotificationId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string DeliveryChannel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Intake records
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Intake form record export — exposes intake method and submitted field data.</summary>
public sealed class IntakeDataRecord
{
    public Guid IntakeDataId { get; set; }
    public string IntakeMethod { get; set; } = string.Empty;
    public object? MandatoryFields { get; set; }
    public object? OptionalFields { get; set; }
    public object? InsuranceInfo { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Clinical documents + extracted data
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Clinical document export record with nested extracted data items.</summary>
public sealed class ClinicalDocumentData
{
    public Guid DocumentId { get; set; }
    public string DocumentCategory { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public List<ExtractedDataRecord> ExtractedData { get; set; } = new();
}

/// <summary>Extracted clinical data item from an AI-parsed document.</summary>
public sealed class ExtractedDataRecord
{
    public Guid ExtractedDataId { get; set; }
    public string DataType { get; set; } = string.Empty;
    public object? DataContent { get; set; }
    public float ConfidenceScore { get; set; }
    public float? CalibratedConfidenceScore { get; set; }
    public string SourceAttribution { get; set; } = string.Empty;
    public int PageNumber { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Medical codes
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Medical code (ICD-10 / CPT) export record.</summary>
public sealed class MedicalCodeData
{
    public Guid MedicalCodeId { get; set; }
    public string CodeType { get; set; } = string.Empty;
    public string CodeValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public bool SuggestedByAi { get; set; }
    public float? AiConfidenceScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Clinical data package (used internally by ClinicalDataCollector)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Aggregated result from the clinical data collector.</summary>
public sealed class ClinicalDataPackage
{
    public List<IntakeDataRecord> IntakeRecords { get; set; } = new();
    public List<ClinicalDocumentData> ClinicalDocuments { get; set; } = new();
    public List<MedicalCodeData> MedicalCodes { get; set; } = new();
}
