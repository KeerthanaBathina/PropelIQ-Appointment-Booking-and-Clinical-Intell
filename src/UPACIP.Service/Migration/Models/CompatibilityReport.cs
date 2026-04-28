namespace UPACIP.Service.Migration.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Severity enum
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>How disruptive a backward-incompatible migration operation is.</summary>
public enum BreakingSeverity
{
    /// <summary>May cause issues during rolling deployment but not a hard failure.</summary>
    Warning  = 0,
    /// <summary>Will break existing application instances during zero-downtime deployment.</summary>
    Breaking = 1,
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting record
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Details of a single detected backward-incompatible migration operation.
/// </summary>
public sealed record BreakingChange
{
    public required string          MigrationName            { get; init; }
    public required string          OperationType            { get; init; }
    public required string          TableName                { get; init; }
    public string?                  ColumnName               { get; init; }
    public BreakingSeverity         Severity                 { get; init; }
    /// <summary>
    /// Concrete expand-contract migration split recommendation for this operation.
    /// </summary>
    public required string          ExpandContractSuggestion { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Primary DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of a backward-compatibility analysis for pending EF Core migrations
/// (US_091 task_002, AC-4, edge case 2, DR-031).
/// </summary>
public sealed record CompatibilityReport
{
    /// <summary>Migration name this report covers.</summary>
    public required string MigrationName { get; init; }

    /// <summary><c>true</c> if no breaking operations were detected.</summary>
    public bool IsBackwardCompatible { get; init; }

    /// <summary>List of backward-incompatible operations detected.</summary>
    public List<BreakingChange> BreakingChanges { get; init; } = [];

    /// <summary>
    /// Expand-contract pattern guidance for each breaking change, plus
    /// general deployment sequencing recommendations.
    /// </summary>
    public List<string> Recommendations { get; init; } = [];

    /// <summary>Operations confirmed as safe for zero-downtime deployment.</summary>
    public List<string> SafeOperations { get; init; } = [];
}
