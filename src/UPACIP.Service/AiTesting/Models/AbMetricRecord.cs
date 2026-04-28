namespace UPACIP.Service.AiTesting.Models;

/// <summary>
/// Per-request metric record captured by the A/B testing middleware after each AI call
/// (US_080 task_001, AC-2, AIR-O10).
/// </summary>
public sealed class AbMetricRecord
{
    /// <summary>Surrogate identifier for this metric row.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The experiment this metric belongs to.</summary>
    public Guid ExperimentId { get; init; }

    /// <summary>Which model variant served this request.</summary>
    public AbVariant Variant { get; init; }

    /// <summary>
    /// Downstream accuracy rating in [0, 1].  Null until the response has been
    /// evaluated by a downstream validator (e.g., staff verification).
    /// </summary>
    public float? Accuracy { get; init; }

    /// <summary>End-to-end inference latency in milliseconds (stopwatch measured).</summary>
    public long LatencyMs { get; init; }

    /// <summary>Total tokens consumed (input + output).</summary>
    public int TokensUsed { get; init; }

    /// <summary>Estimated monetary cost for this request (USD).</summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Request type identifier (e.g. <c>document-parsing</c>,
    /// <c>conversational-intake</c>, <c>medical-coding</c>).
    /// </summary>
    public string RequestType { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the metric was recorded.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Aggregated per-variant performance metrics computed by
/// <see cref="IAbTestingService.GetExperimentResultsAsync"/>
/// (US_080 task_001, AC-2).
/// </summary>
public sealed class AbVariantMetrics
{
    /// <summary>Average accuracy across all evaluated responses in the variant (0–1).</summary>
    public float MeanAccuracy { get; init; }

    /// <summary>Median end-to-end latency in milliseconds.</summary>
    public float MedianLatencyMs { get; init; }

    /// <summary>95th-percentile end-to-end latency in milliseconds.</summary>
    public float P95LatencyMs { get; init; }

    /// <summary>Summed estimated cost (USD) across all requests in the variant.</summary>
    public decimal TotalCost { get; init; }

    /// <summary>Average estimated cost per request (USD).</summary>
    public decimal AverageCostPerRequest { get; init; }
}

/// <summary>
/// Aggregated A/B experiment comparison results returned by the admin API
/// (US_080 task_001, AC-2).
/// </summary>
public sealed class AbExperimentResult
{
    /// <summary>Experiment the results apply to.</summary>
    public Guid ExperimentId { get; init; }

    /// <summary>Aggregated metrics for the control variant.</summary>
    public AbVariantMetrics ControlMetrics { get; init; } = new();

    /// <summary>Aggregated metrics for the candidate variant.</summary>
    public AbVariantMetrics CandidateMetrics { get; init; } = new();

    /// <summary>Number of requests assigned to the control variant.</summary>
    public int ControlSampleSize { get; init; }

    /// <summary>Number of requests assigned to the candidate variant.</summary>
    public int CandidateSampleSize { get; init; }

    /// <summary>
    /// <see langword="true"/> when both variants have ≥ 30 samples and a basic
    /// z-test on accuracy difference produces p &lt; 0.05, indicating the
    /// observed accuracy delta is unlikely to be due to chance.
    /// </summary>
    public bool IsStatisticallySignificant { get; init; }
}
