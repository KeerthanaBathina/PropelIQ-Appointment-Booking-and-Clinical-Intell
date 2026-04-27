namespace UPACIP.Service.AiMetrics.Dtos;

/// <summary>
/// A single data point in a time-series array (US_072 edge case: daily/weekly/monthly views).
/// </summary>
public sealed record TimeSeriesPointDto
{
    public DateTime Date       { get; init; }
    public double   Value      { get; init; }
    public int      SampleSize { get; init; }
}

/// <summary>
/// Time-series chart data for a single metric type over a date range (US_072 edge case).
/// Supports daily, weekly, and monthly granularity for the date-range selector.
/// </summary>
public sealed record AiMetricsTimeSeriesDto
{
    /// <summary>Metric type name (e.g., "CodingAgreement", "ExtractionPrecision", "ExtractionRecall").</summary>
    public string MetricType { get; init; } = string.Empty;

    /// <summary>Granularity of the aggregation applied ("daily", "weekly", "monthly").</summary>
    public string Granularity { get; init; } = string.Empty;

    /// <summary>Target threshold for this metric (percentage or milliseconds depending on type).</summary>
    public double TargetValue { get; init; }

    /// <summary>Ordered time-series data points for chart rendering, oldest first.</summary>
    public IReadOnlyList<TimeSeriesPointDto> DataPoints { get; init; } = [];
}
