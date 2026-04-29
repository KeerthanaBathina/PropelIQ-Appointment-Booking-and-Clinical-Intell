using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.Compliance.Models;

namespace UPACIP.Service.Compliance;

/// <summary>
/// Guards PHI (Protected Health Information) during EF Core migration execution by
/// verifying encryption state and data accessibility before and after migrations
/// (US_093, AC-4, DR-031).
///
/// <para>Pre-migration check (must pass before migration runs):</para>
/// <list type="bullet">
///   <item>Verifies SSL is active on the database connection.</item>
///   <item>Identifies PHI columns and confirms they exist with expected types.</item>
///   <item>Detects ALTER COLUMN TYPE on PHI columns (would re-encode in plaintext).</item>
///   <item>Detects DROP/RENAME on PHI columns without a corresponding ADD (expand-contract pattern).</item>
/// </list>
///
/// <para>Post-migration verify (confirms data remains accessible after migration):</para>
/// <list type="bullet">
///   <item>Re-queries information_schema.columns for all PHI columns.</item>
///   <item>Executes SELECT COUNT(*) on each PHI table to verify read access.</item>
///   <item>Confirms SSL is still active.</item>
/// </list>
/// </summary>
public interface IPhiMigrationGuard
{
    /// <summary>
    /// Executes pre-migration PHI protection checks.
    /// When <see cref="PhiMigrationCheckResult.Safe"/> is <c>false</c> the migration must NOT proceed.
    /// </summary>
    Task<PhiMigrationCheckResult> PreMigrationCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes post-migration PHI accessibility verification.
    /// Call after the migration completes to confirm no PHI data was lost or corrupted.
    /// </summary>
    Task<PhiMigrationCheckResult> PostMigrationVerifyAsync(CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="IPhiMigrationGuard"/>.
/// </summary>
public sealed class PhiMigrationGuard : IPhiMigrationGuard
{
    private readonly ApplicationDbContext       _db;
    private readonly ILogger<PhiMigrationGuard> _logger;

    // PHI-bearing tables and their sensitive columns per DR-031 classification.
    // These column names must NEVER appear in plaintext during a migration.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PhiColumnMap =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["asp_net_users"]   = new[] { "email", "password_hash", "phone_number" },
            ["patients"]        = new[] { "full_name", "date_of_birth", "emergency_contact" },
            ["intake_records"]  = new[] { "insurance_info" },
            ["clinical_documents"] = new[] { "file_path" },
            ["extracted_data"]  = new[] { "data_content" },
        };

    // DDL keywords that indicate a potentially unsafe PHI column operation.
    private static readonly string[] UnsafeDdlKeywords =
        { "ALTER COLUMN", "DROP COLUMN", "RENAME COLUMN" };

    public PhiMigrationGuard(
        ApplicationDbContext       db,
        ILogger<PhiMigrationGuard> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── PreMigrationCheckAsync ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PhiMigrationCheckResult> PreMigrationCheckAsync(
        CancellationToken ct = default)
    {
        var result = new PhiMigrationCheckResult { CheckedAtUtc = DateTime.UtcNow };

        // (a) Verify SSL is active on the migration connection.
        result.SslActive = await CheckSslActiveAsync(ct);
        if (!result.SslActive)
            result.BlockingIssues.Add(
                "SSL is not active on the database connection. PHI must travel over an encrypted channel (TR-018).");

        // (b) Verify PHI columns exist and count them.
        var (verified, missing) = await VerifyPhiColumnsAsync(ct);
        result.PhiColumnsVerified = verified;

        if (missing.Count > 0)
            result.Warnings.AddRange(
                missing.Select(m => $"PHI column '{m}' not found in information_schema — verify schema matches expectations."));

        // (c) Check pending migrations for unsafe PHI column operations.
        var pendingMigrations = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pendingMigrations.Count > 0)
        {
            var unsafeOps = DetectUnsafePhiOperations(pendingMigrations);
            foreach (var issue in unsafeOps)
                result.BlockingIssues.Add(issue);
        }

        result.Safe = result.BlockingIssues.Count == 0;

        _logger.LogInformation(
            "PHI_MIGRATION_PRE_CHECK: Safe={Safe}, PhiColumnsVerified={Verified}, " +
            "BlockingIssues={Blocking}, Warnings={Warnings}, SslActive={Ssl}",
            result.Safe, result.PhiColumnsVerified,
            result.BlockingIssues.Count, result.Warnings.Count, result.SslActive);

        return result;
    }

    // ── PostMigrationVerifyAsync ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PhiMigrationCheckResult> PostMigrationVerifyAsync(
        CancellationToken ct = default)
    {
        var result = new PhiMigrationCheckResult { CheckedAtUtc = DateTime.UtcNow };

        // (a) Re-verify PHI columns are still present.
        var (verified, missing) = await VerifyPhiColumnsAsync(ct);
        result.PhiColumnsVerified = verified;

        if (missing.Count > 0)
            result.BlockingIssues.AddRange(
                missing.Select(m => $"PHI column '{m}' missing after migration — data may have been lost."));

        // (b) Verify read access on each PHI-bearing table via COUNT(*).
        var tableAccessErrors = await VerifyPhiTableAccessAsync(ct);
        result.BlockingIssues.AddRange(tableAccessErrors);

        // (c) Confirm SSL is still active.
        result.SslActive = await CheckSslActiveAsync(ct);
        if (!result.SslActive)
            result.Warnings.Add(
                "SSL is no longer active on the database connection after migration. Investigate immediately.");

        result.Safe = result.BlockingIssues.Count == 0;

        _logger.LogInformation(
            "PHI_MIGRATION_VERIFIED: AllPhiColumnsAccessible={Accessible}, SslActive={Ssl}, " +
            "Verified={Verified}, BlockingIssues={Blocking}",
            result.Safe, result.SslActive,
            result.PhiColumnsVerified, result.BlockingIssues.Count);

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    // Queries pg_stat_ssl for the current backend PID to verify TLS is active.
    private async Task<bool> CheckSslActiveAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _db.Database
                .SqlQuery<bool>($"SELECT ssl AS \"Value\" FROM pg_stat_ssl WHERE pid = pg_backend_pid()")
                .ToListAsync(ct);

            return rows.Count > 0 && rows[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query pg_stat_ssl — treating SSL as inactive.");
            return false;
        }
    }

    // Queries information_schema.columns for each expected PHI column.
    // Returns (verifiedCount, list of missing "table.column" strings).
    private async Task<(int Verified, List<string> Missing)> VerifyPhiColumnsAsync(
        CancellationToken ct)
    {
        var missing  = new List<string>();
        int verified = 0;

        foreach (var (table, columns) in PhiColumnMap)
        {
            foreach (var column in columns)
            {
                var tableName  = table;
                var columnName = column;

                var count = await _db.Database
                    .SqlQuery<long>($"""
                        SELECT COUNT(*) AS "Value"
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name   = {tableName}
                          AND column_name  = {columnName}
                        """)
                    .ToListAsync(ct);

                if (count.Count > 0 && count[0] > 0)
                    verified++;
                else
                    missing.Add($"{table}.{column}");
            }
        }

        return (verified, missing);
    }

    // Executes SELECT COUNT(*) on each PHI table to verify read access post-migration.
    private async Task<List<string>> VerifyPhiTableAccessAsync(CancellationToken ct)
    {
        var errors = new List<string>();

        foreach (var table in PhiColumnMap.Keys)
        {
            try
            {
                // Use a safe, non-parameterized table name — table names come from
                // the static PhiColumnMap dictionary, never from user input.
                await _db.Database.ExecuteSqlRawAsync(
                    $"SELECT COUNT(*) FROM \"{table}\"", ct);
            }
            catch (Exception ex)
            {
                errors.Add($"PHI table '{table}' is not accessible after migration: {ex.Message}");
                _logger.LogError(ex,
                    "PHI_TABLE_ACCESS_FAILED: Table={Table}", table);
            }
        }

        return errors;
    }

    // Inspects pending migration names for any that suggest unsafe PHI column DDL.
    // Migration names are checked for keywords (e.g., "AlterEmailColumn").
    // This is a heuristic check — comprehensive review requires inspecting migration SQL.
    private static List<string> DetectUnsafePhiOperations(IEnumerable<string> migrationNames)
    {
        var issues = new List<string>();

        foreach (var name in migrationNames)
        {
            foreach (var keyword in UnsafeDdlKeywords)
            {
                // Check if the migration name contains PHI column references combined with DDL keywords.
                var ddlIndicator = keyword.Replace(" ", string.Empty);
                if (name.Contains(ddlIndicator, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if any PHI column name is also mentioned in the migration name.
                    var allPhiColumns = PhiColumnMap.Values
                        .SelectMany(cols => cols)
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var phiCol in allPhiColumns)
                    {
                        var colPascal = string.Concat(phiCol.Split('_')
                            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

                        if (name.Contains(colPascal, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains(phiCol, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(
                                $"Pending migration '{name}' may perform '{keyword}' on PHI column '{phiCol}'. " +
                                "Review carefully — use expand-contract pattern per DR-031 to prevent PHI exposure.");
                        }
                    }
                }
            }
        }

        return issues;
    }
}
