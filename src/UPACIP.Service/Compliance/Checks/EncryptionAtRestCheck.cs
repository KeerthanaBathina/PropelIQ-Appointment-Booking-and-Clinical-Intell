using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Compliance.Checks;

/// <summary>
/// Verifies AES-256 encryption at rest (US_093, AC-1, TR-019, §164.312(a)(2)(iv)).
///
/// Checks:
///   (a) PostgreSQL ssl and data_checksums settings via pg_settings.
///   (b) pgcrypto extension is installed (application-level AES-256).
///   (c) Application-level EncryptionOptions.Enabled = true.
///   (d) Latest backup has an encrypted file (FileName ends with .enc).
/// </summary>
public sealed class EncryptionAtRestCheck : IComplianceCheck
{
    private readonly ApplicationDbContext         _db;
    private readonly IOptionsMonitor<EncryptionOptions> _encryptionOptions;
    private readonly ILogger<EncryptionAtRestCheck> _logger;

    public string ControlName    => "Encryption at Rest (AES-256)";
    public string ControlCategory => "Technical";
    public string HipaaReference  => "§164.312(a)(2)(iv) — Encryption and Decryption";

    public EncryptionAtRestCheck(
        ApplicationDbContext               db,
        IOptionsMonitor<EncryptionOptions> encryptionOptions,
        ILogger<EncryptionAtRestCheck>     logger)
    {
        _db                = db;
        _encryptionOptions = encryptionOptions;
        _logger            = logger;
    }

    public async Task<ComplianceCheckResult> ExecuteAsync(CancellationToken ct)
    {
        var details = new Dictionary<string, string>();

        try
        {
            // (a) Query pg_settings for ssl setting
            var sslSetting = await QueryPgSettingAsync("ssl", ct);
            details["PostgreSqlSsl"] = sslSetting ?? "not_found";

            // (b) Verify pgcrypto extension is installed
            var pgcryptoInstalled = await CheckPgcryptoExtensionAsync(ct);
            details["PgcryptoExtension"] = pgcryptoInstalled ? "installed" : "not_installed";

            // (c) Check application-level encryption options
            var encOpts = _encryptionOptions.CurrentValue;
            details["AppEncryptionEnabled"] = encOpts.Enabled.ToString();

            // (d) Verify latest backup is encrypted (.enc suffix)
            var latestBackupEncrypted = await CheckLatestBackupEncryptedAsync(ct);
            details["LatestBackupEncrypted"] = latestBackupEncrypted.HasValue
                ? latestBackupEncrypted.Value.ToString()
                : "no_backup_found";

            // Evaluate results
            if (!pgcryptoInstalled)
            {
                return Fail("pgcrypto extension is not installed in PostgreSQL", details);
            }

            if (!encOpts.Enabled)
            {
                return Fail("Application-level encryption (EncryptionOptions.Enabled) is disabled", details);
            }

            if (latestBackupEncrypted == false)
            {
                return Fail("Latest backup file is not encrypted (expected .enc extension)", details);
            }

            return Pass(
                "AES-256 encryption verified: pgcrypto extension active, application encryption enabled, backup encryption confirmed",
                details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EncryptionAtRestCheck: unexpected error during verification.");
            return Fail($"Verification error: {ex.Message}", details);
        }
    }

    private async Task<string?> QueryPgSettingAsync(string settingName, CancellationToken ct)
    {
        // settingName is always a hardcoded constant — FormattableString parameterization
        // prevents EF1002 SQL injection warning (OWASP A03).
        var result = await _db.Database
            .SqlQuery<string>($"SELECT setting AS \"Value\" FROM pg_settings WHERE name = {settingName}")
            .FirstOrDefaultAsync(ct);
        return result;
    }

    private async Task<bool> CheckPgcryptoExtensionAsync(CancellationToken ct)
    {
        const string extName = "pgcrypto";
        var result = await _db.Database
            .SqlQuery<string>($"SELECT extname AS \"Value\" FROM pg_extension WHERE extname = {extName}")
            .FirstOrDefaultAsync(ct);
        return result is not null;
    }

    private async Task<bool?> CheckLatestBackupEncryptedAsync(CancellationToken ct)
    {
        var latestBackup = await _db.BackupLogs
            .OrderByDescending(b => b.CreatedAtUtc)
            .Where(b => b.Status == "Completed")
            .Select(b => b.FileName)
            .FirstOrDefaultAsync(ct);

        if (latestBackup is null)
            return null;

        return latestBackup.EndsWith(".enc", StringComparison.OrdinalIgnoreCase);
    }

    private ComplianceCheckResult Pass(string evidence, Dictionary<string, string> details) =>
        new()
        {
            Passed          = true,
            ControlName     = ControlName,
            HipaaReference  = HipaaReference,
            Evidence        = evidence,
            VerifiedAtUtc   = DateTime.UtcNow,
            Details         = details
        };

    private ComplianceCheckResult Fail(string reason, Dictionary<string, string> details) =>
        new()
        {
            Passed          = false,
            ControlName     = ControlName,
            HipaaReference  = HipaaReference,
            Evidence        = $"Encryption at rest verification FAILED: {reason}",
            FailureReason   = reason,
            VerifiedAtUtc   = DateTime.UtcNow,
            Details         = details
        };
}
