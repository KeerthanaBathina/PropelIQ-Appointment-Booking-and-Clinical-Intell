namespace UPACIP.Service.Caching.Models;

/// <summary>
/// Lightweight, PHI-minimal patient profile snapshot stored in Redis
/// (US_084 task_002, AC-2, AC-3).
///
/// <para>
/// Contains only display-friendly, non-clinical fields so that cached data
/// represents the minimum required for the profile header and appointment
/// summary view.  Sensitive clinical data (medical history, diagnoses,
/// documents) is always fetched directly from PostgreSQL — never cached here.
/// </para>
///
/// <para>
/// Cache key: <c>patient:profile:{PatientId}</c>. TTL: 5 minutes (NFR-030).
/// </para>
/// </summary>
public sealed record CachedPatientProfile
{
    /// <summary>Patient entity primary key.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Patient display name (FirstName + LastName).</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Date of birth in ISO-8601 format (YYYY-MM-DD).</summary>
    public string DateOfBirth { get; init; } = string.Empty;

    /// <summary>
    /// Insurance provider name from the most recent completed intake record.
    /// <c>null</c> when no intake data has been submitted.
    /// </summary>
    public string? InsuranceProvider { get; init; }

    /// <summary>
    /// Human-readable insurance validation status: <c>"valid"</c>, <c>"needs-review"</c>,
    /// <c>"skipped"</c>, or <c>"unknown"</c>.
    /// </summary>
    public string InsuranceStatus { get; init; } = "unknown";

    /// <summary>Count of non-cancelled appointments for the patient.</summary>
    public int RecentAppointmentCount { get; init; }

    /// <summary>
    /// ISO-8601 UTC timestamp of the most recent appointment.
    /// <c>null</c> when the patient has no appointment history.
    /// </summary>
    public string? LastVisitDate { get; init; }

    /// <summary>UTC timestamp when this snapshot was cached.</summary>
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;
}
