namespace UPACIP.Service.Retention.Models;

/// <summary>
/// Structured result of a single retention purge operation (US_086 AC-4).
/// Emitted as a Serilog structured log event and returned from
/// <c>DataRetentionService</c> for observability.
/// </summary>
public sealed record PurgeResult
{
    /// <summary>Data category that was purged.</summary>
    public RetentionCategory Category { get; init; }

    /// <summary>Total number of records deleted in this purge run.</summary>
    public int RecordsPurged { get; init; }

    /// <summary>
    /// UTC creation timestamp of the oldest record that was purged.
    /// <c>null</c> when <see cref="RecordsPurged"/> is zero.
    /// </summary>
    public DateTime? OldestPurgedDate { get; init; }

    /// <summary>
    /// UTC creation timestamp of the most recent record that was purged.
    /// <c>null</c> when <see cref="RecordsPurged"/> is zero.
    /// </summary>
    public DateTime? NewestPurgedDate { get; init; }

    /// <summary>Wall-clock time taken to complete the purge run.</summary>
    public TimeSpan ExecutionDuration { get; init; }

    /// <summary>
    /// Per-notification-type breakdown of purged records.
    /// Keys are the string representation of <see cref="DataAccess.Enums.NotificationType"/>
    /// (e.g. "Confirmation", "Reminder24h", "SlotSwapCompleted").
    /// Populated only for <see cref="RetentionCategory.Notifications"/>;
    /// empty dictionary for other categories.
    /// </summary>
    public IReadOnlyDictionary<string, int> BreakdownByType { get; init; } =
        new Dictionary<string, int>();
}
