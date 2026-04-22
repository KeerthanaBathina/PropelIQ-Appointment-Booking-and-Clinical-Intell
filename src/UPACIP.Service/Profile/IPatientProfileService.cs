using UPACIP.Service.Consolidation;

namespace UPACIP.Service.Profile;

/// <summary>
/// Contract for the patient profile aggregation service (US_043, AC-1, AC-2, AC-3, FR-052, FR-056).
///
/// All read operations apply a 5-minute Redis cache (NFR-030).
/// Cache entries for a patient are invalidated when <see cref="TriggerConsolidationAsync"/> completes.
/// </summary>
public interface IPatientProfileService
{
    /// <summary>
    /// Retrieves the full 360° consolidated patient profile with all clinical data categories
    /// (medications, diagnoses, procedures, allergies), review metadata, and current version info.
    ///
    /// Applies Redis cache-aside with 5-minute TTL (NFR-030).
    /// Returns <c>null</c> when the patient does not exist or has been soft-deleted.
    /// </summary>
    Task<PatientProfile360Dto?> GetProfile360Async(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Returns the ordered version history for a patient's consolidated profile, newest first.
    ///
    /// Results are cached for 5 minutes per patient.
    /// Returns an empty list when no consolidation has run yet.
    /// </summary>
    Task<IReadOnlyList<VersionHistoryDto>> GetVersionHistoryAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Returns the detail of a specific profile version by version number.
    ///
    /// Returns <c>null</c> when the version number does not exist for the given patient.
    /// </summary>
    Task<VersionHistoryDto?> GetVersionSnapshotAsync(Guid patientId, int versionNumber, CancellationToken ct = default);

    /// <summary>
    /// Returns the source document citation for a single extracted data point (AC-3).
    ///
    /// Verifies the data point belongs to <paramref name="patientId"/> before returning.
    /// Returns <c>null</c> when the extracted data row does not exist or belongs to a different patient.
    /// </summary>
    Task<SourceCitationDto?> GetSourceCitationAsync(Guid patientId, Guid extractedDataId, CancellationToken ct = default);

    /// <summary>
    /// Triggers a full manual consolidation for the patient and invalidates the profile cache.
    ///
    /// Intended for use by the staff-facing "Re-consolidate" action button (FR-052).
    /// </summary>
    Task<ConsolidationResult> TriggerConsolidationAsync(Guid patientId, Guid triggeredByUserId, CancellationToken ct = default);
}
