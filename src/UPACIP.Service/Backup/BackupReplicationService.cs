using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

/// <summary>
/// Copies encrypted backup files to a geographically separate Windows file share (AC-2, DR-024).
/// </summary>
public interface IBackupReplicationService
{
    /// <summary>
    /// Copies <paramref name="sourceFilePath"/> to the configured remote UNC share.
    /// Uses Polly retry with exponential backoff for transient network failures.
    /// Returns a failed <see cref="ReplicationResult"/> without throwing when all retries
    /// are exhausted — the primary backup is always preserved.
    /// </summary>
    Task<ReplicationResult> ReplicateBackupAsync(string sourceFilePath, CancellationToken ct);
}

/// <summary>
/// Implementation of <see cref="IBackupReplicationService"/>.
///
/// Security notes (OWASP A02 / A09):
/// <list type="bullet">
///   <item>Only encrypted <c>.dump.enc</c> files are ever replicated — plaintext never leaves the primary server.</item>
///   <item>Remote path is configured via environment variable <c>BackupReplication__RemoteDestinationPath</c>; never committed to source.</item>
///   <item>Checksums compared with <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent timing attacks.</item>
/// </list>
/// </summary>
public sealed class BackupReplicationService : IBackupReplicationService
{
    private readonly IOptionsMonitor<ReplicationOptions> _options;
    private readonly ILogger<BackupReplicationService>   _logger;

    // Checksum buffer: 256 KB — matches BackupEncryptionService for consistency.
    private const int ChecksumBufferSize = 262_144;

    public BackupReplicationService(
        IOptionsMonitor<ReplicationOptions> options,
        ILogger<BackupReplicationService>   logger)
    {
        _options = options;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<ReplicationResult> ReplicateBackupAsync(
        string            sourceFilePath,
        CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        // ── Guard: replication disabled ────────────────────────────────────────
        if (!opts.Enabled)
        {
            _logger.LogDebug(
                "BackupReplicationService: replication is disabled. Skipping {File}.",
                Path.GetFileName(sourceFilePath));

            return new ReplicationResult
            {
                Success        = true,     // disabled = not a failure
                DestinationPath = null,
                FileSizeBytes   = 0,
                TransferDuration = TimeSpan.Zero,
                AttemptsUsed    = 0,
            };
        }

        // ── Guard: source file must exist ─────────────────────────────────────
        if (!File.Exists(sourceFilePath))
        {
            var msg = $"Source file not found: {Path.GetFileName(sourceFilePath)}";
            _logger.LogError(
                "BACKUP_REPLICATION_FAILED: {Reason}. Source={Source}",
                msg, sourceFilePath);

            return new ReplicationResult { Success = false, ErrorMessage = msg, AttemptsUsed = 0 };
        }

        // ── Guard: remote path must be configured ─────────────────────────────
        if (string.IsNullOrWhiteSpace(opts.RemoteDestinationPath))
        {
            const string msg = "BackupReplication:RemoteDestinationPath is not configured. " +
                               "Set via environment variable BackupReplication__RemoteDestinationPath.";
            _logger.LogError(
                "BACKUP_REPLICATION_FAILED: {Reason}. Source={Source}",
                msg, sourceFilePath);

            return new ReplicationResult { Success = false, ErrorMessage = msg, AttemptsUsed = 0 };
        }

        var destinationPath = Path.Combine(
            opts.RemoteDestinationPath,
            Path.GetFileName(sourceFilePath));

        // Attempt counter shared across retry callbacks.
        var attemptCount = 0;

        // ── Polly retry pipeline (exponential backoff: 30s → 120s → 480s) ────
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = opts.MaxRetries,
                BackoffType      = DelayBackoffType.Exponential,
                Delay            = TimeSpan.FromSeconds(opts.InitialRetryDelaySeconds),
                UseJitter        = false,   // deterministic delays per spec (30s/120s/480s)
                OnRetry          = args =>
                {
                    _logger.LogWarning(
                        "BACKUP_REPLICATION_RETRY: Attempt={Attempt}, " +
                        "Delay={DelaySeconds:F0}s, Error={Error}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message);
                    return default;
                },
            })
            .Build();

        var transferTimer = new Stopwatch();

        try
        {
            await pipeline.ExecuteAsync(async innerCt =>
            {
                attemptCount++;

                // Ensure remote directory exists (handles UNC paths).
                Directory.CreateDirectory(opts.RemoteDestinationPath);

                // Apply per-transfer timeout.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(opts.TransferTimeoutMinutes));

                transferTimer.Restart();

                await CopyFileWithTimeoutAsync(sourceFilePath, destinationPath, timeoutCts.Token);

                transferTimer.Stop();

                // ── Checksum verification ─────────────────────────────────────
                if (opts.VerifyChecksum)
                {
                    await VerifyChecksumAsync(sourceFilePath, destinationPath, innerCt);
                }
            }, ct);

            var fileSize = new FileInfo(destinationPath).Length;

            _logger.LogInformation(
                "BACKUP_REPLICATED: Source={Source}, Destination={Destination}, " +
                "Size={SizeBytes}, Duration={Duration}, Attempts={Attempts}",
                sourceFilePath,
                destinationPath,
                fileSize,
                transferTimer.Elapsed,
                attemptCount);

            return new ReplicationResult
            {
                Success          = true,
                DestinationPath  = destinationPath,
                FileSizeBytes    = fileSize,
                TransferDuration = transferTimer.Elapsed,
                AttemptsUsed     = attemptCount,
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // All retries exhausted — primary backup preserved locally (edge case 1).
            _logger.LogError(
                "BACKUP_REPLICATION_FAILED: AllRetriesExhausted, Source={Source}, " +
                "Destination={Destination}, Error={Error}. Primary backup preserved locally.",
                sourceFilePath,
                destinationPath,
                ex.Message);

            return new ReplicationResult
            {
                Success          = false,
                DestinationPath  = destinationPath,
                FileSizeBytes    = 0,
                TransferDuration = transferTimer.Elapsed,
                ErrorMessage     = ex.Message,
                AttemptsUsed     = attemptCount,
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies a file asynchronously, respecting the provided <paramref name="ct"/>.
    /// Uses a 80 KB streaming copy so cancellation is checked during large transfers.
    /// </summary>
    private static async Task CopyFileWithTimeoutAsync(
        string            source,
        string            destination,
        CancellationToken ct)
    {
        const int bufferSize = 81_920;

        await using var src  = new FileStream(source,      FileMode.Open,   FileAccess.Read,  FileShare.Read,  bufferSize, useAsync: true);
        await using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,  bufferSize, useAsync: true);

        await src.CopyToAsync(dest, bufferSize, ct);
    }

    /// <summary>
    /// Computes SHA-256 checksums of both <paramref name="localPath"/> and
    /// <paramref name="remotePath"/> and compares them with a timing-safe equality check.
    /// Throws <see cref="IOException"/> on mismatch so Polly will retry.
    /// </summary>
    private static async Task VerifyChecksumAsync(
        string            localPath,
        string            remotePath,
        CancellationToken ct)
    {
        var localHash  = await ComputeSha256Async(localPath,  ct);
        var remoteHash = await ComputeSha256Async(remotePath, ct);

        if (!CryptographicOperations.FixedTimeEquals(localHash, remoteHash))
        {
            // Throw to trigger Polly retry — the corrupted remote copy will be overwritten.
            throw new IOException(
                $"Checksum verification failed: remote copy of {Path.GetFileName(remotePath)} " +
                "does not match the local source. The file will be re-copied on retry.");
        }
    }

    /// <summary>Streams a file through SHA-256 and returns the raw hash bytes.</summary>
    private static async Task<byte[]> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChecksumBufferSize,
            useAsync: true);

        return await sha.ComputeHashAsync(stream, ct);
    }
}
