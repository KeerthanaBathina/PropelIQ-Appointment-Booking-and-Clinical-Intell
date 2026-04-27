namespace UPACIP.Service.AiMetrics.Dtos;

/// <summary>
/// Alert record returned in list responses (US_072 AC-4).
/// </summary>
public sealed record AiMetricAlertDto
{
    public Guid     AlertId          { get; init; }
    public DateTime GeneratedAt      { get; init; }
    public string   MetricName       { get; init; } = string.Empty;
    public double   CurrentValue     { get; init; }
    public double   TargetValue      { get; init; }
    public string   TrendDirection   { get; init; } = string.Empty;
    public bool     IsAcknowledged   { get; init; }
    public Guid?    AcknowledgedByUserId { get; init; }
}
