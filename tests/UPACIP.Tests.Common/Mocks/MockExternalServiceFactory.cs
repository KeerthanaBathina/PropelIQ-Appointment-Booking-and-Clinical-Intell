using Microsoft.Extensions.Caching.Distributed;
using Moq;
using UPACIP.Service.Auth;
using UPACIP.Service.Notifications;

namespace UPACIP.Tests.Common.Mocks;

/// <summary>
/// Factory for external-service mock instances covering SMS, email, and Redis cache
/// (US_097, AC-1, edge case 1).
///
/// Unit tests never call real Twilio, SMTP, or Redis endpoints — these factories
/// provide controlled in-memory substitutes. Each method returns a <see cref="Mock{T}"/>
/// so callers can add per-test <c>Setup</c> or <c>Verify</c> calls on top of the defaults.
///
/// Variants:
/// <list type="bullet">
///   <item><see cref="CreateEmailService"/> — email always succeeds (no-op async).</item>
///   <item><see cref="CreateEmailServiceFailure"/> — email throws <see cref="InvalidOperationException"/>.</item>
///   <item><see cref="CreateSmsTransport"/> — SMS succeeds with <c>Delivered</c> outcome.</item>
///   <item><see cref="CreateRedisCache"/> — in-memory dictionary backing a <see cref="IDistributedCache"/> stub.</item>
/// </list>
/// </summary>
public static class MockExternalServiceFactory
{
    // ── Email ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an <see cref="IEmailService"/> mock where all send methods complete without error.
    /// </summary>
    public static Mock<IEmailService> CreateEmailService()
    {
        var mock = new Mock<IEmailService>();

        mock.Setup(x => x.SendVerificationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.SendPasswordResetEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    /// <summary>
    /// Returns an <see cref="IEmailService"/> mock where all send methods throw
    /// <see cref="InvalidOperationException"/>. Use to test fallback / retry paths.
    /// </summary>
    public static Mock<IEmailService> CreateEmailServiceFailure(
        string message = "SMTP server unavailable")
    {
        var mock = new Mock<IEmailService>();

        mock.Setup(x => x.SendVerificationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

        mock.Setup(x => x.SendPasswordResetEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

        return mock;
    }

    // ── SMS ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an <see cref="ISmsTransport"/> mock where all sends return a
    /// <c>Sent</c> outcome. Use for happy-path appointment confirmation tests.
    /// </summary>
    public static Mock<ISmsTransport> CreateSmsTransport()
    {
        var mock = new Mock<ISmsTransport>();

        mock.Setup(x => x.SendAsync(
                It.IsAny<SmsTransportMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SmsDeliveryAttemptResult.Succeeded("SM_MOCK_SID_000", attemptsMade: 1));

        return mock;
    }

    // ── Redis / IDistributedCache ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an <see cref="IDistributedCache"/> mock backed by an in-memory
    /// <see cref="Dictionary{TKey,TValue}"/>. Supports <c>GetAsync</c>, <c>SetAsync</c>,
    /// and <c>RemoveAsync</c> operations.
    /// </summary>
    public static Mock<IDistributedCache> CreateRedisCache()
    {
        var mock  = new Mock<IDistributedCache>();
        var store = new Dictionary<string, byte[]>();

        mock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                store.TryGetValue(key, out var val) ? val : null);

        mock.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((string key, byte[] val, DistributedCacheEntryOptions _, CancellationToken _) =>
                store[key] = val)
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string key, CancellationToken _) => store.Remove(key))
            .Returns(Task.CompletedTask);

        return mock;
    }
}
