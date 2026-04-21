namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Reference record for the soft insurance pre-check (US_031, AC-2, FR-033).
///
/// Each row represents one known-valid (provider keyword, policy prefix) combination.
/// The <see cref="UPACIP.Service.Appointments.InsurancePrecheckService"/> validates
/// submitted insurance details against these rows so development and QA environments
/// have a stable, deterministic validation source without external payer integrations.
///
/// Records are seeded once by the AddMinorGuardianAndInsuranceValidation migration.
/// Staff or administrators can deactivate a record by setting <see cref="IsActive"/> = false
/// without deleting it, preserving historical context for audit trails.
///
/// Note: This entity intentionally does NOT inherit from <see cref="BaseEntity"/> because
/// it uses an integer identity PK (not a Guid) and does not require UpdatedAt / DeletedAt
/// audit fields.  Its rows are static reference data managed by migrations.
/// </summary>
public sealed class InsuranceValidationRecord
{
    /// <summary>
    /// Auto-increment integer surrogate primary key.
    /// Using int identity (not Guid) because these are static reference rows — small cardinality,
    /// never referenced by FK from other domain entities.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Canonical display name of the insurance provider (e.g., "Blue Cross Blue Shield").
    /// Used for logging and staff-facing review output.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase keyword or alias matched against the patient-supplied provider string
    /// (e.g., "blue cross", "bcbs", "aetna").
    /// Comparison is case-insensitive substring match.
    /// </summary>
    public string ProviderKeyword { get; set; } = string.Empty;

    /// <summary>
    /// Known-valid policy number prefix for this provider (e.g., "BCB-", "AET-").
    /// The patient's policy number must start with this prefix for a "valid" outcome.
    /// </summary>
    public string PolicyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// When <c>false</c> the record is excluded from the active validation set.
    /// Allows deactivating a provider without a schema migration.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the seed record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
