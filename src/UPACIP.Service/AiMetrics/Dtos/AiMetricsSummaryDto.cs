namespace UPACIP.Service.AiMetrics.Dtos;

/// <summary>
/// Latency summary for a single AI operation type (US_072 AC-3).
/// Millisecond values — consumers convert to seconds for display.
/// </summary>
public sealed record OperationLatencyDto
{
    public string OperationType        { get; init; } = string.Empty;
    public double P50Milliseconds      { get; init; }
    public double P95Milliseconds      { get; init; }
    public double TargetP95Milliseconds { get; init; }
    public int    SampleSize           { get; init; }
    public bool   MeetsTarget          { get; init; }
}

/// <summary>
/// Dashboard summary response for the AI monitoring dashboard (US_072 AC-1, AC-2, AC-3).
///
/// <para>
/// When <see cref="CodingSampleSize"/> or <see cref="ExtractionSampleSize"/> is below
/// <see cref="MinSampleSize"/>, the corresponding accuracy values should be displayed
/// as "Insufficient data" (edge case requirement).
/// </para>
/// </summary>
public sealed record AiMetricsSummaryDto
{
    // ── Accuracy metrics ──────────────────────────────────────────────────────

    /// <summary>Current AI-human agreement rate for medical coding as a percentage [0–100].</summary>
    public double CodingAgreementRate { get; init; }

    /// <summary>Target threshold for coding agreement rate (default 98.0).</summary>
    public double CodingAgreementTarget { get; init; }

    /// <summary>Number of AI-suggested codes included in the coding agreement calculation.</summary>
    public int CodingSampleSize { get; init; }

    /// <summary>Current AI extraction precision as a percentage [0–100].</summary>
    public double ExtractionPrecision { get; init; }

    /// <summary>Current AI extraction recall as a percentage [0–100].</summary>
    public double ExtractionRecall { get; init; }

    /// <summary>Target threshold for extraction precision and recall (default 95.0).</summary>
    public double ExtractionTarget { get; init; }

    /// <summary>Number of extracted data items included in the precision/recall calculation.</summary>
    public int ExtractionSampleSize { get; init; }

    /// <summary>
    /// Minimum sample count required for a metric to be considered statistically meaningful.
    /// Constant value: 30.  When <see cref="CodingSampleSize"/> or <see cref="ExtractionSampleSize"/>
    /// is below this value the dashboard must show "Insufficient data".
    /// </summary>
    public int MinSampleSize { get; init; } = 30;

    // ── Trend directions ──────────────────────────────────────────────────────

    /// <summary>Trend direction for coding agreement rate relative to the previous day.</summary>
    public string CodingAgreementTrend { get; init; } = string.Empty;

    /// <summary>Trend direction for extraction precision relative to the previous day.</summary>
    public string ExtractionPrecisionTrend { get; init; } = string.Empty;

    /// <summary>Trend direction for extraction recall relative to the previous day.</summary>
    public string ExtractionRecallTrend { get; init; } = string.Empty;

    // ── Latency metrics ───────────────────────────────────────────────────────

    /// <summary>Latency percentile summaries indexed by operation type (AC-3).</summary>
    public IReadOnlyList<OperationLatencyDto> Latencies { get; init; } = [];

    // ── Active alerts ─────────────────────────────────────────────────────────

    /// <summary>Count of active (unacknowledged) alerts at the time of this snapshot.</summary>
    public int ActiveAlertCount { get; init; }

    /// <summary>UTC timestamp of the most recently aggregated metric record.</summary>
    public DateTime? LastCalculatedAt { get; init; }
}
