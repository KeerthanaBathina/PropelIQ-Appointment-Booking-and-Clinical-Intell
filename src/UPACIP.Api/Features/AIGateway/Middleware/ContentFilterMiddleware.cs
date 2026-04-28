using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Service.AiSafety;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Singleton AI Gateway middleware component that scans AI-generated responses for
/// harmful, discriminatory, or medically dangerous content and replaces blocked
/// responses with a safe fallback message (US_079 task_002, AIR-S05, AC-3).
///
/// <para>Pipeline position (after LLM response is received):</para>
/// <list type="number">
///   <item>Auth check</item>
///   <item>Validation</item>
///   <item>Token budget enforcement</item>
///   <item>Prompt injection sanitization</item>
///   <item>PII redaction</item>
///   <item>Queue routing / provider dispatch</item>
///   <item>Response normalization</item>
///   <item><b>Content filtering ← this middleware</b></item>
///   <item>Cost tracking</item>
/// </list>
///
/// <para>
/// When a response is blocked, the middleware replaces
/// <see cref="AIResponse.Content"/> with the safe fallback message and marks
/// the response metadata so callers can surface the filtering event to the UI.
/// The original blocked content is never forwarded; its SHA-256 hash is retained
/// for audit correlation (AIR-S04).
/// </para>
///
/// <para>
/// Singleton lifetime: <see cref="IContentFilterService"/> is also Singleton
/// (stateless apart from cached compiled regexes).
/// </para>
/// </summary>
public sealed class ContentFilterMiddleware
{
    private const string FilteredHeaderKey = "X-Content-Filtered";

    private readonly IContentFilterService              _filterService;
    private readonly ILogger<ContentFilterMiddleware>   _logger;

    public ContentFilterMiddleware(
        IContentFilterService             filterService,
        ILogger<ContentFilterMiddleware>  logger)
    {
        _filterService = filterService;
        _logger        = logger;
    }

    /// <summary>
    /// Scans <paramref name="response"/> for harmful content and returns either the
    /// original response (when clean) or a safe replacement (when blocked).
    /// </summary>
    /// <param name="response">Normalized AI Gateway response to filter.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// Tuple of the (possibly replaced) response and a boolean indicating whether
    /// the response was blocked by the content filter.
    /// </returns>
    public async Task<(AIResponse filteredResponse, bool wasBlocked)> FilterAsync(
        AIResponse        response,
        CancellationToken cancellationToken = default)
    {
        if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            return (response, false);

        var result = await _filterService.FilterResponseAsync(
            response.Content,
            response.RequestId.ToString(),
            cancellationToken);

        if (!result.IsBlocked)
            return (response, false);

        var categories = string.Join(", ", result.BlockedCategories);

        _logger.LogWarning(
            "ContentFilterMiddleware: response blocked. " +
            "RequestId={RequestId} Categories={Categories} ResponseHash={Hash} " +
            "Action=Blocked",
            response.RequestId,
            categories,
            result.OriginalResponseHash);

        // Replace harmful content with the safe fallback; preserve all other fields.
        var filtered = new AIResponse
        {
            RequestId        = response.RequestId,
            Content          = result.SafeResponse,
            ProviderName     = response.ProviderName,
            ModelVersion     = response.ModelVersion,
            InputTokensUsed  = response.InputTokensUsed,
            OutputTokensUsed = response.OutputTokensUsed,
            ConfidenceScore  = response.ConfidenceScore,
            LatencyMs        = response.LatencyMs,
            Success          = true,   // Safe response is a successful (filtered) response.
            ErrorMessage     = null,
            IsQueued         = response.IsQueued,
            JobId            = response.JobId,
            Truncated        = response.Truncated,
            TruncationInfo   = response.TruncationInfo,
        };

        return (filtered, true);
    }
}
