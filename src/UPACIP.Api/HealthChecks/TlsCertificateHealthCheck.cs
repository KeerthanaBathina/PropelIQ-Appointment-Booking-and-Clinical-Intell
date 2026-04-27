using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Health check that monitors the TLS certificate bound to the HTTPS endpoint (US_063 AC-3).
///
/// Status mapping:
///   <see cref="HealthStatus.Healthy"/>   — certificate is valid and expires in more than
///                                           <c>Security:Tls:CertExpiryWarningDays</c> days.
///   <see cref="HealthStatus.Degraded"/>  — certificate is valid but expires within the
///                                           warning threshold (default 30 days). Auto-renewal
///                                           should be triggered.
///   <see cref="HealthStatus.Unhealthy"/> — certificate has already expired, the configured
///                                           path does not exist, or the file cannot be read.
///
/// The check reads the certificate from the path configured in
/// <c>Security:Tls:CertificatePath</c>.  When that path is absent (e.g. development with a
/// Kestrel dev-cert) the check returns <see cref="HealthStatus.Healthy"/> with a descriptive
/// message so local developer workflow is unaffected.
///
/// Registered on the <c>/ready</c> health endpoint (tag "ready") so load balancers are
/// notified of near-expiry or expired certificates before traffic is impacted.
/// </summary>
public sealed class TlsCertificateHealthCheck : IHealthCheck
{
    private readonly IConfiguration                       _configuration;
    private readonly ILogger<TlsCertificateHealthCheck>   _logger;

    public TlsCertificateHealthCheck(
        IConfiguration                      configuration,
        ILogger<TlsCertificateHealthCheck>  logger)
    {
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken  cancellationToken = default)
    {
        var certPath = _configuration["Security:Tls:CertificatePath"];

        // No certificate path configured — development scenario using the Kestrel dev-cert.
        // Return Healthy so the local developer workflow is not broken.
        if (string.IsNullOrWhiteSpace(certPath))
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "TLS certificate path not configured — using development certificate."));
        }

        if (!File.Exists(certPath))
        {
            _logger.LogCritical(
                "TLS certificate file not found at path {CertPath}. " +
                "HTTPS endpoint cannot start. Verify the Security:Tls:CertificatePath " +
                "configuration value and ensure the certificate is deployed.",
                certPath);

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"TLS certificate file not found: {certPath}"));
        }

        try
        {
            // Load the certificate without its private key for inspection only.
            using var cert = new X509Certificate2(certPath);

            var now         = DateTime.UtcNow;
            var expiresAt   = cert.NotAfter.ToUniversalTime();
            var daysLeft    = (expiresAt - now).TotalDays;

            var warningDays = _configuration.GetValue<int>(
                "Security:Tls:CertExpiryWarningDays",
                defaultValue: 30);

            if (daysLeft < 0)
            {
                _logger.LogCritical(
                    "TLS certificate has EXPIRED. Subject={Subject} Thumbprint={Thumbprint} " +
                    "ExpiredAt={ExpiredAt:O}. Replace the certificate immediately.",
                    cert.Subject, cert.Thumbprint, expiresAt);

                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"TLS certificate expired {Math.Abs(daysLeft):F0} day(s) ago " +
                    $"(expired {expiresAt:yyyy-MM-dd})."));
            }

            if (daysLeft <= warningDays)
            {
                _logger.LogWarning(
                    "TLS certificate nearing expiry. Subject={Subject} Thumbprint={Thumbprint} " +
                    "DaysRemaining={DaysRemaining:F0} ExpiresAt={ExpiresAt:O}. " +
                    "Trigger auto-renewal via Let's Encrypt before expiry.",
                    cert.Subject, cert.Thumbprint, daysLeft, expiresAt);

                return Task.FromResult(HealthCheckResult.Degraded(
                    $"TLS certificate expires in {daysLeft:F0} day(s) ({expiresAt:yyyy-MM-dd}). " +
                    $"Renewal required within {warningDays} days."));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"TLS certificate valid. Expires {expiresAt:yyyy-MM-dd} " +
                $"({daysLeft:F0} day(s) remaining)."));
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Failed to read TLS certificate from {CertPath}. " +
                "Verify the file is a valid PFX/PEM and is readable by the application account.",
                certPath);

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"TLS certificate could not be read: {ex.Message}",
                exception: ex));
        }
    }
}
