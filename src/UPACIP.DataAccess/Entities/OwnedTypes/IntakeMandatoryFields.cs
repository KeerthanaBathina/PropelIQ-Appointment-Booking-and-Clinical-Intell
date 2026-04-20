namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// Owned type stored as a JSONB column (mandatory_fields) in the intake_data table.
/// These fields are required for every intake submission.
/// </summary>
public sealed class IntakeMandatoryFields
{
    public string ChiefComplaint { get; set; } = string.Empty;

    public string Allergies { get; set; } = string.Empty;

    public List<string> CurrentMedications { get; set; } = [];

    public string MedicalHistory { get; set; } = string.Empty;
}
