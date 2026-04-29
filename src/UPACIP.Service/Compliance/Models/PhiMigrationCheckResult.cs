namespace UPACIP.Service.Compliance.Models;

/// <summary>
/// Result of a PHI protection check surrounding an EF Core migration execution
/// (US_093, AC-4, DR-031).
///
/// <para>
/// <see cref="BlockingIssues"/> must be empty for a migration to proceed safely.
/// <see cref="Warnings"/> are non-blocking but should be reviewed before proceeding.
/// </para>
/// </summary>
public sealed class PhiMigrationCheckResult
{
    /// <summary>
    /// <c>true</c> when no <see cref="BlockingIssues"/> were detected and the migration
    /// may proceed; <c>false</c> when migration must be halted.
    /// </summary>
    public bool Safe { get; set; }

    /// <summary>
    /// Non-blocking advisories — migration may proceed but issues should be reviewed.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Blocking issues that would expose PHI in plaintext or cause data loss.
    /// Non-empty means <see cref="Safe"/> is <c>false</c>.
    /// </summary>
    public List<string> BlockingIssues { get; set; } = new();

    /// <summary>
    /// Number of PHI columns verified as present and having the expected data types.
    /// </summary>
    public int PhiColumnsVerified { get; set; }

    /// <summary>
    /// Whether SSL/TLS is active on the current database connection at check time.
    /// </summary>
    public bool SslActive { get; set; }

    /// <summary>UTC timestamp when this check was performed.</summary>
    public DateTime CheckedAtUtc { get; set; }
}
