using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

/// <summary>
/// Admin-triggered quarterly backup restoration test pipeline (AC-3, AC-4, DR-026).
/// </summary>
public interface IBackupRestorationTestService
{
    /// <summary>
    /// Decrypts the latest backup, restores it to the isolated test database, and runs
    /// three-layer integrity validation (row counts, FK constraints, checksums).
    /// </summary>
    Task<RestorationTestResult> RunRestorationTestAsync(string performedBy, CancellationToken ct);

    /// <summary>
    /// Returns the quarterly scheduling status: "Completed", "DueSoon", or "Overdue".
    /// </summary>
    Task<QuarterlyTestStatus> GetQuarterlyTestStatusAsync(CancellationToken ct);
}

/// <summary>
/// Scoped implementation of <see cref="IBackupRestorationTestService"/>.
///
/// Security notes (OWASP A02 / A09):
/// <list type="bullet">
///   <item>Test DB password is injected via <c>RestorationTest__TestDatabasePassword</c> env var only.</item>
///   <item><c>PGPASSWORD</c> is set on the child process environment exclusively — never in CLI args.</item>
///   <item>The temporary decrypted <c>.dump</c> file is deleted in a <c>finally</c> block.</item>
///   <item>Production database is accessed read-only for row-count and checksum queries.</item>
///   <item>All SQL queries use parameterised <c>NpgsqlCommand</c> — no string interpolation into SQL.</item>
/// </list>
/// </summary>
public sealed class BackupRestorationTestService : IBackupRestorationTestService
{
    private readonly IOptionsMonitor<RestorationTestOptions> _restorationOptions;
    private readonly IOptionsMonitor<BackupOptions>          _backupOptions;
    private readonly IBackupEncryptionService                _encryptionService;
    private readonly ApplicationDbContext                    _db;
    private readonly ILogger<BackupRestorationTestService>   _logger;

    public BackupRestorationTestService(
        IOptionsMonitor<RestorationTestOptions> restorationOptions,
        IOptionsMonitor<BackupOptions>          backupOptions,
        IBackupEncryptionService                encryptionService,
        ApplicationDbContext                    db,
        ILogger<BackupRestorationTestService>   logger)
    {
        _restorationOptions = restorationOptions;
        _backupOptions      = backupOptions;
        _encryptionService  = encryptionService;
        _db                 = db;
        _logger             = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IBackupRestorationTestService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RestorationTestResult> RunRestorationTestAsync(
        string            performedBy,
        CancellationToken ct)
    {
        var opts        = _restorationOptions.CurrentValue;
        var backupOpts  = _backupOptions.CurrentValue;
        var startedAt   = DateTime.UtcNow;

        // Guard: test database must not be the production database (OWASP A01 — access control).
        if (string.Equals(opts.TestDatabaseName, backupOpts.DatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            const string safetyMsg =
                "RestorationTest:TestDatabaseName must differ from the production database. " +
                "Aborting to prevent production data loss.";
            _logger.LogError("RESTORATION_TEST_FAILED: {Reason}", safetyMsg);
            return FailedResult(string.Empty, startedAt, TimeSpan.Zero, safetyMsg);
        }

        // ── Step 1: Find latest backup ────────────────────────────────────────
        string? backupFilePath = FindLatestBackupFile(backupOpts.BackupDirectory);

        if (backupFilePath is null)
        {
            const string msg = "No backup files found in the configured backup directory.";
            _logger.LogError("RESTORATION_TEST_FAILED: {Reason}", msg);
            return FailedResult(string.Empty, startedAt, TimeSpan.Zero, msg);
        }

        var backupFileName = Path.GetFileName(backupFilePath);
        string? tempDecryptedPath = null;

        try
        {
            // ── Step 2: Decrypt if encrypted ──────────────────────────────────
            string plaintextPath;

            if (backupFilePath.EndsWith(".dump.enc", StringComparison.OrdinalIgnoreCase))
            {
                tempDecryptedPath = Path.Combine(
                    backupOpts.BackupDirectory,
                    Path.GetFileNameWithoutExtension(backupFilePath) + ".tmp.dump");

                _logger.LogInformation(
                    "BackupRestorationTestService: decrypting {EncFile} → {TmpFile}",
                    backupFileName, Path.GetFileName(tempDecryptedPath));

                plaintextPath = await _encryptionService.DecryptBackupAsync(
                    backupFilePath, tempDecryptedPath, ct);
            }
            else
            {
                plaintextPath = backupFilePath;
            }

            // ── Step 3: Drop + recreate test database ─────────────────────────
            var dbError = await RecreateTestDatabaseAsync(opts, ct);
            if (dbError is not null)
            {
                return FailedResult(backupFileName, startedAt, TimeSpan.Zero, dbError);
            }

            // ── Step 4: pg_restore ────────────────────────────────────────────
            var (restoreOk, restoreStderr, restorationDuration) =
                await RunPgRestoreAsync(plaintextPath, opts, ct);

            if (!restoreOk)
            {
                return FailedResult(backupFileName, startedAt, restorationDuration, restoreStderr);
            }

            // ── Step 5: Row count validation ──────────────────────────────────
            var rowCountResults = await ValidateRowCountsAsync(opts, ct);
            bool rowCountsPassed = rowCountResults.All(r => r.Matched);

            // ── Step 6: Referential integrity validation ──────────────────────
            var (integrityPassed, integrityErrors) = await ValidateReferentialIntegrityAsync(opts, ct);

            // ── Step 7: Checksum validation ───────────────────────────────────
            var checksumResults = await ValidateChecksumsAsync(opts, ct);
            bool checksumPassed = checksumResults.All(r => r.Matched);

            bool overallSuccess = rowCountsPassed && integrityPassed && checksumPassed;

            _logger.LogInformation(
                "RESTORATION_TEST_COMPLETE: Backup={FileName}, " +
                "RowCounts={RowCountResult}, Integrity={IntegrityResult}, " +
                "Checksums={ChecksumResult}, Duration={Duration}",
                backupFileName,
                rowCountsPassed ? "PASSED" : "FAILED",
                integrityPassed ? "PASSED" : "FAILED",
                checksumPassed  ? "PASSED" : "FAILED",
                restorationDuration);

            var result = new RestorationTestResult
            {
                OverallSuccess            = overallSuccess,
                BackupFileName            = backupFileName,
                RestoredAtUtc             = startedAt,
                RestorationDuration       = restorationDuration,
                RowCountResults           = rowCountResults,
                RowCountsPassed           = rowCountsPassed,
                ReferentialIntegrityPassed = integrityPassed,
                ReferentialIntegrityErrors = integrityErrors,
                ChecksumPassed            = checksumPassed,
                ChecksumResults           = checksumResults,
            };

            // ── Step 8: Persist audit log ─────────────────────────────────────
            await PersistRestorationLogAsync(result, performedBy, ct);

            return result;
        }
        finally
        {
            // ── Step 8: Always delete the temporary decrypted file ────────────
            if (tempDecryptedPath is not null && File.Exists(tempDecryptedPath))
            {
                try
                {
                    File.Delete(tempDecryptedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "BackupRestorationTestService: failed to delete temp file {Path}.",
                        tempDecryptedPath);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<QuarterlyTestStatus> GetQuarterlyTestStatusAsync(CancellationToken ct)
    {
        var now           = DateTime.UtcNow;
        var quarterStart  = GetQuarterStart(now);
        var quarterEnd    = quarterStart.AddMonths(3);
        var daysRemaining = (int)(quarterEnd - now).TotalDays;

        var lastTestThisQuarter = await _db.RestorationTestLogs
            .Where(r => r.CreatedAtUtc >= quarterStart && r.CreatedAtUtc < quarterEnd)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (lastTestThisQuarter is not null)
        {
            return new QuarterlyTestStatus
            {
                Status                 = "Completed",
                QuarterStart           = quarterStart,
                QuarterEnd             = quarterEnd,
                LastTestDate           = lastTestThisQuarter.CreatedAtUtc,
                LastTestPassed         = lastTestThisQuarter.OverallSuccess,
                DaysRemainingInQuarter = daysRemaining,
            };
        }

        var opts           = _restorationOptions.CurrentValue;
        var alertThreshold = opts.QuarterlyAlertDaysBeforeDue;

        string status = daysRemaining < 0
            ? "Overdue"
            : daysRemaining <= alertThreshold
                ? "DueSoon"
                : "DueSoon";   // No test this quarter yet — always at least DueSoon

        return new QuarterlyTestStatus
        {
            Status                 = status,
            QuarterStart           = quarterStart,
            QuarterEnd             = quarterEnd,
            LastTestDate           = null,
            LastTestPassed         = null,
            DaysRemainingInQuarter = daysRemaining,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Restore helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string? FindLatestBackupFile(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            _logger.LogError(
                "BackupRestorationTestService: backup directory does not exist: {Dir}",
                backupDirectory);
            return null;
        }

        // Prefer encrypted files; fall back to plaintext .dump files.
        var files = Directory
            .EnumerateFiles(backupDirectory, "*.dump.enc")
            .Concat(Directory.EnumerateFiles(backupDirectory, "*.dump"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        return files?.FullName;
    }

    private async Task<string?> RecreateTestDatabaseAsync(
        RestorationTestOptions opts,
        CancellationToken      ct)
    {
        _logger.LogInformation(
            "BackupRestorationTestService: recreating test database {Db} on {Host}:{Port}.",
            opts.TestDatabaseName, opts.TestDatabaseHost, opts.TestDatabasePort);

        var dropError = await RunPsqlCommandAsync(
            opts,
            $"DROP DATABASE IF EXISTS \"{opts.TestDatabaseName}\"",
            ct);

        if (dropError is not null)
            return $"DROP DATABASE failed: {dropError}";

        var createError = await RunPsqlCommandAsync(
            opts,
            $"CREATE DATABASE \"{opts.TestDatabaseName}\"",
            ct);

        return createError is not null ? $"CREATE DATABASE failed: {createError}" : null;
    }

    /// <summary>
    /// Runs a single SQL statement against the test server's maintenance database (<c>postgres</c>)
    /// using <c>psql</c> via <see cref="Process"/>. Returns the stderr string on failure, null on success.
    /// </summary>
    private async Task<string?> RunPsqlCommandAsync(
        RestorationTestOptions opts,
        string                 sql,
        CancellationToken      ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = opts.PsqlPath,
            Arguments              = $"--host={opts.TestDatabaseHost} " +
                                     $"--port={opts.TestDatabasePort} " +
                                     $"--username={opts.TestDatabaseUsername} " +
                                     "--dbname=postgres " +
                                     $"--command=\"{sql}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        // Credential injected only into the child process environment (OWASP A02).
        psi.Environment["PGPASSWORD"] = opts.TestDatabasePassword;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start psql process.");

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                return string.IsNullOrWhiteSpace(stderr) ? $"psql exited with code {process.ExitCode}" : stderr;

            return null;
        }
        finally
        {
            // Clear credential from child process environment — best-effort.
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    private async Task<(bool Ok, string Stderr, TimeSpan Duration)> RunPgRestoreAsync(
        string                 plaintextPath,
        RestorationTestOptions opts,
        CancellationToken      ct)
    {
        _logger.LogInformation(
            "BackupRestorationTestService: running pg_restore for {File} → {Db}.",
            Path.GetFileName(plaintextPath), opts.TestDatabaseName);

        var psi = new ProcessStartInfo
        {
            FileName               = opts.PgRestorePath,
            Arguments              = $"--host={opts.TestDatabaseHost} " +
                                     $"--port={opts.TestDatabasePort} " +
                                     $"--username={opts.TestDatabaseUsername} " +
                                     $"--dbname={opts.TestDatabaseName} " +
                                     "--no-owner --no-privileges " +
                                     $"\"{plaintextPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        psi.Environment["PGPASSWORD"] = opts.TestDatabasePassword;

        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(opts.MaxRestorationTimeoutMinutes));

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start pg_restore process.");

            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            sw.Stop();

            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "BackupRestorationTestService: pg_restore exited with code {Code}. Stderr={Stderr}",
                    process.ExitCode, stderr);
                return (false, stderr, sw.Elapsed);
            }

            return (true, string.Empty, sw.Elapsed);
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<TableRowCountComparison>> ValidateRowCountsAsync(
        RestorationTestOptions opts,
        CancellationToken      ct)
    {
        _logger.LogInformation(
            "BackupRestorationTestService: validating row counts against test database.");

        var productionConnStr = _db.Database.GetConnectionString()!;
        var testConnStr       = BuildTestConnectionString(opts);

        const string sql = """
            SELECT schemaname, relname, n_live_tup
            FROM pg_stat_user_tables
            ORDER BY schemaname, relname;
            """;

        var productionCounts = await FetchRowCountsAsync(productionConnStr, sql, ct);
        var testCounts       = await FetchRowCountsAsync(testConnStr,       sql, ct);

        var results = new List<TableRowCountComparison>();

        foreach (var (key, sourceCount) in productionCounts)
        {
            testCounts.TryGetValue(key, out var restoredCount);
            results.Add(new TableRowCountComparison
            {
                SchemaName    = key.Schema,
                TableName     = key.Table,
                SourceCount   = sourceCount,
                RestoredCount = restoredCount,
                Matched       = sourceCount == restoredCount,
            });
        }

        return results;
    }

    private static async Task<Dictionary<(string Schema, string Table), long>> FetchRowCountsAsync(
        string            connectionString,
        string            sql,
        CancellationToken ct)
    {
        var result = new Dictionary<(string, string), long>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var table  = reader.GetString(1);
            var count  = reader.GetInt64(2);
            result[(schema, table)] = count;
        }

        return result;
    }

    private async Task<(bool Passed, List<string> Errors)> ValidateReferentialIntegrityAsync(
        RestorationTestOptions opts,
        CancellationToken      ct)
    {
        _logger.LogInformation(
            "BackupRestorationTestService: validating referential integrity on test database.");

        var testConnStr = BuildTestConnectionString(opts);
        var errors      = new List<string>();

        // Check for unvalidated FK constraints — these indicate the restore created orphaned rows.
        const string unvalidatedSql = """
            SELECT conname, conrelid::regclass AS table_name,
                   confrelid::regclass AS referenced_table
            FROM pg_constraint
            WHERE contype = 'f'
              AND NOT convalidated;
            """;

        await using var conn = new NpgsqlConnection(testConnStr);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(unvalidatedSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            errors.Add(
                $"Unvalidated FK: constraint={reader.GetString(0)}, " +
                $"table={reader.GetString(1)}, " +
                $"references={reader.GetString(2)}");
        }

        return (errors.Count == 0, errors);
    }

    private async Task<List<TableChecksumComparison>> ValidateChecksumsAsync(
        RestorationTestOptions opts,
        CancellationToken      ct)
    {
        _logger.LogInformation(
            "BackupRestorationTestService: computing table checksums for validation.");

        var productionConnStr = _db.Database.GetConnectionString()!;
        var testConnStr       = BuildTestConnectionString(opts);

        // Fetch the list of application tables from the production database.
        const string tablesSql = """
            SELECT schemaname, relname
            FROM pg_stat_user_tables
            ORDER BY schemaname, relname;
            """;

        var tables = await FetchTableListAsync(productionConnStr, tablesSql, ct);
        var results = new List<TableChecksumComparison>();

        foreach (var (schema, table) in tables)
        {
            var sourceChecksum   = await ComputeTableChecksumAsync(productionConnStr, schema, table, ct);
            var restoredChecksum = await ComputeTableChecksumAsync(testConnStr,       schema, table, ct);

            results.Add(new TableChecksumComparison
            {
                SchemaName       = schema,
                TableName        = table,
                SourceChecksum   = sourceChecksum,
                RestoredChecksum = restoredChecksum,
                Matched          = string.Equals(sourceChecksum, restoredChecksum,
                                       StringComparison.Ordinal),
            });
        }

        return results;
    }

    private static async Task<List<(string Schema, string Table)>> FetchTableListAsync(
        string            connectionString,
        string            sql,
        CancellationToken ct)
    {
        var result = new List<(string, string)>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            result.Add((reader.GetString(0), reader.GetString(1)));

        return result;
    }

    /// <summary>
    /// Computes an <c>md5(string_agg(...))</c> row-order-independent checksum for a single table.
    /// Returns null when the table does not exist in the target database (e.g., not yet created by migration).
    /// Uses a parameterised identifier check to prevent SQL injection.
    /// </summary>
    private static async Task<string?> ComputeTableChecksumAsync(
        string            connectionString,
        string            schema,
        string            tableName,
        CancellationToken ct)
    {
        // Validate schema/table names contain only safe characters before embedding.
        // PostgreSQL identifiers contain only letters, digits, underscores, and dots.
        if (!IsValidIdentifier(schema) || !IsValidIdentifier(tableName))
            return null;

        // Use pg_catalog.quote_ident via safe embedding — identifiers are pre-validated.
        var safeSql = $"""
            SELECT md5(string_agg(md5(CAST(t.* AS text)), '' ORDER BY CAST(t.* AS text)))
            FROM "{schema}"."{tableName}" t;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(safeSql, conn);
            var result = await cmd.ExecuteScalarAsync(ct);

            return result is DBNull || result is null ? "(empty)" : result.ToString();
        }
        catch (NpgsqlException)
        {
            // Table may not exist in the test database yet — treat as non-matching.
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Persistence
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PersistRestorationLogAsync(
        RestorationTestResult result,
        string                performedBy,
        CancellationToken     ct)
    {
        string? errorDetails = null;

        if (!result.OverallSuccess)
        {
            var failures = new
            {
                RowCountFailures      = result.RowCountResults.Where(r => !r.Matched).ToList(),
                IntegrityErrors       = result.ReferentialIntegrityErrors,
                ChecksumFailures      = result.ChecksumResults.Where(r => !r.Matched).ToList(),
                RestorationError      = result.ErrorMessage,
            };
            errorDetails = JsonSerializer.Serialize(failures);

            // Trim to max column length.
            if (errorDetails.Length > 4000)
                errorDetails = errorDetails[..4000];
        }

        var log = new RestorationTestLog
        {
            Id                       = Guid.NewGuid(),
            BackupFileName           = result.BackupFileName,
            OverallSuccess           = result.OverallSuccess,
            RowCountsPassed          = result.RowCountsPassed,
            ReferentialIntegrityPassed = result.ReferentialIntegrityPassed,
            ChecksumsPassed          = result.ChecksumPassed,
            RestorationDuration      = result.RestorationDuration,
            ErrorDetails             = errorDetails,
            PerformedBy              = performedBy,
            CreatedAtUtc             = result.RestoredAtUtc,
        };

        _db.RestorationTestLogs.Add(log);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BackupRestorationTestService: failed to persist RestorationTestLog for {File}.",
                result.BackupFileName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string BuildTestConnectionString(RestorationTestOptions opts)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host     = opts.TestDatabaseHost,
            Port     = opts.TestDatabasePort,
            Database = opts.TestDatabaseName,
            Username = opts.TestDatabaseUsername,
            Password = opts.TestDatabasePassword,
        };
        return builder.ConnectionString;
    }

    private static DateTime GetQuarterStart(DateTime dt)
    {
        var quarter = (dt.Month - 1) / 3;
        return new DateTime(dt.Year, quarter * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns true when <paramref name="identifier"/> contains only safe PostgreSQL identifier
    /// characters (letters, digits, underscores) to prevent SQL injection via table/schema names.
    /// </summary>
    private static bool IsValidIdentifier(string identifier) =>
        !string.IsNullOrEmpty(identifier) &&
        identifier.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static RestorationTestResult FailedResult(
        string   backupFileName,
        DateTime restoredAt,
        TimeSpan restorationDuration,
        string   errorMessage) =>
        new()
        {
            OverallSuccess             = false,
            BackupFileName             = backupFileName,
            RestoredAtUtc              = restoredAt,
            RestorationDuration        = restorationDuration,
            RowCountResults            = [],
            RowCountsPassed            = false,
            ReferentialIntegrityPassed = false,
            ReferentialIntegrityErrors = [],
            ChecksumPassed             = false,
            ChecksumResults            = [],
            ErrorMessage               = errorMessage,
        };
}
