using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

// ─── Patient search ───────────────────────────────────────────────────────────

/// <summary>
/// Request payload for staff patient-search endpoint (US_022 AC-2).
/// Staff may search by name, date-of-birth, or phone number.
/// </summary>
public sealed record WalkInPatientSearchRequest
{
    /// <summary>
    /// Search term entered by staff.
    /// Interpreted as a partial name match, ISO-8601 DOB (YYYY-MM-DD), or phone number
    /// depending on <see cref="Field"/>.
    /// Min 2 characters required to prevent full-table scans.
    /// </summary>
    [Required(ErrorMessage = "Search term is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Search term must be 2-100 characters.")]
    public string Term { get; init; } = string.Empty;

    /// <summary>
    /// Field to search against. Defaults to <c>name</c>.
    /// Accepted values: name | dob | phone.
    /// </summary>
    [RegularExpression("^(name|dob|phone)$", ErrorMessage = "Field must be 'name', 'dob', or 'phone'.")]
    public string Field { get; init; } = "name";
}

/// <summary>
/// Lightweight patient record returned in staff search results (US_022 AC-2).
/// Contains only the fields needed for staff to identify and select the correct patient.
/// Email is excluded from search results — surfaced only after a patient record is selected.
/// </summary>
public sealed record WalkInPatientSearchResult(
    /// <summary>Unique patient identifier used in subsequent booking request.</summary>
    string PatientId,
    /// <summary>Full display name.</summary>
    string FullName,
    /// <summary>ISO-8601 date of birth (YYYY-MM-DD).</summary>
    string DateOfBirth,
    /// <summary>Primary contact phone number.</summary>
    string Phone,
    /// <summary>Email address (needed by FE to pre-fill new-patient form on match).</summary>
    string Email);

// ─── Walk-in booking ──────────────────────────────────────────────────────────

/// <summary>
/// Minimal new-patient data supplied when staff registers an unrecognised walk-in
/// and no existing patient record is selected (US_022 AC-2 fallback).
/// </summary>
public sealed record NewWalkInPatientRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100, ErrorMessage = "Full name must not exceed 100 characters.")]
    public string FullName { get; init; } = string.Empty;

    /// <summary>ISO-8601 date of birth (YYYY-MM-DD).</summary>
    [Required(ErrorMessage = "Date of birth is required.")]
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "DateOfBirth must be in YYYY-MM-DD format.")]
    public string DateOfBirth { get; init; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [StringLength(20, ErrorMessage = "Phone must not exceed 20 characters.")]
    public string Phone { get; init; } = string.Empty;

    /// <summary>Email used as the patient login identifier after inline creation.</summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(256, ErrorMessage = "Email must not exceed 256 characters.")]
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Booking payload for a staff-initiated walk-in appointment (US_022 AC-3).
///
/// Either <see cref="PatientId"/> (existing patient) or <see cref="NewPatient"/> (inline creation)
/// must be provided — not both.  Server validates this constraint.
/// </summary>
public sealed record WalkInBookingRequest
{
    /// <summary>
    /// UUID of an existing patient record. Null when staff is creating a new patient inline.
    /// Mutually exclusive with <see cref="NewPatient"/>.
    /// </summary>
    public Guid? PatientId { get; init; }

    /// <summary>
    /// Minimal new-patient data for inline creation.
    /// Required when <see cref="PatientId"/> is null.
    /// </summary>
    public NewWalkInPatientRequest? NewPatient { get; init; }

    /// <summary>
    /// Slot identifier from <c>GET /api/staff/walkin/slots</c>.
    /// Must be a same-day slot (server validates that the slot date equals UTC today).
    /// </summary>
    [Required(ErrorMessage = "SlotId is required.")]
    [StringLength(50, ErrorMessage = "SlotId must not exceed 50 characters.")]
    public string SlotId { get; init; } = string.Empty;

    /// <summary>Appointment type label (e.g. "General Checkup"). Max 50 characters.</summary>
    [Required(ErrorMessage = "VisitType is required.")]
    [StringLength(50, ErrorMessage = "VisitType must not exceed 50 characters.")]
    public string VisitType { get; init; } = string.Empty;

    /// <summary>
    /// True when staff flags this walk-in as urgent (EC-2).
    /// Triggers <see cref="QueuePriority.Urgent"/> queue insertion and, when same-day capacity
    /// is exhausted, returns an escalation result instead of a plain 409.
    /// </summary>
    public bool IsUrgent { get; init; }
}

// ─── Walk-in booking response ─────────────────────────────────────────────────

/// <summary>
/// Response returned on successful walk-in booking (201 Created, US_022 AC-3).
/// </summary>
public sealed record WalkInBookingResponse(
    /// <summary>Generated booking reference (BK-{YYYYMMDD}-{6-char-suffix}).</summary>
    string BookingReference,
    /// <summary>UUID of the created appointment.</summary>
    string AppointmentId,
    /// <summary>UUID of the patient (new or existing).</summary>
    string PatientId,
    /// <summary>Full name of the patient.</summary>
    string PatientName,
    /// <summary>ISO-8601 appointment date (YYYY-MM-DD).</summary>
    string Date,
    /// <summary>Appointment start time (HH:mm 24-hour).</summary>
    string StartTime,
    /// <summary>Appointment end time (HH:mm 24-hour).</summary>
    string EndTime,
    /// <summary>Provider full name.</summary>
    string ProviderName,
    /// <summary>Visit type / appointment type label.</summary>
    string AppointmentType,
    /// <summary>True — all walk-in appointments carry this flag (AC-3).</summary>
    bool IsWalkIn,
    /// <summary>True when staff flagged the walk-in as urgent (EC-2).</summary>
    bool IsUrgent,
    /// <summary>Position in the arrival queue after insertion.</summary>
    int QueuePosition);
