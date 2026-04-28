namespace UPACIP.Service.Import.Models;

/// <summary>
/// Real-time progress snapshot for a background CSV import job
/// (US_092 task_002, edge case 1 — 100K+ row files).
/// </summary>
public sealed record ImportProgress
{
    /// <summary>1-based index of the batch currently being persisted.</summary>
    public int CurrentBatch { get; init; }

    /// <summary>
    /// Estimated total number of batches, computed from file size heuristic
    /// (average 200 bytes / row ÷ BatchSize).
    /// </summary>
    public int TotalEstimatedBatches { get; init; }

    /// <summary>Total rows processed (validated + skipped) so far.</summary>
    public int ProcessedRows { get; init; }

    /// <summary>Rows successfully persisted to the database so far.</summary>
    public int SuccessSoFar { get; init; }

    /// <summary>Rows that failed validation so far.</summary>
    public int ErrorsSoFar { get; init; }

    /// <summary>Rows skipped due to duplicate detection so far.</summary>
    public int DuplicatesSoFar { get; init; }

    /// <summary>0–100 estimated completion percentage.</summary>
    public double PercentComplete { get; init; }

    /// <summary>
    /// Estimated time remaining computed from the average batch processing time
    /// multiplied by remaining batches. Null until at least one batch has completed.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}
