using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// NCCI procedure-to-procedure bundling edit record (US_051, AC-4, task_003_db_payer_rules_schema).
///
/// Column-1 and column-2 follow NCCI terminology:
/// — Column-1 code: the comprehensive/primary procedure.
/// — Column-2 code: the component/secondary procedure that cannot be billed separately
///   unless a permitted modifier is appended.
/// </summary>
public sealed class BundlingEdit
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid EditId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Comprehensive (column-1) CPT code.
    /// Max 10 characters; part of composite index with <see cref="Column2Code"/>.
    /// </summary>
    public string Column1Code { get; set; } = string.Empty;

    /// <summary>
    /// Component (column-2) CPT code that cannot be billed with <see cref="Column1Code"/>.
    /// Max 10 characters.
    /// </summary>
    public string Column2Code { get; set; } = string.Empty;

    /// <summary>Edit type determines whether a modifier can override the restriction.</summary>
    public BundlingEditType EditType { get; set; }

    /// <summary>
    /// <c>true</c> when a modifier (e.g. "59", "XS") can be appended to <see cref="Column2Code"/>
    /// to override the bundling restriction.  Always <c>false</c> for <see cref="BundlingEditType.MutuallyExclusive"/>.
    /// </summary>
    public bool ModifierAllowed { get; set; }

    /// <summary>
    /// JSON array of modifier codes that can override this bundling edit (e.g. <c>["59","XS","XU"]</c>).
    /// Empty when <see cref="ModifierAllowed"/> is <c>false</c>. Max 200 characters.
    /// </summary>
    public string AllowedModifiers { get; set; } = "[]";

    /// <summary>Source dataset — "NCCI" for CMS NCCI edits. Max 20 characters.</summary>
    public string Source { get; set; } = "NCCI";

    /// <summary>Date from which this edit is effective.</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Optional date after which this edit is superseded.</summary>
    public DateOnly? ExpirationDate { get; set; }

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
