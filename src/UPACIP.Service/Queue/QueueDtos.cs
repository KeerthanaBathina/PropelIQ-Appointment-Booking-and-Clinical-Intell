using System.ComponentModel.DataAnnotations;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Queue;

// ─── Response DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// Single queue entry returned in GET /queue/today and mutation responses (US_052, US_053).
/// Statuses are serialized as camelCase strings matching the frontend contract.
/// </summary>
public sealed class QueueEntryDto
{
    public Guid   QueueId           { get; init; }
    public Guid   AppointmentId     { get; init; }
    public string PatientName       { get; init; } = string.Empty;
    /// <summary>ISO-8601 UTC appointment time.</summary>
    public DateTime AppointmentTime  { get; init; }
    /// <summary>ISO-8601 UTC arrival timestamp; null when patient has not arrived.</summary>
    public DateTime? ArrivalTimestamp { get; init; }
    /// <summary>Server-computed wait time in whole minutes from ArrivalTimestamp to now.</summary>
    public int   WaitTimeMinutes    { get; init; }
    public string Priority          { get; init; } = "normal";
    public string Status            { get; init; } = "waiting";
    public string AppointmentStatus { get; init; } = "scheduled";
    /// <summary>1-based queue position after priority + time sort.</summary>
    public int QueuePosition        { get; init; }
    /// <summary>Denormalised provider display name (US_053, AC-3). Null for walk-ins.</summary>
    public string? ProviderName     { get; init; }
    /// <summary>Appointment type label, e.g. "Checkup", "Follow-up" (US_053, AC-3).</summary>
    public string? AppointmentType  { get; init; }
    // ── US_055 — Auto no-show detection fields ────────────────────────────────
    /// <summary>True when the no-show was set automatically by the background detection service (US_055 AC-4).</summary>
    public bool IsAutoNoShow        { get; init; }
    /// <summary>True when auto no-show was detected during outage recovery (delayed detection edge case).</summary>
    public bool IsDelayedDetection  { get; init; }
    /// <summary>True when the wait time meets or exceeds the configured threshold (US_055 AC-2).</summary>
    public bool ExceedsWaitThreshold { get; init; }
    /// <summary>"none" | "warning" (>= threshold) | "critical" (>= threshold * 1.5) (US_055 AC-2).</summary>
    public string WaitAlertLevel    { get; init; } = "none";
    /// <summary>Current configured wait alert threshold in minutes — for frontend reference (US_055 AC-3).</summary>
    public int WaitThresholdMinutes { get; init; }
}

/// <summary>Envelope for GET /queue/today (AC-4, UXR-103).</summary>
public sealed class QueueTodayResponse
{
    public IReadOnlyList<QueueEntryDto> Data       { get; init; } = [];
    public int                          TotalCount { get; init; }
}

/// <summary>
/// Enriched, paginated envelope for GET /queue/today (US_053, AC-4, EC-1, EC-2).
///
/// Extends <see cref="QueueTodayResponse"/> semantics with:
///   - server-side pagination metadata (Page, PageSize)
///   - computed average wait time for currently waiting patients (AC-4)
///   - alert count for patients waiting over the threshold (QueueSettings.WaitTimeThresholdMinutes)
///   - last-updated UTC timestamp for the "Last updated" UI badge (EC-2)
/// </summary>
public sealed class QueuePagedResponseDto
{
    /// <summary>Page of queue entries after filter + priority sort (urgent first, then appt time).</summary>
    public IReadOnlyList<QueueEntryDto> Data                    { get; init; } = [];
    /// <summary>Total entries matching the current filter (before pagination).</summary>
    public int                          TotalCount              { get; init; }
    /// <summary>Current 1-based page number.</summary>
    public int                          Page                    { get; init; }
    /// <summary>Page size (default 25 per EC-1).</summary>
    public int                          PageSize                { get; init; }
    /// <summary>
    /// Mean wait time in whole minutes for entries with status=waiting and non-null ArrivalTimestamp.
    /// Null when there are no currently waiting patients (AC-4).
    /// </summary>
    public int?                         AverageWaitTimeMinutes  { get; init; }
    /// <summary>UTC timestamp when this response was assembled (EC-2).</summary>
    public DateTime                     LastUpdated             { get; init; }
    /// <summary>Count of waiting patients whose wait exceeds the configured threshold (default 30 min).</summary>
    public int                          ThresholdAlertCount     { get; init; }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>Body for POST /queue/arrive (AC-1).</summary>
public sealed class MarkArrivalRequest
{
    [Required]
    public Guid AppointmentId { get; init; }
}

/// <summary>Body for PUT /queue/{queueId}/status (AC-3).</summary>
public sealed class UpdateQueueStatusRequest
{
    [Required]
    public string Status { get; init; } = string.Empty;
}

/// <summary>Body for PUT /queue/{queueId}/override (edge case).</summary>
public sealed class OverrideNoShowRequest
{
    [Required]
    public string NewStatus { get; init; } = "arrived_late";

    /// <summary>Staff-provided reason; minimum 10 characters (validated by FluentValidation).</summary>
    [Required]
    [MinLength(10)]
    public string Reason { get; init; } = string.Empty;
}

// ─── US_054 — Priority Queue Management DTOs ─────────────────────────────────

/// <summary>Body for PUT /queue/{queueId}/priority (US_054 AC-1, AC-4).</summary>
public sealed class SetPriorityRequest
{
    /// <summary>New priority level. Must be "urgent" or "normal" (case-insensitive).</summary>
    [Required]
    public string Priority { get; init; } = string.Empty;
}

/// <summary>Body for PUT /queue/reorder (US_054 AC-2, AC-3).</summary>
public sealed class ReorderQueueRequest
{
    /// <summary>The queue entry to reposition.</summary>
    [Required]
    public Guid QueueId { get; init; }

    /// <summary>
    /// Target 1-based position within today's active queue.
    /// Valid range: 1–999. The service clamps to the actual queue length.
    /// </summary>
    [Required]
    [Range(1, 999, ErrorMessage = "NewPosition must be between 1 and 999.")]
    public int NewPosition { get; init; }
}

/// <summary>
/// Response envelope for PUT /queue/reorder and PUT /queue/{id}/priority (US_054).
/// Returns the re-sorted queue so the frontend can update its list in a single round-trip.
/// </summary>
public sealed class QueueReorderResponse
{
    /// <summary>Full today's queue sorted by priority tier then queue_position.</summary>
    public IReadOnlyList<QueueEntryDto> Data { get; init; } = [];
    /// <summary>Total number of entries in today's queue (unfiltered).</summary>
    public int TotalCount { get; init; }
}

// ─── Internal result types ────────────────────────────────────────────────────

/// <summary>
/// Status transition validation outcome returned by QueueService internal helpers.
/// Keeps controller thin — status codes are mapped at the controller boundary.
/// </summary>
public enum QueueOperationResult
{
    Success,
    NotFound,
    Conflict,
    UnprocessableStatusTransition,
}

public sealed class QueueServiceResult<T>
{
    public QueueOperationResult Outcome   { get; private init; }
    public T?                   Value     { get; private init; }
    public string?              Message   { get; private init; }

    public bool IsSuccess => Outcome == QueueOperationResult.Success;

    public static QueueServiceResult<T> Ok(T value)
        => new() { Outcome = QueueOperationResult.Success, Value = value };

    public static QueueServiceResult<T> NotFound(string message)
        => new() { Outcome = QueueOperationResult.NotFound, Message = message };

    public static QueueServiceResult<T> Conflict(string message)
        => new() { Outcome = QueueOperationResult.Conflict, Message = message };

    public static QueueServiceResult<T> Unprocessable(string message)
        => new() { Outcome = QueueOperationResult.UnprocessableStatusTransition, Message = message };
}

// ─── Serialization helpers ────────────────────────────────────────────────────

/// <summary>Canonical string representations used by the API contract (frontend types in useArrivalQueue.ts).</summary>
public static class QueueStatusStrings
{
    public const string Waiting     = "waiting";
    public const string InVisit     = "in_visit";
    public const string Completed   = "completed";
    public const string NoShow      = "no_show";
    public const string ArrivedLate = "arrived_late";
    public const string Cancelled   = "cancelled";

    public static string FromEnum(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Waiting     => Waiting,
        QueueEntryStatus.InVisit     => InVisit,
        QueueEntryStatus.Completed   => Completed,
        QueueEntryStatus.NoShow      => NoShow,
        QueueEntryStatus.ArrivedLate => ArrivedLate,
        QueueEntryStatus.Cancelled   => Cancelled,
        _                            => status.ToString().ToLowerInvariant(),
    };

    public static string FromEnum(UPACIP.DataAccess.Enums.QueueStatus status) => status switch
    {
        UPACIP.DataAccess.Enums.QueueStatus.Waiting     => Waiting,
        UPACIP.DataAccess.Enums.QueueStatus.InVisit     => InVisit,
        UPACIP.DataAccess.Enums.QueueStatus.Completed   => Completed,
        UPACIP.DataAccess.Enums.QueueStatus.NoShow      => NoShow,
        UPACIP.DataAccess.Enums.QueueStatus.ArrivedLate => ArrivedLate,
        UPACIP.DataAccess.Enums.QueueStatus.Cancelled   => Cancelled,
        _                                                => status.ToString().ToLowerInvariant(),
    };

    public static string AppointmentStatusFromEnum(UPACIP.DataAccess.Enums.AppointmentStatus status) => status switch
    {
        UPACIP.DataAccess.Enums.AppointmentStatus.Scheduled => "scheduled",
        UPACIP.DataAccess.Enums.AppointmentStatus.Completed => "completed",
        UPACIP.DataAccess.Enums.AppointmentStatus.Cancelled => "cancelled",
        UPACIP.DataAccess.Enums.AppointmentStatus.NoShow    => "no-show",
        _                                                    => status.ToString().ToLowerInvariant(),
    };

    public static string PriorityFromEnum(UPACIP.DataAccess.Enums.QueuePriority priority) => priority switch
    {
        UPACIP.DataAccess.Enums.QueuePriority.Urgent => "urgent",
        _                                             => "normal",
    };
}

/// <summary>
/// Internal enum mirroring the string API contract for service-layer validation
/// without coupling directly to the EF enum (allows renaming without API breakage).
/// </summary>
public enum QueueEntryStatus
{
    Waiting,
    InVisit,
    Completed,
    NoShow,
    ArrivedLate,
    Cancelled,
}

// ─── US_055 — Wait Threshold Config DTOs ─────────────────────────────────────

/// <summary>Response for GET /api/queue/config/threshold (US_055 AC-3).</summary>
public sealed class WaitThresholdConfigResponse
{
    /// <summary>Current wait time alert threshold in whole minutes.</summary>
    public int ThresholdMinutes { get; init; }
}

/// <summary>Body for PUT /api/queue/config/threshold (US_055 AC-3, admin-only).</summary>
public sealed class UpdateWaitThresholdRequest
{
    /// <summary>New threshold in minutes. Valid range: 5–120.</summary>
    [Range(5, 120, ErrorMessage = "ThresholdMinutes must be between 5 and 120.")]
    public int ThresholdMinutes { get; init; }
}

// ─── US_056 — Queue History DTOs ─────────────────────────────────────────────

/// <summary>
/// Aggregated metrics for a single calendar day in the queue history report (US_056 AC-3).
/// </summary>
public sealed class QueueDailyMetrics
{
    /// <summary>Calendar date (UTC) in ISO-8601 format (yyyy-MM-dd).</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Mean wait time in whole minutes for all entries that day. Null when no data.</summary>
    public int? AvgWaitTimeMinutes { get; init; }

    /// <summary>Count of no-show queue entries for that day.</summary>
    public int NoShowCount { get; init; }

    /// <summary>Count of completed queue entries for that day (patient throughput).</summary>
    public int PatientThroughput { get; init; }

    /// <summary>Total queue entries recorded for that day (all statuses).</summary>
    public int TotalEntries { get; init; }
}

/// <summary>
/// Aggregated totals across the full requested date range (US_056 AC-3).
/// </summary>
public sealed class QueueHistorySummary
{
    /// <summary>Mean wait time across all days in the range. Null when no waiting entries.</summary>
    public int? AvgWaitTimeMinutes { get; init; }

    /// <summary>Total no-show count for the range.</summary>
    public int NoShowCount { get; init; }

    /// <summary>Total completed entries for the range.</summary>
    public int PatientThroughput { get; init; }

    /// <summary>Total queue entries for the range (all statuses).</summary>
    public int TotalEntries { get; init; }
}

/// <summary>
/// Response envelope for GET /api/queue/history (US_056 AC-3).
/// </summary>
public sealed class QueueHistoryResponse
{
    /// <summary>Inclusive start of the requested date range (yyyy-MM-dd).</summary>
    public string StartDate { get; init; } = string.Empty;

    /// <summary>Inclusive end of the requested date range (yyyy-MM-dd).</summary>
    public string EndDate { get; init; } = string.Empty;

    /// <summary>Per-day breakdown in ascending date order.</summary>
    public IReadOnlyList<QueueDailyMetrics> Metrics { get; init; } = [];

    /// <summary>Aggregated totals across the full range.</summary>
    public QueueHistorySummary Summary { get; init; } = new();

    /// <summary>
    /// Earliest date that has queue data, in yyyy-MM-dd format.
    /// Null when there is no historical data at all (empty database).
    /// Provided so the frontend can suggest a useful default date range.
    /// </summary>
    public string? AvailableFromDate { get; init; }
}
