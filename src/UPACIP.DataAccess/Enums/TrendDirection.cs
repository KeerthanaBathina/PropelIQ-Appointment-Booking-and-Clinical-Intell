namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Indicates the direction a metric is trending relative to its historical baseline.
/// Used in <c>AiMetricAlert</c> to convey whether a breached metric is improving,
/// declining, or holding steady (US_072 AC-4).
/// Stored as a string via EF Core <c>HasConversion&lt;string&gt;()</c>.
/// </summary>
public enum TrendDirection
{
    /// <summary>Metric value is improving toward target.</summary>
    Up = 1,

    /// <summary>Metric value is declining away from target.</summary>
    Down = 2,

    /// <summary>Metric value is neither improving nor declining.</summary>
    Stable = 3,
}
