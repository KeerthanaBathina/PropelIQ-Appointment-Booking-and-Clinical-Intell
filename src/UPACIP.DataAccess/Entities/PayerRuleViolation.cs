using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records a payer rule violation detected during the validation run for a patient encounter
/// (US_051, AC-2, task_003_db_payer_rules_schema).
///
/// Each row represents a single violation identified during a validation pass.
/// When a staff member takes action (accept, override, dismiss) the resolution fields are
/// populated and the record serves as the permanent audit trail for the decision (FR-066).
/// </summary>
public sealed class PayerRuleViolation
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid ViolationId { get; set; } = Guid.NewGuid();

    /// <summary>FK to the patient whose codes triggered this violation.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Calendar date of the encounter being validated.</summary>
    public DateOnly EncounterDate { get; set; }

    /// <summary>FK to the <see cref="PayerRule"/> that was violated.</summary>
    public Guid RuleId { get; set; }

    /// <summary>
    /// JSON array of code values involved in the violation (e.g. <c>["99213","99214"]</c>).
    /// Max 500 characters.
    /// </summary>
    public string ViolatingCodes { get; set; } = "[]";

    /// <summary>Severity copied from the rule at the time of violation detection.</summary>
    public PayerRuleSeverity Severity { get; set; }

    /// <summary>Current lifecycle state of this violation record.</summary>
    public ViolationResolutionStatus ResolutionStatus { get; set; } = ViolationResolutionStatus.Pending;

    /// <summary>FK to the staff user who resolved the violation. <c>null</c> while pending.</summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>
    /// Mandatory clinical justification when staff overrides the payer rule.
    /// <c>null</c> for accepted or dismissed violations. Max 1 000 characters.
    /// </summary>
    public string? ResolutionJustification { get; set; }

    /// <summary>UTC timestamp when the resolution was recorded. <c>null</c> while pending.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>UTC timestamp when this violation was first detected.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>The patient whose encounter triggered this violation.</summary>
    public Patient Patient { get; set; } = null!;

    /// <summary>The payer rule that was violated.</summary>
    public PayerRule Rule { get; set; } = null!;

    /// <summary>The staff user who resolved the violation (null while pending).</summary>
    public ApplicationUser? ResolvedByUser { get; set; }
}
