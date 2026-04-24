namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks the NCCI bundling rule check result for a <see cref="UPACIP.DataAccess.Entities.MedicalCode"/>
/// (US_051, AC-4, task_003_db_payer_rules_schema).
/// </summary>
public enum BundlingCheckResult
{
    /// <summary>Bundling validation has not been run for this code set yet (default).</summary>
    NotChecked,

    /// <summary>Code set passed all NCCI bundling rule checks.</summary>
    Passed,

    /// <summary>Code set has one or more bundling violations that require resolution.</summary>
    Failed,
}
