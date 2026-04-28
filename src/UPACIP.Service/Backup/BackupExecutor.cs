using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Encapsulates the <c>pg_dump</c> process invocation and SHA-256 checksum
/// computation for a single backup attempt (US_088, AC-3).
///
/// Registered as a Singleton — <c>pg_dump</c> creates a new child process per
/// call so there is no shared mutable state.
/// </summary>
public interface IBackupExecutor
{
    /// <summary>
    /// Invokes <c>pg_dump</c>, waits for completion, computes the SHA-256
    /// checksum of the output file, and returns a <see cref="BackupResult"/>.
    /// </summary>
    /// <param name="isRetry">Pass <c>true</c> when this is a retry attempt (AC-4).</param>
    /// <param name="ct">Cancellation token; honours application-shutdown gracefully.</param>
    Task<BackupResult> ExecuteBackupAsync(bool isRetry = false, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton implementation of <see cref="IBackupExecutor"/>.
///
/// Security posture:
/// <list type="bullet">
///   <item>
///     The PostgreSQL password is passed to <c>pg_dump</c> via the <c>PGPASSWORD</c>
///     environment variable on the child process only — never via command-line arguments
///     (which are visible in Windows process listings and event logs — OWASP A02).
///   </item>
///   <item>
///     <c>PGPASSWORD</c> is set explicitly in the child-process environment rather than
///     inheriting the parent process environment, so the password does not leak to other
///     child processes spawned by the application.
///   </item>
///   <item>
///     The password value is never logged at any log level.
///   </item>
/// </list>
///
/// Backup filename convention: <c>upacip_backup_yyyyMMdd_HHmmss.dump</c>.
/// </summary>
public sealed class BackupExecutor : IBackupExecutor
{
    private readonly IOptionsMonitor<BackupOptions> _options;
    private readonly ILogger<BackupExecutor>        _logger;

    public BackupExecutor(
        IOptionsMonitor<BackupOptions> options,
        ILogger<BackupExecutor>        logger)
    {
        _options = options;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<BackupResult> ExecuteBackupAsync(
        bool              isRetry = false,
        CancellationToken ct      = default)
    {
        var opts      = _options.CurrentValue;
        var sw        = Stopwatch.StartNew();
        var timestamp = DateTime.Now; // local time for filename — 2 AM local
        var fileName  = $"upacip_backup_{timestamp:yyyyMMdd_HHmmss}.dump";
        var filePath  = Path.Combine(opts.BackupDirectory, fileName);

        _logger.LogInformation(
            "BackupExecutor: starting pg_dump. File={FileName}, IsRetry={IsRetry}",
            fileName, isRetry);

        // Ensure the backup directory exists (creates intermediate directories).
        Directory.CreateDirectory(opts.BackupDirectory);

        // ── Build pg_dump arguments ────────────────────────────────────────────
        // Password is intentionally excluded from args — set via PGPASSWORD env var.
        var args = string.Join(" ",
            $"--host={opts.DbHost}",
            $"--port={opts.DbPort}",
            $"--username={opts.DbUsername}",
            $"--format={opts.BackupFormat}",
            "--no-owner",
            "--no-privileges",
            $"--file=\"{filePath}\"",
            opts.DatabaseName);

        // ── Configure the pg_dump child process ───────────────────────────────
        var psi = new ProcessStartInfo
        {
            FileName               = opts.PgDumpPath,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = false,
            CreateNoWindow         = true,
        };

        // Set PGPASSWORD only in the child process environment — not inherited from parent.
        // This prevents accidental leakage to other child processes.
        psi.Environment["PGPASSWORD"] = opts.DbPassword;

        string stderr    = string.Empty;
        int    exitCode  = -1;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    $"Failed to start pg_dump at path: {opts.PgDumpPath}");

            // Read stderr asynchronously to avoid deadlocks on the output buffer.
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            // Wait for the process to exit, respecting the cancellation token.
            await process.WaitForExitAsync(ct);

            stderr   = await stderrTask;
            exitCode = process.ExitCode;
        }
        finally
        {
            // Security hygiene: clear the password from the PSI environment object.
            // The child process has already received it via its own copy of the environment.
            psi.Environment.Remove("PGPASSWORD");
        }

        sw.Stop();
        var completedAt = DateTime.UtcNow;

        // ── Failure path ──────────────────────────────────────────────────────
        if (exitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? $"pg_dump exited with code {exitCode} (no stderr output)"
                : stderr.Length > 2000
                    ? stderr[..2000]
                    : stderr;

            _logger.LogWarning(
                "BackupExecutor: pg_dump failed. ExitCode={ExitCode}, IsRetry={IsRetry}, " +
                "Duration={DurationMs}ms. Stderr (truncated): {Stderr}",
                exitCode, isRetry, (int)sw.Elapsed.TotalMilliseconds, errorMessage);

            return new BackupResult
            {
                Success          = false,
                FilePath         = filePath,
                FileName         = fileName,
                FileSizeBytes    = 0,
                Duration         = sw.Elapsed,
                Checksum         = string.Empty,
                ErrorMessage     = errorMessage,
                IsRetry          = isRetry,
                CompletedAtUtc   = completedAt,
            };
        }

        // ── Success path: compute SHA-256 checksum ────────────────────────────
        long   fileSizeBytes = 0;
        string checksum      = string.Empty;

        if (File.Exists(filePath))
        {
            fileSizeBytes = new FileInfo(filePath).Length;

            // SHA256.HashData reads the entire file — acceptable for backup files.
            // For very large files (> 1 GB) a streaming approach would be preferred,
            // but backup files for this application are expected to be moderate in size.
            var hashBytes = await ComputeChecksumAsync(filePath, ct);
            checksum      = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        else
        {
            _logger.LogWarning(
                "BackupExecutor: pg_dump exited 0 but output file not found: {FilePath}",
                filePath);
        }

        _logger.LogInformation(
            "BackupExecutor: pg_dump succeeded. FileName={FileName}, " +
            "Size={SizeBytes} bytes, Duration={DurationMs}ms, " +
            "Checksum={Checksum}, IsRetry={IsRetry}",
            fileName, fileSizeBytes, (int)sw.Elapsed.TotalMilliseconds, checksum, isRetry);

        return new BackupResult
        {
            Success          = true,
            FilePath         = filePath,
            FileName         = fileName,
            FileSizeBytes    = fileSizeBytes,
            Duration         = sw.Elapsed,
            Checksum         = checksum,
            ErrorMessage     = null,
            IsRetry          = isRetry,
            CompletedAtUtc   = completedAt,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams the file through SHA-256 to produce a checksum without blocking.
    /// Uses a 256 KB buffer — suitable for multi-GB backup files.
    /// </summary>
    private static async Task<byte[]> ComputeChecksumAsync(
        string            filePath,
        CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 262144,  // 256 KB
            useAsync: true);

        return await sha256.ComputeHashAsync(stream, ct);
    }
}
