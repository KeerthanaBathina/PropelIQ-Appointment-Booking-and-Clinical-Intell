namespace UPACIP.Service.AiSafety.Models;

/// <summary>
/// Result of an AI-generated response content filtering scan
/// (US_079 task_002, AIR-S05, AC-3).
///
/// <para>
/// When <see cref="IsBlocked"/> is <see langword="true"/>, callers must use
/// <see cref="SafeResponse"/> instead of the original AI output.
/// The original harmful content is <b>never</b> returned to callers or stored;
/// only <see cref="OriginalResponseHash"/> (SHA-256) is kept for audit correlation.
/// </para>
/// </summary>
public sealed class ContentFilterResult
{
    /// <summary>
    /// <see langword="true"/> when at least one Critical-severity content filter
    /// category matched the AI-generated response.
    /// </summary>
    public bool IsBlocked { get; init; }

    /// <summary>
    /// Categories that triggered the block. Empty when <see cref="IsBlocked"/> is
    /// <see langword="false"/>.
    /// </summary>
    public IReadOnlyList<ContentFilterCategory> BlockedCategories { get; init; } =
        Array.Empty<ContentFilterCategory>();

    /// <summary>
    /// Safe replacement message shown to the user when the response is blocked.
    /// Only populated when <see cref="IsBlocked"/> is <see langword="true"/>.
    /// </summary>
    public string SafeResponse { get; init; } = string.Empty;

    /// <summary>
    /// SHA-256 hex hash of the blocked response text for audit correlation (AIR-S04).
    /// Allows audit trail matching without storing the harmful content itself.
    /// Empty when <see cref="IsBlocked"/> is <see langword="false"/>.
    /// </summary>
    public string OriginalResponseHash { get; init; } = string.Empty;
}
