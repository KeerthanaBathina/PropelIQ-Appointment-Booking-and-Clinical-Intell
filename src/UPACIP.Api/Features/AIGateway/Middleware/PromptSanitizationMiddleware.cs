using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Service.AiSafety;
using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Singleton AI Gateway middleware component that intercepts user-supplied prompt
/// text before it reaches downstream prompt assembly, detects prompt injection
/// patterns, and either blocks or sanitises the request (US_079 task_001, AIR-S06,
/// AIR-S04).
///
/// <para>Pipeline position (inside <c>AIGatewayService.ProcessAsync</c>):</para>
/// <list type="number">
///   <item>Auth check</item>
///   <item>Validation</item>
///   <item>Token budget enforcement</item>
///   <item><b>Prompt injection sanitization ← this middleware</b></item>
///   <item>PII redaction</item>
///   <item>Queue routing / provider dispatch</item>
///   <item>Response normalization</item>
///   <item>Cost tracking</item>
/// </list>
///
/// <para>Decision logic (AIR-S06):</para>
/// <list type="bullet">
///   <item>
///     <b>Block</b> — <see cref="InjectionDetectionResult.RiskScore"/> ≥ 0.8
///     (High or Critical severity): request is rejected with a structured
///     <c>PROMPT_INJECTION_DETECTED</c> error and a Warning-level audit event
///     is emitted via Serilog.
///   </item>
///   <item>
///     <b>Sanitize &amp; proceed</b> — RiskScore &lt; 0.8 (Low or Medium severity):
///     the sanitised prompt replaces the original and the pipeline continues.
///   </item>
///   <item>
///     <b>Pass-through</b> — no injection detected: request is forwarded unchanged.
///   </item>
/// </list>
///
/// <para>
/// Only the user-supplied <see cref="AIRequest.Prompt"/> is scanned.
/// <see cref="AIRequest.SystemMessage"/> is trusted server-authored content and
/// is not subject to injection scanning.
/// </para>
///
/// <para>
/// Singleton lifetime: <see cref="IPromptInjectionDetector"/> is also Singleton
/// (stateless apart from cached compiled regexes), so no scope management is required.
/// </para>
/// </summary>
public sealed class PromptSanitizationMiddleware
{
    private const float BlockRiskScoreThreshold = 0.8f;

    private readonly IPromptInjectionDetector              _detector;
    private readonly ILogger<PromptSanitizationMiddleware> _logger;

    public PromptSanitizationMiddleware(
        IPromptInjectionDetector              detector,
        ILogger<PromptSanitizationMiddleware> logger)
    {
        _detector = detector;
        _logger   = logger;
    }

    /// <summary>
    /// Scans the user-input portion of <paramref name="request"/> for prompt injection
    /// patterns and returns the (possibly sanitised) request, detection result, and a
    /// block flag.
    ///
    /// <para>
    /// Callers <b>must not</b> dispatch the request to any AI provider when
    /// <c>isBlocked = true</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Incoming AI gateway request.</param>
    /// <param name="userId">Authenticated caller's user ID for audit logging (AIR-S04).</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// Tuple of:
    /// <list type="bullet">
    ///   <item><c>sanitisedRequest</c> — request with injection patterns removed from
    ///   <see cref="AIRequest.Prompt"/> when applicable.</item>
    ///   <item><c>result</c> — full <see cref="InjectionDetectionResult"/> for audit purposes.</item>
    ///   <item><c>isBlocked</c> — <c>true</c> when the risk score meets the block threshold.</item>
    /// </list>
    /// </returns>
    public async Task<(AIRequest sanitisedRequest, InjectionDetectionResult result, bool isBlocked)>
        SanitizeRequestAsync(
            AIRequest         request,
            string            userId,
            CancellationToken cancellationToken = default)
    {
        var result = await _detector.SanitizeAsync(
            request.Prompt, userId, cancellationToken);

        // No injection detected — pass through unchanged.
        if (!result.IsInjectionDetected)
            return (request, result, false);

        // High / Critical risk — block entirely.
        if (result.RiskScore >= BlockRiskScoreThreshold)
        {
            var categories = string.Join(", ",
                result.DetectedPatterns.Select(p => p.Category).Distinct());

            _logger.LogWarning(
                "PromptSanitizationMiddleware: request blocked. " +
                "UserId={UserId} CorrelationId={CorrelationId} " +
                "InjectionCategory={Category} RiskScore={RiskScore} " +
                "Action=Blocked",
                userId,
                request.CorrelationId,
                categories,
                result.RiskScore);

            return (request, result, true);
        }

        // Low / Medium risk — sanitise and proceed.
        var categories2 = string.Join(", ",
            result.DetectedPatterns.Select(p => p.Category).Distinct());

        _logger.LogWarning(
            "PromptSanitizationMiddleware: prompt sanitised and forwarded. " +
            "UserId={UserId} CorrelationId={CorrelationId} " +
            "InjectionCategory={Category} RiskScore={RiskScore} " +
            "Action=Sanitized",
            userId,
            request.CorrelationId,
            categories2,
            result.RiskScore);

        // Replace the prompt with the sanitised text; all other fields are unchanged.
        var sanitisedRequest = new AIRequest
        {
            RequestId       = request.RequestId,
            RequestType     = request.RequestType,
            Prompt          = result.SanitizedText,
            SystemMessage   = request.SystemMessage,
            MaxInputTokens  = request.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens,
            Temperature     = request.Temperature,
            Metadata        = request.Metadata,
            CorrelationId   = request.CorrelationId,
        };

        return (sanitisedRequest, result, false);
    }
}
