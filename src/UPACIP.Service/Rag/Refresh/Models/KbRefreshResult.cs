namespace UPACIP.Service.Rag.Refresh.Models;

/// <summary>Lifecycle state of a knowledge-base refresh operation (US_078 AC-3).</summary>
public enum RefreshStatus
{
    /// <summary>Refresh is currently executing.</summary>
    InProgress,

    /// <summary>Refresh completed successfully; live table was swapped.</summary>
    Completed,

    /// <summary>
    /// Refresh failed at some step; the live table was NOT modified.
    /// See <see cref="KbRefreshResult.ErrorMessage"/> for details.
    /// </summary>
    Failed,
}

/// <summary>
/// Outcome returned by <see cref="IKnowledgeBaseRefreshService.RefreshAsync"/> and
/// <see cref="IKnowledgeBaseRefreshService.GetRefreshStatusAsync"/>.
/// </summary>
public sealed record KbRefreshResult
{
    /// <summary>Number of codes that did not exist in the live table and were added.</summary>
    public int NewCodesAdded { get; init; }

    /// <summary>
    /// Number of codes that existed but whose description changed; re-embedded.
    /// </summary>
    public int CodesUpdated { get; init; }

    /// <summary>
    /// Number of codes that were absent from the request (or <c>IsDeprecated = true</c>)
    /// and received a <c>deprecated_at</c> timestamp in the live table.
    /// </summary>
    public int CodesDeprecated { get; init; }

    /// <summary>Total entries evaluated during the diff pass.</summary>
    public int TotalProcessed { get; init; }

    /// <summary>Wall-clock duration from pipeline start to atomic swap (or failure).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Terminal lifecycle state of this refresh run.</summary>
    public RefreshStatus Status { get; init; }

    /// <summary>
    /// Human-readable error description when <see cref="Status"/> is
    /// <see cref="RefreshStatus.Failed"/>; <c>null</c> on success.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
