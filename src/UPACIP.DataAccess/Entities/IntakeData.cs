using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Captures all intake information collected from a patient prior to an appointment.
/// The three JSONB columns (<see cref="MandatoryFields"/>, <see cref="OptionalFields"/>,
/// <see cref="InsuranceInfo"/>) are stored as strongly-typed owned types serialized to
/// JSONB by EF Core / Npgsql.
/// </summary>
public sealed class IntakeData : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> who submitted this intake record.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Channel through which the intake was collected.</summary>
    public IntakeMethod IntakeMethod { get; set; }

    /// <summary>Required clinical fields — stored as JSONB (mandatory_fields column).</summary>
    public IntakeMandatoryFields? MandatoryFields { get; set; }

    /// <summary>Optional clinical fields — stored as JSONB (optional_fields column).</summary>
    public IntakeOptionalFields? OptionalFields { get; set; }

    /// <summary>Patient insurance details — stored as JSONB (insurance_info column).</summary>
    public InsuranceInfo? InsuranceInfo { get; set; }

    /// <summary>UTC timestamp when the patient completed and submitted the intake form.</summary>
    public DateTime? CompletedAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;
}
