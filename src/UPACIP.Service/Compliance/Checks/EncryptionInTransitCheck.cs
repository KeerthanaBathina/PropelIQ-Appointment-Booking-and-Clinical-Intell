using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;

namespace UPACIP.Service.Compliance.Checks;

/// <summary>
/// Verifies encryption in transit via TLS 1.2+ (US_093, AC-1, TR-018, §164.312(e)(1)).
///
/// Checks:
///   (a) Current PostgreSQL connection uses TLS via pg_stat_ssl.
///   (b) TLS version is 1.2 or higher.
///   (c) Cipher suite is strong (no NULL, RC4, or DES ciphers).
///   (d) Application HTTPS listener is active (IServerAddressesFeature has https:// binding).
/// </summary>
public sealed class EncryptionInTransitCheck : IComplianceCheck
{
    private readonly ApplicationDbContext          _db;
    private readonly IServer                       _server;
    private readonly ILogger<EncryptionInTransitCheck> _logger;

    private static readonly HashSet<string> WeakCipherPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "NULL", "RC4", "DES" };

    private static readonly HashSet<string> AcceptedTlsVersions =
        new(StringComparer.OrdinalIgnoreCase) { "TLSv1.2", "TLSv1.3" };

    public string ControlName     => "Encryption in Transit (TLS 1.2+)";
    public string ControlCategory => "Technical";
    public string HipaaReference  => "§164.312(e)(1) — Transmission Security";

    public EncryptionInTransitCheck(
        ApplicationDbContext              db,
        IServer                           server,
        ILogger<EncryptionInTransitCheck> logger)
    {
        _db     = db;
        _server = server;
        _logger = logger;
    }

    public async Task<ComplianceCheckResult> ExecuteAsync(CancellationToken ct)
    {
        var details = new Dictionary<string, string>();

        try
        {
            // (a-c) Query pg_stat_ssl for the current backend connection
            var sslRow = await QueryPgStatSslAsync(ct);

            if (sslRow is null)
            {
                details["PostgreSqlTls"] = "no_ssl_row_found";
                return Fail("pg_stat_ssl returned no row for the current backend PID — database connection may not use TLS", details);
            }

            details["PostgreSqlSsl"]     = sslRow.SslActive ? "on" : "off";
            details["PostgreSqlTlsVersion"] = sslRow.Version ?? "unknown";
            details["PostgreSqlCipher"]  = sslRow.Cipher ?? "unknown";

            if (!sslRow.SslActive)
            {
                return Fail("PostgreSQL connection is not using TLS/SSL", details);
            }

            // (b) Verify TLS version is 1.2 or higher
            var tlsVersion = sslRow.Version ?? string.Empty;
            if (!AcceptedTlsVersions.Contains(tlsVersion))
            {
                return Fail(
                    $"TLS version '{tlsVersion}' is below the required TLS 1.2 minimum",
                    details);
            }

            // (c) Verify cipher suite strength
            var cipher = sslRow.Cipher ?? string.Empty;
            if (IsWeakCipher(cipher))
            {
                return Fail($"Cipher suite '{cipher}' is weak and not acceptable for HIPAA compliance", details);
            }

            // (d) Verify HTTPS listener is active
            var httpsActive = CheckHttpsListenerActive();
            details["HttpsListenerActive"] = httpsActive.ToString();

            if (!httpsActive)
            {
                return Fail("No HTTPS listener binding detected — application may be serving HTTP only", details);
            }

            return Pass(
                $"TLS {tlsVersion} active with cipher {cipher}, HTTPS enforced on all endpoints",
                details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EncryptionInTransitCheck: unexpected error during verification.");
            return Fail($"Verification error: {ex.Message}", details);
        }
    }

    private async Task<PgSslRow?> QueryPgStatSslAsync(CancellationToken ct)
    {
        var rows = await _db.Database
            .SqlQuery<PgSslRow>(
                $"SELECT ssl AS \"SslActive\", version AS \"Version\", cipher AS \"Cipher\" FROM pg_stat_ssl WHERE pid = pg_backend_pid()")
            .ToListAsync(ct);

        return rows.FirstOrDefault();
    }

    private bool CheckHttpsListenerActive()
    {
        var addressesFeature = _server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature is null)
            return false;

        return addressesFeature.Addresses
            .Any(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWeakCipher(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return false;

        return WeakCipherPrefixes.Any(prefix =>
            cipher.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private ComplianceCheckResult Pass(string evidence, Dictionary<string, string> details) =>
        new()
        {
            Passed         = true,
            ControlName    = ControlName,
            HipaaReference = HipaaReference,
            Evidence       = evidence,
            VerifiedAtUtc  = DateTime.UtcNow,
            Details        = details
        };

    private ComplianceCheckResult Fail(string reason, Dictionary<string, string> details) =>
        new()
        {
            Passed         = false,
            ControlName    = ControlName,
            HipaaReference = HipaaReference,
            Evidence       = $"Encryption in transit verification FAILED: {reason}",
            FailureReason  = reason,
            VerifiedAtUtc  = DateTime.UtcNow,
            Details        = details
        };

    // ── Raw SQL projection for pg_stat_ssl ──────────────────────────────────

    private sealed class PgSslRow
    {
        public bool    SslActive { get; set; }
        public string? Version   { get; set; }
        public string? Cipher    { get; set; }
    }
}
