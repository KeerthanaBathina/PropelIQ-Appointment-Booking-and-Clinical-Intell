using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Post-inference content filter that scans AI-generated responses for harmful,
/// discriminatory, or medically dangerous content (US_079 task_002, AIR-S05, AC-3).
///
/// <para>
/// When a blocked category is detected the service returns a safe fallback message
/// and a SHA-256 hash of the blocked content for audit correlation. The original
/// harmful content is <b>never</b> logged or stored.
/// </para>
///
/// <para>
/// Filter rules are loaded from <c>config/content-filter-rules.json</c> via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> with
/// hot-reload support.
/// </para>
/// </summary>
public interface IContentFilterService
{
    /// <summary>
    /// Scans <paramref name="responseText"/> against all configured content filter
    /// rules and returns a <see cref="ContentFilterResult"/>.
    ///
    /// <para>
    /// When the result's <see cref="ContentFilterResult.IsBlocked"/> is
    /// <see langword="true"/>, callers must use
    /// <see cref="ContentFilterResult.SafeResponse"/> instead of
    /// <paramref name="responseText"/>.
    /// </para>
    /// </summary>
    /// <param name="responseText">AI-generated response text to scan.</param>
    /// <param name="correlationId">Request correlation ID for audit logging (AIR-S04).</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    Task<ContentFilterResult> FilterResponseAsync(
        string            responseText,
        string            correlationId,
        CancellationToken cancellationToken = default);
}
