namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks whether a <see cref="UPACIP.DataAccess.Entities.MedicalCode"/> has been validated
/// against payer-specific rules (US_051, AC-1, task_003_db_payer_rules_schema).
/// </summary>
public enum PayerValidationStatus
{
    /// <summary>Payer validation has not been run for this code yet (default).</summary>
    NotValidated,

    /// <summary>Code passed all payer-specific validation rules.</summary>
    Valid,

    /// <summary>Code triggered a payer warning — review recommended before claim submission.</summary>
    Warning,

    /// <summary>Code is flagged as a high denial risk by payer rules.</summary>
    Denied,
}
