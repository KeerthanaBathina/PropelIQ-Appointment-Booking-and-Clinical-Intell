using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using UPACIP.Service.Resilience;

namespace UPACIP.Service.Auth;

/// <summary>
/// Polly V8 circuit-breaker decorator for <see cref="SmtpEmailService"/>
/// (US_084 task_001, AC-3).
///
/// <para>
/// Wraps every <see cref="IEmailService"/> method with the <c>Email</c> resilience
/// pipeline from <see cref="IExternalServiceResilienceProvider"/>.  When the circuit
/// is open (i.e. the SMTP provider is unavailable), the call parameters are serialised
/// to JSON and pushed to Redis list <c>notification:email:retry_queue</c> so they can
/// be retried later by <see cref="UPACIP.Service.Notifications.NotificationRetryService"/>.
/// </para>
///
/// <para>
/// This class is registered as the primary <c>IEmailService</c> in DI.
/// <see cref="SmtpEmailService"/> is also registered as its concrete type so
/// that <see cref="UPACIP.Service.Notifications.NotificationRetryService"/> can inject
/// it directly (avoiding re-queuing loops when retrying).
/// </para>
/// </summary>
public sealed class ResilientEmailService : IEmailService
{
    private const string RetryQueue = "notification:email:retry_queue";

    private readonly SmtpEmailService                   _inner;
    private readonly IExternalServiceResilienceProvider _resilienceProvider;
    private readonly IConnectionMultiplexer              _redis;
    private readonly ILogger<ResilientEmailService>      _logger;

    public ResilientEmailService(
        SmtpEmailService                    inner,
        IExternalServiceResilienceProvider  resilienceProvider,
        IConnectionMultiplexer              connectionMultiplexer,
        ILogger<ResilientEmailService>      logger)
    {
        _inner              = inner;
        _resilienceProvider = resilienceProvider;
        _redis              = connectionMultiplexer;
        _logger             = logger;
    }

    // ── IEmailService ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task SendVerificationEmailAsync(
        string            toEmail,
        string            toName,
        string            verificationLink,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Email");

        try
        {
            await pipeline.ExecuteAsync(
                static async (state, ct) =>
                    await state.inner.SendVerificationEmailAsync(
                        state.toEmail, state.toName, state.verificationLink, ct),
                (inner: _inner, toEmail, toName, verificationLink),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "Email circuit OPEN — queuing verification email for {Recipient}.",
                MaskEmail(toEmail));

            await EnqueueAsync(new NotificationQueueItem(
                Type: "Verification",
                Recipient: toEmail,
                RecipientName: toName,
                Param1: verificationLink),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendPasswordResetEmailAsync(
        string            toEmail,
        string            toName,
        string            resetLink,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Email");

        try
        {
            await pipeline.ExecuteAsync(
                static async (state, ct) =>
                    await state.inner.SendPasswordResetEmailAsync(
                        state.toEmail, state.toName, state.resetLink, ct),
                (inner: _inner, toEmail, toName, resetLink),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "Email circuit OPEN — queuing password reset email for {Recipient}.",
                MaskEmail(toEmail));

            await EnqueueAsync(new NotificationQueueItem(
                Type: "PasswordReset",
                Recipient: toEmail,
                RecipientName: toName,
                Param1: resetLink),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendWaitlistOfferEmailAsync(
        string            toEmail,
        string            toName,
        string            claimLink,
        string            appointmentDetails,
        bool              isWithin24Hours,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Email");

        try
        {
            await pipeline.ExecuteAsync(
                static async (state, ct) =>
                    await state.inner.SendWaitlistOfferEmailAsync(
                        state.toEmail, state.toName, state.claimLink,
                        state.appointmentDetails, state.isWithin24Hours, ct),
                (inner: _inner, toEmail, toName, claimLink, appointmentDetails, isWithin24Hours),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "Email circuit OPEN — queuing waitlist offer email for {Recipient}.",
                MaskEmail(toEmail));

            await EnqueueAsync(new NotificationQueueItem(
                Type: "WaitlistOffer",
                Recipient: toEmail,
                RecipientName: toName,
                Param1: claimLink,
                Param2: appointmentDetails,
                BoolParam: isWithin24Hours),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendSwapCompletedEmailAsync(
        string            toEmail,
        string            toName,
        string            oldAppointmentTime,
        string            newAppointmentTime,
        string            providerName,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Email");

        try
        {
            await pipeline.ExecuteAsync(
                static async (state, ct) =>
                    await state.inner.SendSwapCompletedEmailAsync(
                        state.toEmail, state.toName, state.oldAppointmentTime,
                        state.newAppointmentTime, state.providerName, ct),
                (inner: _inner, toEmail, toName, oldAppointmentTime, newAppointmentTime, providerName),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "Email circuit OPEN — queuing swap-completed email for {Recipient}.",
                MaskEmail(toEmail));

            await EnqueueAsync(new NotificationQueueItem(
                Type: "SwapCompleted",
                Recipient: toEmail,
                RecipientName: toName,
                Param1: oldAppointmentTime,
                Param2: newAppointmentTime,
                Param3: providerName),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendManualSwapConfirmationEmailAsync(
        string            toEmail,
        string            toName,
        string            preferredSlotTime,
        string            providerName,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Email");

        try
        {
            await pipeline.ExecuteAsync(
                static async (state, ct) =>
                    await state.inner.SendManualSwapConfirmationEmailAsync(
                        state.toEmail, state.toName, state.preferredSlotTime, state.providerName, ct),
                (inner: _inner, toEmail, toName, preferredSlotTime, providerName),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "Email circuit OPEN — queuing manual-swap confirmation email for {Recipient}.",
                MaskEmail(toEmail));

            await EnqueueAsync(new NotificationQueueItem(
                Type: "ManualSwapConfirmation",
                Recipient: toEmail,
                RecipientName: toName,
                Param1: preferredSlotTime,
                Param2: providerName),
                cancellationToken);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task EnqueueAsync(NotificationQueueItem item, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(item);
            var db   = _redis.GetDatabase();
            await db.ListRightPushAsync(RetryQueue, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enqueue email retry item (type={Type}) for {Recipient}. " +
                "Notification will be lost.",
                item.Type,
                MaskEmail(item.Recipient));
        }
    }

    private static string MaskEmail(string email)
    {
        var idx = email.IndexOf('@', StringComparison.Ordinal);
        return idx > 1 ? $"{email[0]}***{email[idx..]}" : "***@***";
    }
}

/// <summary>
/// JSON-serialisable envelope for an email notification queued for retry
/// (US_084 task_001, AC-3).
/// </summary>
public sealed record NotificationQueueItem(
    string   Type,
    string   Recipient,
    string   RecipientName,
    string?  Param1      = null,
    string?  Param2      = null,
    string?  Param3      = null,
    bool?    BoolParam   = null,
    int      RetryCount  = 0,
    DateTime QueuedAt    = default)
{
    /// <summary>UTC time the item was queued.  Defaults to <see cref="DateTime.UtcNow"/> when 0.</summary>
    public DateTime QueuedAt { get; init; } = QueuedAt == default ? DateTime.UtcNow : QueuedAt;
}
