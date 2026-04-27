using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Pre-aggregated daily AI cost rollup by provider and request type (US_071 TASK_001, AC-1).
///
/// <para>
/// Written (or upserted) by the daily aggregation job, which reads all
/// <see cref="AiRequestLog"/> rows for a given calendar day and computes the
/// per-<see cref="Provider"/>/<see cref="RequestType"/> totals.  Querying this
/// table is O(log n) via the unique composite index; it avoids full GROUP BY scans
/// on the raw <c>ai_request_logs</c> table at dashboard request time (NFR-004).
/// </para>
///
/// <para>
/// A unique composite constraint on (<see cref="SummaryDate"/>, <see cref="Provider"/>,
/// <see cref="RequestType"/>) prevents duplicate aggregation rows and enables safe
/// INSERT … ON CONFLICT DO UPDATE (upsert) from the aggregation job.
/// </para>
///
/// Extends <see cref="BaseEntity"/> for <c>Id</c>, <c>CreatedAt</c>, and <c>UpdatedAt</c>.
/// </summary>
public sealed class AiCostDailySummary : BaseEntity
{
    /// <summary>
    /// Calendar day (UTC) that this summary covers.
    /// Stored as a PostgreSQL <c>date</c> column (no time component).
    /// </summary>
    public DateOnly SummaryDate { get; set; }

    /// <summary>AI provider dimension for this aggregation partition.</summary>
    public AiProvider Provider { get; set; }

    /// <summary>AI request type dimension for this aggregation partition.</summary>
    public AiRequestType RequestType { get; set; }

    /// <summary>
    /// Sum of <see cref="AiRequestLog.InputTokens"/> across all requests in this partition.
    /// Stored as <c>bigint</c> to handle high-volume days without integer overflow.
    /// </summary>
    public long TotalInputTokens { get; set; }

    /// <summary>
    /// Sum of <see cref="AiRequestLog.OutputTokens"/> across all requests in this partition.
    /// Stored as <c>bigint</c> to handle high-volume days without integer overflow.
    /// </summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>
    /// Sum of <see cref="AiRequestLog.EstimatedCost"/> across all requests in this partition (USD).
    /// Precision: <c>numeric(12,6)</c> — supports daily totals up to $999,999.999999.
    /// May include approximate entries; consumers should cross-check
    /// <see cref="ApproximateRequestCount"/> if precision is required.
    /// </summary>
    public decimal TotalEstimatedCost { get; set; }

    /// <summary>
    /// Count of <see cref="AiRequestLog"/> rows contributing to this summary partition.
    /// Used for average-cost-per-request calculations and trend reporting.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Count of requests within this partition where cost was estimated via the rate card
    /// (<see cref="AiCostSource.Approximate"/>).  Used to surface data-quality caveats in
    /// the admin cost dashboard.
    /// </summary>
    public int ApproximateRequestCount { get; set; }
}
