namespace UPACIP.DataAccess.Entities;

/// <summary>
/// CPT billing modifier reference data (US_051, AC-4, task_003_db_payer_rules_schema).
/// Modifiers can be appended to CPT codes to clarify billing circumstances and override
/// certain NCCI bundling restrictions.
/// </summary>
public sealed class CodeModifier
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid ModifierId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Two-character modifier code (e.g. <c>"59"</c>, <c>"25"</c>, <c>"76"</c>).
    /// Unique; max 5 characters.
    /// </summary>
    public string ModifierCode { get; set; } = string.Empty;

    /// <summary>Clinical description of what the modifier signifies. Max 500 characters.</summary>
    public string ModifierDescription { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of code types this modifier applies to (e.g. <c>["cpt"]</c>).
    /// Max 50 characters.
    /// </summary>
    public string ApplicableCodeTypes { get; set; } = "[\"cpt\"]";

    /// <summary>
    /// <c>true</c> when appending this modifier requires additional clinical documentation
    /// to support the payer's medical-review process.
    /// </summary>
    public bool DocumentationRequired { get; set; }

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
