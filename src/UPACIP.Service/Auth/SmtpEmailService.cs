using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;

namespace UPACIP.Service.Auth;

/// <summary>
/// SMTP implementation of <see cref="IEmailService"/> using MailKit.
/// Supports SendGrid free-tier and Gmail SMTP via configuration.
///
/// Resilience (NFR-023, NFR-032):
///   - Polly retry policy: up to 3 attempts with exponential backoff (1s, 2s, 4s).
///   - Circuit breaker: opens after 3 consecutive failures; half-open after 60 s.
///   - When the circuit is open the exception is logged and re-thrown so the caller
///     can surface an appropriate error to the client without hiding failures.
///
/// SMTP credentials are read from configuration (never hardcoded).
/// TLS is enforced via <see cref="SecureSocketOptions.StartTls"/> or
/// <see cref="SecureSocketOptions.SslOnConnect"/> depending on the port.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly AsyncPolicy _resiliencePolicy;

    public SmtpEmailService(
        IConfiguration configuration,
        ILogger<SmtpEmailService> logger)
    {
        _logger = logger;

        _settings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>()
            ?? throw new InvalidOperationException(
                "SmtpSettings configuration section is missing. "
                + "Configure it in appsettings.json or user secrets.");

        // Circuit breaker: open after 3 consecutive exceptions; half-open after 60 s.
        var circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(60),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "SMTP circuit breaker OPEN for {Duration}s.", duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("SMTP circuit breaker RESET."));

        // Retry: 3 attempts with exponential back-off (1 s, 2 s, 4 s). Do not retry
        // when the circuit is open — BrokenCircuitException propagates immediately.
        var retry = Policy
            .Handle<Exception>(ex => ex is not BrokenCircuitException)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "SMTP send attempt {Attempt} failed; retrying in {Delay}s.",
                        attempt, delay.TotalSeconds));

        // Wrap: retry first, then circuit breaker wraps the combined policy
        _resiliencePolicy = Policy.WrapAsync(retry, circuitBreaker);
    }

    /// <inheritdoc/>
    public async Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationLink,
        CancellationToken cancellationToken = default)
    {
        var message = BuildVerificationMessage(toEmail, toName, verificationLink);

        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var client = new SmtpClient();
            // Choose SecureSocketOptions based on port (465 = SSL, anything else = STARTTLS)
            var socketOptions = _settings.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        });

        _logger.LogInformation("Verification email dispatched to {Email}.", toEmail);
    }

    /// <inheritdoc/>
    public async Task SendPasswordResetEmailAsync(
        string toEmail,
        string toName,
        string resetLink,
        CancellationToken cancellationToken = default)
    {
        var message = BuildResetMessage(toEmail, toName, resetLink);

        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var client = new SmtpClient();
            var socketOptions = _settings.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        });

        _logger.LogInformation("Password reset email dispatched to {Email}.", toEmail);
    }

    private MimeMessage BuildVerificationMessage(
        string toEmail,
        string toName,
        string verificationLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Verify your UPACIP account";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = BuildHtmlBody(toName, verificationLink),
            TextBody = BuildTextBody(toName, verificationLink),
        };

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private MimeMessage BuildResetMessage(
        string toEmail,
        string toName,
        string resetLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Reset your UPACIP password";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = BuildResetHtmlBody(toName, resetLink),
            TextBody = BuildResetTextBody(toName, resetLink),
        };

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private static string BuildHtmlBody(string name, string link) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="font-family:Arial,sans-serif;background:#F5F5F5;padding:24px">
          <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:8px;padding:32px;box-shadow:0 1px 4px rgba(0,0,0,.1)">
            <h1 style="color:#1976D2;font-size:1.5rem;margin-bottom:4px">UPACIP</h1>
            <h2 style="font-weight:400;font-size:1.25rem">Verify your email address</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>Thank you for registering. Click the button below to activate your account.
               This link expires in <strong>1 hour</strong>.</p>
            <p style="text-align:center;margin:32px 0">
              <a href="{link}" style="background:#1976D2;color:#fff;padding:12px 24px;border-radius:4px;text-decoration:none;font-weight:bold">
                Verify Email
              </a>
            </p>
            <p style="font-size:.875rem;color:#757575">
              If you did not create this account, you can safely ignore this email.
            </p>
          </div>
        </body>
        </html>
        """;

    private static string BuildTextBody(string name, string link) =>
        $"Hi {name},\n\nVerify your UPACIP account by visiting:\n{link}\n\nThis link expires in 1 hour.\n\nIf you did not create this account, ignore this email.";

    private static string BuildResetHtmlBody(string name, string link) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="font-family:Arial,sans-serif;background:#F5F5F5;padding:24px">
          <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:8px;padding:32px;box-shadow:0 1px 4px rgba(0,0,0,.1)">
            <h1 style="color:#1976D2;font-size:1.5rem;margin-bottom:4px">UPACIP</h1>
            <h2 style="font-weight:400;font-size:1.25rem">Reset your password</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>We received a request to reset your UPACIP account password.
               Click the button below to choose a new password.
               This link expires in <strong>1 hour</strong>.</p>
            <p style="text-align:center;margin:32px 0">
              <a href="{link}" style="background:#1976D2;color:#fff;padding:12px 24px;border-radius:4px;text-decoration:none;font-weight:bold">
                Reset Password
              </a>
            </p>
            <p style="font-size:.875rem;color:#757575">
              If you did not request a password reset, you can safely ignore this email.
              Your password will not be changed.
            </p>
          </div>
        </body>
        </html>
        """;

    private static string BuildResetTextBody(string name, string link) =>
        $"Hi {name},\n\nReset your UPACIP password by visiting:\n{link}\n\nThis link expires in 1 hour.\n\nIf you did not request this, ignore this email.";
}

/// <summary>Configuration record for SMTP transport settings.</summary>
public sealed record SmtpSettings
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "UPACIP";
}
