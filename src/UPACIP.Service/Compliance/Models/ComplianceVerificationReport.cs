namespace UPACIP.Service.Compliance.Models;

/// <summary>
/// Aggregated result of a full HIPAA technical safeguard verification run (US_093, AC-1).
/// Returned by the compliance API and persisted as JSON in <see cref="ComplianceVerificationLog"/>.
/// </summary>
public sealed class ComplianceVerificationReport
{
    /// <summary>UTC timestamp when the verification was executed.</summary>
    public DateTime ExecutedAtUtc { get; set; }

    /// <summary>Identity of the admin who triggered the verification.</summary>
    public required string ExecutedBy { get; set; }

    /// <summary>True when all checks passed; false if any check failed.</summary>
    public bool AllPassed { get; set; }

    /// <summary>Total number of checks executed.</summary>
    public int TotalChecks { get; set; }

    /// <summary>Number of checks that passed.</summary>
    public int PassedCount { get; set; }

    /// <summary>Number of checks that failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Per-control verification results.</summary>
    public List<ComplianceCheckResult> Results { get; set; } = new();
}
