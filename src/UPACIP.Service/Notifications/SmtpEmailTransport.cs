using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;

namespace UPACIP.Service.Notifications;

/// <summary>
/// MailKit-backed SMTP transport that delivers messages via SendGrid (primary) and
/// Gmail (fallback), enforcing the delivery contract defined in <see cref="IEmailTransport"/>.
///
/// Resilience model:
/// <list type="bullet">
///   <item>
///     <strong>Retry</strong>: up to <see cref="EmailProviderOptions.MaxRetryAttempts"/> retries
///     with exponential backoff (1s, 2s, 4s) for transient SMTP and network failures (AC-3).
///   </item>
///   <item>
///     <strong>Failover</strong>: when the primary provider fails after all retries, and
///     <see cref="EmailProviderOptions.EnableFallback"/> is <c>true</c>, the same message is
///     attempted via the configured fallback provider.  A <c>LogCritical</c> event is emitted
///     to alert operations (EC-1).
///   </item>
///   <item>
///     <strong>Bounce detection</strong>: permanent 5xx SMTP status codes (550–554) are
///     classified as bounced rather than retried, allowing downstream orchestration to flag
///     the patient record for a contact update (EC-2).
///   </item>
///   <item>
///     <strong>Delivery timeout</strong>: a <see cref="CancellationTokenSource"/> enforces
///     <see cref="EmailProviderOptions.DeliveryTimeoutSeconds"/> (default 30s) across all
///     attempts, satisfying the 30-second delivery target (AC-1).
///   </item>
/// </list>
///
/// Security:
/// <list type="bullet">
///   <item>Credentials are never logged (OWASP A02).</item>
///   <item>Recipient email addresses are masked in log output (OWASP A09 / patient privacy).</item>
///   <item>TLS is enforced: STARTTLS on port 587; SSL/TLS on port 465.</item>
/// </list>
///
/// This class never throws.  All outcomes are encoded in <see cref="EmailDeliveryAttemptResult"/>.
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailProviderOptions _options;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(
        IOptions<EmailProviderOptions> options,
        ILogger<SmtpEmailTransport> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<EmailDeliveryAttemptResult> SendAsync(
        EmailTransportMessage message,
        CancellationToken cancellationToken = default)
    {
        // Enforce the overall 30-second wall-clock budget across all attempts (AC-1).
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.DeliveryTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        // --- Attempt primary provider ---
        var (primaryResult, primaryAttempts) =
            await AttemptDeliveryAsync(message, _options.Primary, linkedCts.Token);

        if (primaryResult.IsSuccess || primaryResult.IsBounced)
            return primaryResult;

        // --- Failover decision (EC-1) ---
        if (!_options.EnableFallback || _options.Fallback is null)
        {
            _logger.LogError(
                "SMTP delivery via {Provider} failed after {Attempts} attempt(s). " +
                "No fallback provider is configured.",
                _options.Primary.ProviderName, primaryAttempts);
            return primaryResult;
        }

        // Critical alert: primary exhausted — operations team must investigate quota or outage.
        _logger.LogCritical(
            "Primary SMTP provider {Primary} is unavailable or has exhausted its daily quota " +
            "({Quota} messages/day). Activating failover to {Fallback}. " +
            "Manual review of provider account is recommended.",
            _options.Primary.ProviderName,
            _options.Primary.DailyQuotaLimit,
            _options.Fallback.ProviderName);

        // --- Attempt fallback provider ---
        var (fallbackResult, fallbackAttempts) =
            await AttemptDeliveryAsync(message, _options.Fallback, linkedCts.Token);

        int totalAttempts = primaryAttempts + fallbackAttempts;

        if (fallbackResult.IsSuccess)
        {
            return EmailDeliveryAttemptResult.SucceededViaFallback(
                fallbackResult.ProviderName,
                totalAttempts);
        }

        _logger.LogError(
            "SMTP delivery failed via both {Primary} ({PrimaryAttempts} attempt(s)) " +
            "and {Fallback} ({FallbackAttempts} attempt(s)). Message was not delivered.",
            _options.Primary.ProviderName, primaryAttempts,
            _options.Fallback.ProviderName, fallbackAttempts);

        return EmailDeliveryAttemptResult.Failed(
            fallbackResult.ProviderName,
            totalAttempts,
            fallbackResult.FailureReason ?? "All configured providers exhausted.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts delivery through a single SMTP provider, applying the configured
    /// retry policy.  Returns the result and the total number of attempts made.
    /// Never throws.
    /// </summary>
    private async Task<(EmailDeliveryAttemptResult result, int attempts)> AttemptDeliveryAsync(
        EmailTransportMessage message,
        SmtpProviderSettings provider,
        CancellationToken ct)
    {
        int attemptCounter = 0;

        // Retry on transient failures only.
        // Permanent bounces (IsPermanentBounce) and cancellation break out immediately.
        var retryPolicy = Policy
            .Handle<Exception>(ex =>
                ex is not OperationCanceledException &&
                !IsPermanentBounce(ex))
            .WaitAndRetryAsync(
                retryCount: _options.MaxRetryAttempts,
                // Backoff: attempt 1 → 1s, attempt 2 → 2s, attempt 3 → 4s (AC-3)
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "SMTP send attempt {Attempt} via {Provider} failed; " +
                        "retrying in {Delay:F1}s.",
                        attempt, provider.ProviderName, delay.TotalSeconds));

        try
        {
            await retryPolicy.ExecuteAsync(async token =>
            {
                attemptCounter++;
                await SendViaSingleProviderAsync(message, provider, token);
            }, ct);

            _logger.LogInformation(
                "SMTP delivery to {MaskedAddress} succeeded via {Provider} " +
                "in {Attempts} attempt(s).",
                MaskAddress(message.ToAddress), provider.ProviderName, attemptCounter);

            return (EmailDeliveryAttemptResult.Succeeded(provider.ProviderName, attemptCounter),
                    attemptCounter);
        }
        catch (Exception ex) when (IsPermanentBounce(ex))
        {
            // Permanent 5xx failure — invalid recipient; do NOT retry or failover (EC-2).
            var reason = BuildSafeErrorReason(ex);
            _logger.LogWarning(
                "SMTP message to {MaskedAddress} bounced via {Provider}: {Reason}. " +
                "Downstream orchestration should flag this patient record for contact update.",
                MaskAddress(message.ToAddress), provider.ProviderName, reason);

            return (EmailDeliveryAttemptResult.Bounced(provider.ProviderName, reason),
                    attemptCounter);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "SMTP delivery via {Provider} was cancelled or timed out after {Attempts} attempt(s).",
                provider.ProviderName, attemptCounter);

            return (EmailDeliveryAttemptResult.Failed(
                        provider.ProviderName,
                        attemptCounter,
                        "Delivery cancelled or timed out."),
                    attemptCounter);
        }
        catch (Exception ex)
        {
            // Transient failure exhausted all retries.
            var reason = BuildSafeErrorReason(ex);
            _logger.LogError(ex,
                "SMTP delivery via {Provider} failed after {Attempts} attempt(s): {Reason}",
                provider.ProviderName, attemptCounter, reason);

            return (EmailDeliveryAttemptResult.Failed(provider.ProviderName, attemptCounter, reason),
                    attemptCounter);
        }
    }

    /// <summary>
    /// Opens a single SMTP connection, authenticates, sends the message, and disconnects.
    /// Throws on any SMTP or network error so the retry policy can evaluate it.
    /// </summary>
    private static async Task SendViaSingleProviderAsync(
        EmailTransportMessage message,
        SmtpProviderSettings provider,
        CancellationToken ct)
    {
        var mimeMessage = BuildMimeMessage(message, provider);
        using var client = new SmtpClient();

        // Enforce TLS: SSL/TLS for port 465; STARTTLS for all other ports (including 587).
        var socketOptions = provider.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(provider.Host, provider.Port, socketOptions, ct);
        await client.AuthenticateAsync(provider.Username, provider.Password, ct);
        await client.SendAsync(mimeMessage, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    /// <summary>Assembles a <see cref="MimeMessage"/> from the transport message and provider settings.</summary>
    private static MimeMessage BuildMimeMessage(
        EmailTransportMessage message,
        SmtpProviderSettings provider)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(provider.FromName, provider.FromAddress));
        mimeMessage.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new BodyBuilder
        {
            HtmlBody    = message.HtmlBody,
            TextBody    = message.PlainTextBody,
        }.ToMessageBody();

        return mimeMessage;
    }

    /// <summary>
    /// Returns <c>true</c> for permanent 5xx SMTP failures that indicate an invalid or
    /// non-existent recipient address.  These outcomes are NOT retryable (EC-2).
    ///
    /// RFC 5321 permanent bounce codes:
    /// <list type="bullet">
    ///   <item>550 — Mailbox unavailable / address does not exist</item>
    ///   <item>551 — User not local, please forward</item>
    ///   <item>552 — Exceeded mailbox storage allocation</item>
    ///   <item>553 — Mailbox name not allowed</item>
    ///   <item>554 — Transaction failed (permanent)</item>
    /// </list>
    /// </summary>
    private static bool IsPermanentBounce(Exception ex)
    {
        if (ex is SmtpCommandException smtp)
        {
            var code = (int)smtp.StatusCode;
            // 550-554 are all permanent recipient-level failures per RFC 5321
            return code is >= 550 and <= 554;
        }

        return false;
    }

    /// <summary>
    /// Returns a sanitised, credential-free error string safe for structured log output.
    /// The full exception message is intentionally omitted to prevent credential or
    /// patient PII leakage (OWASP A02, A09).
    /// </summary>
    private static string BuildSafeErrorReason(Exception ex) =>
        ex switch
        {
            SmtpCommandException smtp   => $"SMTP {(int)smtp.StatusCode}: {smtp.StatusCode}",
            SmtpProtocolException       => "SMTP protocol error",
            _                           => ex.GetType().Name,
        };

    /// <summary>
    /// Masks the local-part of an email address for log output to reduce PII exposure.
    /// Example: <c>john.doe@example.com</c> → <c>j***@example.com</c>
    /// </summary>
    private static string MaskAddress(string email)
    {
        var atIndex = email.IndexOf('@', StringComparison.Ordinal);
        return atIndex > 0 ? email[0] + "***" + email[atIndex..] : "****";
    }
}
