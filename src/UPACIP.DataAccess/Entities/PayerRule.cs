using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Payer-specific or CMS-default code validation rule used by the payer rule validation
/// service (US_051, AC-1, AC-2, task_003_db_payer_rules_schema).
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     <strong>No <c>BaseEntity</c> inheritance</strong> — uses a dedicated
///     <c>RuleId</c> PK (consistent with <c>CptBundleRule</c>).  Rules are reference data,
///     not patient records, so <c>UpdatedAt</c> would add noise.
///   </item>
///   <item>
///     <strong><c>IsCmsDefault</c> flag</strong> — when payer-specific rules are unavailable
///     the service falls back to rows where this flag is true (edge case, US_051 EC-1).
///   </item>
/// </list>
/// </summary>
public sealed class PayerRule
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid RuleId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// External payer identifier (e.g. <c>"BCBS-IL"</c>, <c>"AETNA"</c>).
    /// <c>null</c> for CMS-default rules that apply to all payers.
    /// Max 50 characters; indexed for fast lookup.
    /// </summary>
    public string? PayerId { get; set; }

    /// <summary>Display name of the payer for UI presentation. Max 200 characters.</summary>
    public string? PayerName { get; set; }

    /// <summary>Categorises the validation rule type.</summary>
    public PayerRuleType RuleType { get; set; }

    /// <summary>Code standard the <see cref="PrimaryCode"/> belongs to.</summary>
    public CodeType CodeType { get; set; }

    /// <summary>
    /// The code value the rule applies to (e.g. <c>"99213"</c>).
    /// Max 20 characters; indexed as part of the composite lookup key.
    /// </summary>
    public string PrimaryCode { get; set; } = string.Empty;

    /// <summary>
    /// Optional second code involved in a combination rule (e.g. <c>"99214"</c> in a same-day duplicate rule).
    /// <c>null</c> for single-code rules. Max 20 characters.
    /// </summary>
    public string? SecondaryCode { get; set; }

    /// <summary>Human-readable description of the rule. Max 1 000 characters.</summary>
    public string RuleDescription { get; set; } = string.Empty;

    /// <summary>Payer's stated reason for denying the claim. Max 500 characters.</summary>
    public string DenialReason { get; set; } = string.Empty;

    /// <summary>Suggested corrective action staff should take. Max 500 characters.</summary>
    public string CorrectiveAction { get; set; } = string.Empty;

    /// <summary>Urgency level determining how prominently the alert is displayed.</summary>
    public PayerRuleSeverity Severity { get; set; }

    /// <summary>
    /// <c>true</c> for rules sourced from CMS guidelines.
    /// When payer-specific rules are unavailable, only CMS-default rules are applied (US_051 EC-1).
    /// </summary>
    public bool IsCmsDefault { get; set; }

    /// <summary>Date from which this rule is effective. Used to filter out future-dated rules.</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Optional date after which this rule is no longer enforced.</summary>
    public DateOnly? ExpirationDate { get; set; }

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update to this row.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
