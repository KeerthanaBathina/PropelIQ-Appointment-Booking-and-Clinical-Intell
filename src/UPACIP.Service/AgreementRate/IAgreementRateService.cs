namespace UPACIP.Service.AgreementRate;

// ─────────────────────────────────────────────────────────────────────────────
// Service-layer result types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Service-layer representation of a daily agreement-rate snapshot (US_050, AC-1, AC-2, FR-067).
/// Stored in <c>AgreementRateMetric</c> and projected to <c>AgreementRateDto</c> by the controller.
/// </summary>
public sealed record AgreementRateResult
{
    public DateOnly  CalculationDate               { get; init; }
    public decimal   DailyAgreementRate             { get; init; }
    public decimal?  Rolling30DayRate               { get; init; }
    public int       TotalCodesVerified             { get; init; }
    public int       CodesApprovedWithoutOverride   { get; init; }
    public int       CodesOverridden                { get; init; }
    public int       CodesPartiallyOverridden       { get; init; }
    public bool      MeetsMinimumThreshold          { get; init; }
}

/// <summary>Service-layer representation of a coding discrepancy (US_050, AC-3, FR-068).</summary>
public sealed record DiscrepancyResult
{
    public Guid            DiscrepancyId        { get; init; }
    public Guid            PatientId            { get; init; }
    public string          AiSuggestedCode      { get; init; } = string.Empty;
    public string          StaffSelectedCode    { get; init; } = string.Empty;
    public string          CodeType             { get; init; } = string.Empty;
    public string          DiscrepancyType      { get; init; } = string.Empty;
    public string?         OverrideJustification { get; init; }
    public DateTimeOffset  DetectedAt           { get; init; }
}

/// <summary>Service-layer representation of a below-threshold alert (US_050, AC-4).</summary>
public sealed record AlertResult
{
    public DateOnly               AlertDate              { get; init; }
    public decimal                CurrentRate            { get; init; }
    public IReadOnlyList<string>  DisagreementPatterns   { get; init; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Service contract for AI-human coding agreement rate calculation, discrepancy detection,
/// and alert generation (US_050, FR-067, FR-068, AIR-Q09).
/// </summary>
public interface IAgreementRateService
{
    /// <summary>
    /// Computes and persists the daily agreement rate for <paramref name="date"/>.
    /// Upserts the <c>AgreementRateMetric</c> row and writes new <c>CodingDiscrepancy</c>
    /// records for any overrides detected on that day.
    /// Also generates an alert record when the rate is below 98 % (AC-4).
    /// </summary>
    Task<AgreementRateResult> CalculateDailyRateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Returns the most recently computed daily metric row.</summary>
    Task<AgreementRateResult?> GetLatestMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all daily metric rows in the inclusive date range
    /// [<paramref name="from"/>, <paramref name="to"/>].
    /// </summary>
    Task<IReadOnlyList<AgreementRateResult>> GetMetricsRangeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns discrepancy records in the given date range.
    /// Both parameters are optional — omit for all-time.
    /// </summary>
    Task<IReadOnlyList<DiscrepancyResult>> GetDiscrepanciesAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>
    /// Returns alert records for dates where the daily rate was below 98 %, ordered
    /// most-recent first.
    /// </summary>
    Task<IReadOnlyList<AlertResult>> GetActiveAlertsAsync(CancellationToken ct = default);
}
