namespace UPACIP.Service.Queue;

/// <summary>
/// Configuration for queue dashboard behaviour (US_053).
/// Bound from the <c>QueueSettings</c> appsettings section.
/// </summary>
public sealed class QueueSettings
{
    public const string SectionName = "QueueSettings";

    /// <summary>
    /// Number of minutes a patient must be waiting before the dashboard highlights the row
    /// and increments <c>ThresholdAlertCount</c> in the API response. Default: 30.
    /// </summary>
    public int WaitTimeThresholdMinutes { get; set; } = 30;
}
