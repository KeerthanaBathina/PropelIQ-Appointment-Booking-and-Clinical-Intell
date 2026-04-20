namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// Owned type stored as a JSONB column (optional_fields) in the intake_data table.
/// These fields are collected when available but are not required for submission.
/// </summary>
public sealed class IntakeOptionalFields
{
    public string? FamilyHistory { get; set; }

    public string? SocialHistory { get; set; }

    public string? ReviewOfSystems { get; set; }

    public string? AdditionalNotes { get; set; }
}
