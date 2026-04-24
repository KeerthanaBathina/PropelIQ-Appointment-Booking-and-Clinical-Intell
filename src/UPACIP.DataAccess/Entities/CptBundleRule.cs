namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Defines a bundling relationship between a composite CPT procedure code and one of its
/// component codes (US_048 AC-3, edge case: bundled procedures).
///
/// When the AI coding pipeline identifies that multiple individual CPT codes can be
/// consolidated into a single billable bundle code, the relevant rule row is surfaced to
/// the staff reviewer alongside the individual codes.  Staff may then approve the bundled
/// code instead of billing each component separately.
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     <strong>String foreign keys to <c>cpt_code_library.cpt_code</c></strong> rather
///     than UUID FKs.  CPT code values are stable business identifiers; string FKs keep
///     the bundle rule readable and maintainable by clinical informaticists without
///     needing to join to <c>cpt_code_library</c> for display.
///   </item>
///   <item>
///     <strong>Unique constraint on (<c>bundle_cpt_code</c>, <c>component_cpt_code</c>)</strong>
///     prevents duplicate rules and makes the quarterly seed idempotent on re-run.
///   </item>
///   <item>
///     <strong>No <c>BaseEntity</c> inheritance</strong> — uses a dedicated
///     <c>BundleRuleId</c> PK to distinguish it from patient-record entities.
///   </item>
/// </list>
/// </summary>
public sealed class CptBundleRule
{
    /// <summary>Surrogate UUID primary key (column: bundle_rule_id).</summary>
    public Guid BundleRuleId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The composite/bundled CPT code that replaces the individual component codes
    /// when bundling is appropriate (e.g. <c>"99213"</c> bundling office visit components).
    /// References <c>cpt_code_library.cpt_code</c>; max 10 characters.
    /// </summary>
    public string BundleCptCode { get; set; } = string.Empty;

    /// <summary>
    /// One of the individual CPT codes that is absorbed into the bundle.
    /// References <c>cpt_code_library.cpt_code</c>; max 10 characters.
    /// </summary>
    public string ComponentCptCode { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of why these codes may be bundled
    /// (e.g. "NCCI edit: 99213 includes basic venipuncture service").
    /// Used by staff reviewers to understand the bundling rationale. Max 500 characters.
    /// </summary>
    public string BundleDescription { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> while this rule is still clinically valid and should be surfaced to
    /// staff reviewers.  Set to <c>false</c> when the rule is superseded by a new AMA
    /// guideline or NCCI edit update without removing the historical record.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
