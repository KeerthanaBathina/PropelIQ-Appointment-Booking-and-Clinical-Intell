using System.Threading;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Resilience;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Handles the dual-provider-failure degraded mode scenario (US_070 TASK_002, AIR-O04, EC-1).
///
/// <para>When both the primary (OpenAI) and fallback (Claude) providers are simultaneously
/// unavailable — both circuit breakers open — the AI Gateway enters <em>degraded mode</em>:
/// <list type="bullet">
///   <item>All pending and new requests receive a structured 503-equivalent
///   <see cref="AIResponse.Failed"/> response immediately without hitting any provider.</item>
///   <item>A <c>LogCritical</c> Serilog event is emitted so external monitoring
///   (Seq alerting rules, PagerDuty integration, etc.) can notify on-call admins.</item>
/// </list>
/// </para>
///
/// <para>The handler also exposes <see cref="IsBothProvidersUnavailable"/> as a fast
/// in-process check, driven by <see cref="ProviderStateManager"/>, so
/// <see cref="AIProviderFallbackHandler"/> can short-circuit to degraded mode without
/// making Redis calls for every request.</para>
///
/// Registered as Singleton — no mutable per-request state.
/// </summary>
public sealed class DegradedModeHandler
{
    private readonly ProviderStateManager  _stateManager;
    private readonly ILogger<DegradedModeHandler> _logger;

    // ── Throttle: at most one admin-alert log per 60 seconds ────────────────
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;
    private const int AlertIntervalSeconds = 60;

    public DegradedModeHandler(
        ProviderStateManager          stateManager,
        ILogger<DegradedModeHandler>  logger)
    {
        _stateManager = stateManager;
        _logger       = logger;
    }

    /// <summary>
    /// Returns <see langword="true"/> when both the primary and fallback providers are
    /// in <see cref="ProviderState.Unavailable"/> (circuit open) state.
    /// Reads from in-process <see cref="ProviderStateManager"/> — O(1), no I/O.
    /// </summary>
    /// <param name="primaryProviderName">e.g., <c>"openai"</c></param>
    /// <param name="fallbackProviderName">e.g., <c>"anthropic"</c></param>
    public bool IsBothProvidersUnavailable(
        string primaryProviderName,
        string? fallbackProviderName)
    {
        if (string.IsNullOrEmpty(fallbackProviderName))
            return false; // No fallback configured → single-provider degraded = handled by caller

        return _stateManager.GetProviderState(primaryProviderName)  == ProviderState.Unavailable
            && _stateManager.GetProviderState(fallbackProviderName) == ProviderState.Unavailable;
    }

    /// <summary>
    /// Builds a structured error <see cref="AIResponse"/> for degraded mode and
    /// fires an admin-alert critical log event (throttled to once per
    /// <c>AlertIntervalSeconds</c> to avoid log spam during sustained outages).
    /// </summary>
    /// <param name="request">The originating AI request.</param>
    /// <param name="primaryProviderName">Name of the primary provider (logging only).</param>
    /// <param name="fallbackProviderName">Name of the fallback provider, or <see langword="null"/>.</param>
    /// <param name="elapsedMs">Gateway elapsed time in milliseconds for the response envelope.</param>
    public AIResponse HandleDegradedMode(
        AIRequest request,
        string    primaryProviderName,
        string?   fallbackProviderName,
        long      elapsedMs)
    {
        MaybeEmitAdminAlert(request, primaryProviderName, fallbackProviderName);

        return AIResponse.Failed(
            request.RequestId,
            "AI service temporarily unavailable — all providers are offline. " +
            "Please try again later or use the manual workflow.",
            elapsedMs);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void MaybeEmitAdminAlert(
        AIRequest request,
        string    primaryProviderName,
        string?   fallbackProviderName)
    {
        var now = DateTimeOffset.UtcNow;

        // Throttle: suppress repeated alerts during a sustained dual outage.
        if ((now - _lastAlertAt).TotalSeconds < AlertIntervalSeconds)
            return;

        _lastAlertAt = now;

        // CRITICAL structured log — picked up by Seq alerting / PagerDuty integration.
        // No PII: only provider names, states, and correlation IDs are included.
        _logger.LogCritical(
            "AI GATEWAY DEGRADED MODE: both providers unavailable — admin action required. " +
            "PrimaryProvider={PrimaryProvider} PrimaryState={PrimaryState} " +
            "FallbackProvider={FallbackProvider} FallbackState={FallbackState} " +
            "CorrelationId={CorrelationId} OccurredAt={OccurredAt}",
            primaryProviderName,
            _stateManager.GetProviderState(primaryProviderName),
            fallbackProviderName ?? "none",
            fallbackProviderName is not null
                ? _stateManager.GetProviderState(fallbackProviderName).ToString()
                : "N/A",
            request.CorrelationId,
            now);
    }
}
