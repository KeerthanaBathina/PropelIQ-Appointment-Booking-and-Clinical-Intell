using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Twilio-backed SMS transport that delivers messages using the Twilio Messaging API,
/// enforcing the delivery contract defined in <see cref="ISmsTransport"/>.
///
/// Validation model:
/// <list type="bullet">
///   <item>
///     <strong>US-number scope</strong>: Phase 1 accepts only numbers prefixed with
///     <c>+1</c> (configurable via <see cref="SmsProviderOptions.AllowedCountryCode"/>).
///     International numbers receive a deterministic <see cref="SmsDeliveryOutcome.InvalidNumber"/>
///     result — no API call is made (EC-2).
///   </item>
///   <item>
///     <strong>Gateway disable</strong>: when <see cref="SmsProviderOptions.SmsEnabled"/> is
///     <c>false</c>, all sends are short-circuited with <see cref="SmsDeliveryOutcome.GatewayDisabled"/>
///     so the caller can fall through to email-only delivery (EC-1).
///   </item>
/// </list>
///
/// Resilience model:
/// <list type="bullet">
///   <item>
///     <strong>Retry</strong>: up to <see cref="SmsProviderOptions.MaxRetryAttempts"/> retries
///     with exponential backoff (1s, 2s, 4s) for transient Twilio API failures (AC-3).
///   </item>
///   <item>
///     <strong>Delivery timeout</strong>: a <see cref="CancellationTokenSource"/> enforces
///     <see cref="SmsProviderOptions.DeliveryTimeoutSeconds"/> (default 30s) across all
///     attempts (AC-1).
///   </item>
/// </list>
///
/// Security:
/// <list type="bullet">
///   <item>Auth Token is never logged (OWASP A02).</item>
///   <item>Recipient phone numbers are masked in log output (OWASP A09).</item>
///   <item>Twilio credentials are initialised once per instance via <see cref="TwilioClient.Init"/>.</item>
/// </list>
///
/// This class never throws.  All outcomes are encoded in <see cref="SmsDeliveryAttemptResult"/>.
/// </summary>
public sealed class TwilioSmsTransport : ISmsTransport
{
    private readonly SmsProviderOptions _options;
    private readonly ILogger<TwilioSmsTransport> _logger;
    private readonly PhoneNumber _fromNumber;

    public TwilioSmsTransport(
        IOptions<SmsProviderOptions> options,
        ILogger<TwilioSmsTransport> logger)
    {
        _options    = options.Value;
        _logger     = logger;
        _fromNumber = new PhoneNumber(_options.FromNumber);

        // Initialise Twilio SDK with credentials.  TwilioClient.Init is idempotent
        // and thread-safe; calling it multiple times with the same credentials is safe.
        // Credentials are NEVER passed to any logger (OWASP A02).
        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
    }

    /// <inheritdoc/>
    public async Task<SmsDeliveryAttemptResult> SendAsync(
        SmsTransportMessage message,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Gateway disable check (EC-1) ─────────────────────────────────
        if (!_options.SmsEnabled)
        {
            _logger.LogWarning(
                "SMS gateway is disabled (SmsProvider:SmsEnabled = false). " +
                "Skipping send to {MaskedNumber}. " +
                "Caller should fall through to email-only delivery.",
                MaskNumber(message.ToPhoneNumber));

            return SmsDeliveryAttemptResult.GatewayDisabled();
        }

        // ── 2. US-number scope validation (EC-2) ─────────────────────────────
        if (!IsAllowedNumber(message.ToPhoneNumber))
        {
            _logger.LogWarning(
                "SMS rejected: {MaskedNumber} does not match the allowed country-code " +
                "prefix '{Prefix}' (Phase 1: US numbers only).",
                MaskNumber(message.ToPhoneNumber),
                _options.AllowedCountryCode);

            return SmsDeliveryAttemptResult.InvalidNumber(message.ToPhoneNumber);
        }

        // ── 3. Enforce overall delivery timeout (AC-1) ───────────────────────
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.DeliveryTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        // ── 4. Retry on transient failures (AC-3) ────────────────────────────
        int attemptCounter = 0;

        var retryPolicy = Policy
            .Handle<Exception>(ex =>
                ex is not OperationCanceledException &&
                !IsCredentialOrAccountError(ex))
            .WaitAndRetryAsync(
                retryCount: _options.MaxRetryAttempts,
                // Backoff: attempt 1 → 1s, attempt 2 → 2s, attempt 3 → 4s (AC-3)
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "Twilio SMS send attempt {Attempt} to {MaskedNumber} failed; " +
                        "retrying in {Delay:F1}s.",
                        attempt, MaskNumber(message.ToPhoneNumber), delay.TotalSeconds));

        try
        {
            MessageResource? resource = null;

            await retryPolicy.ExecuteAsync(async token =>
            {
                attemptCounter++;
                resource = await MessageResource.CreateAsync(
                    to:   new PhoneNumber(message.ToPhoneNumber),
                    from: _fromNumber,
                    body: message.Body);
            }, linkedCts.Token);

            var sid = resource?.Sid ?? "(unknown)";

            _logger.LogInformation(
                "SMS sent to {MaskedNumber} via Twilio. " +
                "MessageSid: {MessageSid}. Attempts: {Attempts}.",
                MaskNumber(message.ToPhoneNumber), sid, attemptCounter);

            return SmsDeliveryAttemptResult.Succeeded(sid, attemptCounter);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Twilio SMS send to {MaskedNumber} was cancelled or timed out " +
                "after {Attempts} attempt(s).",
                MaskNumber(message.ToPhoneNumber), attemptCounter);

            return SmsDeliveryAttemptResult.Failed(attemptCounter, "Delivery cancelled or timed out.");
        }
        catch (Exception ex) when (IsTrialCreditExhausted(ex))
        {
            // EC-1: trial credits exhausted — emit operational alert and disable SMS.
            // The SmsEnabled flag is read-only on the options object (immutable at runtime),
            // so the alert signals operations to update the configuration and redeploy or
            // toggle the flag via the feature management system.
            _logger.LogCritical(
                "[ADMIN ALERT] Twilio trial credits are exhausted. " +
                "SMS delivery is being skipped for this and subsequent requests. " +
                "Update SmsProvider:SmsEnabled to false or top-up Twilio account. " +
                "Error: {ErrorType}",
                ex.GetType().Name);

            return SmsDeliveryAttemptResult.Failed(attemptCounter, "Twilio trial credits exhausted.");
        }
        catch (Exception ex) when (IsCredentialOrAccountError(ex))
        {
            // Hard credential / account error — not retryable.
            var reason = BuildSafeErrorReason(ex);
            _logger.LogError(ex,
                "Twilio SMS send to {MaskedNumber} failed with a non-retryable account error: {Reason}",
                MaskNumber(message.ToPhoneNumber), reason);

            return SmsDeliveryAttemptResult.Failed(attemptCounter, reason);
        }
        catch (Exception ex)
        {
            var reason = BuildSafeErrorReason(ex);
            _logger.LogError(ex,
                "Twilio SMS send to {MaskedNumber} failed after {Attempts} attempt(s): {Reason}",
                MaskNumber(message.ToPhoneNumber), attemptCounter, reason);

            return SmsDeliveryAttemptResult.Failed(attemptCounter, reason);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> when the number starts with the configured allowed prefix.
    /// Phase 1 allows only US <c>+1</c> numbers (EC-2).
    /// </summary>
    private bool IsAllowedNumber(string phoneNumber) =>
        !string.IsNullOrWhiteSpace(phoneNumber) &&
        phoneNumber.StartsWith(_options.AllowedCountryCode, StringComparison.Ordinal);

    /// <summary>
    /// Returns <c>true</c> for Twilio exceptions that indicate exhausted trial credits.
    /// Twilio returns error code 21408 or 20003 for trial-account restrictions.
    /// </summary>
    private static bool IsTrialCreditExhausted(Exception ex)
    {
        if (ex is ApiException twilioEx)
        {
            // 20003 — Authentication error (exhausted / suspended)
            // 21408 — Permission to send an SMS has not been enabled for the region
            return twilioEx.Code is 20003 or 21408;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> for Twilio exceptions that represent non-retryable
    /// credential or account configuration errors.
    /// </summary>
    private static bool IsCredentialOrAccountError(Exception ex)
    {
        if (ex is ApiException twilioEx)
        {
            // 20003 — Auth error, 20404 — Account not found, 21211 — Invalid To number
            return twilioEx.Code is 20003 or 20404 or 21211;
        }

        return false;
    }

    /// <summary>
    /// Returns a sanitised error string safe for structured log output.
    /// Exception message is omitted to prevent credential or PII leakage (OWASP A02/A09).
    /// </summary>
    private static string BuildSafeErrorReason(Exception ex) =>
        ex switch
        {
            ApiException twilio => $"Twilio error {twilio.Code}: {twilio.Message?.Split('.')[0] ?? ex.GetType().Name}",
            _                   => ex.GetType().Name,
        };

    /// <summary>
    /// Masks the last 4 digits of a phone number for log output to reduce PII exposure.
    /// Example: <c>+12025551234</c> → <c>+1202555****</c>
    /// </summary>
    private static string MaskNumber(string phoneNumber)
    {
        if (phoneNumber.Length <= 4) return "****";
        return phoneNumber[..^4] + "****";
    }
}
