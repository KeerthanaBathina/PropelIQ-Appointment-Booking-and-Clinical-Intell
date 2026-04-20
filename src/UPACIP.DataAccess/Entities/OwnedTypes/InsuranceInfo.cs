namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// Owned type stored as a JSONB column (insurance_info) in the intake_data table.
/// Captures patient insurance details submitted during intake.
/// </summary>
public sealed class InsuranceInfo
{
    public string? Provider { get; set; }

    public string? PolicyNumber { get; set; }

    public string? GroupNumber { get; set; }

    public string? PrimaryInsuredName { get; set; }

    public DateOnly? PolicyExpiryDate { get; set; }
}
