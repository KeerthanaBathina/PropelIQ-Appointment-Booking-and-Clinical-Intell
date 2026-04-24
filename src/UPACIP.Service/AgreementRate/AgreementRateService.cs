using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AgreementRate;

/// <summary>
/// Implements AI-human agreement rate computation, discrepancy detection, and alert
/// generation for the coding quality dashboard (US_050, FR-067, FR-068, AIR-Q09).
///
/// Key calculation rules:
/// <list type="bullet">
///   <item>Verified codes: <c>SuggestedByAi = true</c> AND <c>ApprovedByUserId != null</c>.</item>
///   <item>Agreement: <c>OriginalCodeValue</c> is null (no override) or equals final <c>CodeValue</c>.</item>
///   <item>Full override: <c>OriginalCodeValue</c> differs completely from final code.</item>
///   <item>Partial override: first 3 characters match (same base code, different specificity suffix) — counted as disagreement per EC-1.</item>
///   <item>Rate stored as [0.0, 1.0] in <c>AgreementRateMetric</c>; surface as percentage [0.0, 100.0] in results.</item>
///   <item>Minimum threshold: 50+ verified codes for statistical significance (EC-1).</item>
///   <item>Rolling 30-day: weighted average; <c>null</c> when fewer than 7 days of history.</item>
///   <item>Alert: generated for any day where rate &lt; 0.98 (98%).</item>
/// </list>
/// </summary>
public sealed class AgreementRateService : IAgreementRateService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const decimal TargetRateFraction     = 0.98m;   // 98 %
    private const int     MinimumThresholdCount  = 50;
    private const int     RollingWindowDays      = 30;
    private const int     MinRollingDaysRequired = 7;
    private const int     MaxAlertPatterns       = 5;

    // Number of leading characters used for "partial override" detection.
    // ICD-10: "E11" is the 3-char base; CPT: "992" groups E/M codes.
    private const int PartialOverridePrefixLength = 3;

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext              _db;
    private readonly ILogger<AgreementRateService>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AgreementRateService(
        ApplicationDbContext          db,
        ILogger<AgreementRateService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CalculateDailyRateAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AgreementRateResult> CalculateDailyRateAsync(
        DateOnly date, CancellationToken ct = default)
    {
        _logger.LogInformation("AgreementRateService: calculating daily rate for {Date}.", date);

        // ── 1. Load AI-suggested codes whose last update falls on the target date ──
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd   = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var verifiedCodes = await _db.MedicalCodes
            .AsNoTracking()
            .Where(m => m.SuggestedByAi
                     && m.ApprovedByUserId != null
                     && m.UpdatedAt >= dayStart
                     && m.UpdatedAt <= dayEnd)
            .ToListAsync(ct);

        int total = verifiedCodes.Count;

        // ── 2. Classify each code ───────────────────────────────────────────
        int approvedWithoutOverride = 0;
        int fullOverrides           = 0;
        int partialOverrides        = 0;

        var discrepanciesToWrite = new List<CodingDiscrepancy>();

        foreach (var code in verifiedCodes)
        {
            // No override: OriginalCodeValue is null (was never overridden)
            if (code.OriginalCodeValue is null)
            {
                approvedWithoutOverride++;
                continue;
            }

            // Override detected — classify as partial or full
            var discType = ClassifyDiscrepancy(code.OriginalCodeValue, code.CodeValue);
            if (discType == DataAccess.Enums.DiscrepancyType.PartialOverride)
                partialOverrides++;
            else
                fullOverrides++;

            // Only write a new CodingDiscrepancy if one doesn't already exist for this code
            var alreadyRecorded = await _db.CodingDiscrepancies
                .AnyAsync(d => d.MedicalCodeId == code.Id, ct);

            if (!alreadyRecorded)
            {
                discrepanciesToWrite.Add(new CodingDiscrepancy
                {
                    DiscrepancyId         = Guid.NewGuid(),
                    MedicalCodeId         = code.Id,
                    PatientId             = code.PatientId,
                    AiSuggestedCode       = code.OriginalCodeValue,
                    StaffSelectedCode     = code.CodeValue,
                    CodeType              = code.CodeType,
                    DiscrepancyType       = discType,
                    OverrideJustification = code.OverrideJustification,
                    DetectedAt            = DateTimeOffset.UtcNow,
                    CreatedAt             = DateTime.UtcNow,
                });
            }
        }

        if (discrepanciesToWrite.Count > 0)
        {
            _db.CodingDiscrepancies.AddRange(discrepanciesToWrite);
        }

        // ── 3. Compute daily rate ────────────────────────────────────────────
        // Store as [0.0, 1.0] fraction in the metric row.
        decimal dailyRateFraction = total > 0
            ? (decimal)approvedWithoutOverride / total
            : 0m;

        bool meetsThreshold = total >= MinimumThresholdCount;

        // ── 4. Compute rolling 30-day weighted average ───────────────────────
        decimal? rolling30DayPct = await ComputeRolling30DayAsync(date, ct);

        // ── 5. Upsert AgreementRateMetric ────────────────────────────────────
        var existing = await _db.AgreementRateMetrics
            .FirstOrDefaultAsync(a => a.CalculationDate == date, ct);

        if (existing is null)
        {
            existing = new AgreementRateMetric
            {
                MetricId            = Guid.NewGuid(),
                CalculationDate     = date,
                CreatedAt           = DateTime.UtcNow,
            };
            _db.AgreementRateMetrics.Add(existing);
        }

        existing.DailyAgreementRate           = dailyRateFraction;
        existing.Rolling30DayRate             = rolling30DayPct.HasValue
            ? rolling30DayPct.Value / 100m   // store as [0,1] fraction
            : null;
        existing.TotalCodesVerified           = total;
        existing.CodesApprovedWithoutOverride = approvedWithoutOverride;
        existing.CodesOverridden              = fullOverrides;
        existing.CodesPartiallyOverridden     = partialOverrides;
        existing.MeetsMinimumThreshold        = meetsThreshold;
        existing.UpdatedAt                    = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AgreementRateService: {Date} — total={Total}, approved={Approved}, rate={Rate:P2}, threshold={Threshold}.",
            date, total, approvedWithoutOverride, dailyRateFraction, meetsThreshold);

        return MapToResult(existing, rolling30DayPct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetLatestMetricsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AgreementRateResult?> GetLatestMetricsAsync(CancellationToken ct = default)
    {
        var row = await _db.AgreementRateMetrics
            .AsNoTracking()
            .OrderByDescending(a => a.CalculationDate)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : MapToResult(row, RollingToPercentage(row.Rolling30DayRate));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetMetricsRangeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgreementRateResult>> GetMetricsRangeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await _db.AgreementRateMetrics
            .AsNoTracking()
            .Where(a => a.CalculationDate >= from && a.CalculationDate <= to)
            .OrderBy(a => a.CalculationDate)
            .ToListAsync(ct);

        return rows.Select(r => MapToResult(r, RollingToPercentage(r.Rolling30DayRate))).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetDiscrepanciesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscrepancyResult>> GetDiscrepanciesAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.CodingDiscrepancies.AsNoTracking();

        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(d => d.DetectedAt >= start);
        }

        if (to.HasValue)
        {
            var end = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(d => d.DetectedAt <= end);
        }

        var rows = await query
            .OrderByDescending(d => d.DetectedAt)
            .ToListAsync(ct);

        return rows.Select(d => new DiscrepancyResult
        {
            DiscrepancyId         = d.DiscrepancyId,
            PatientId             = d.PatientId,
            AiSuggestedCode       = d.AiSuggestedCode,
            StaffSelectedCode     = d.StaffSelectedCode,
            CodeType              = d.CodeType.ToString(),
            DiscrepancyType       = d.DiscrepancyType.ToString(),
            OverrideJustification = d.OverrideJustification,
            DetectedAt            = d.DetectedAt,
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetActiveAlertsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AlertResult>> GetActiveAlertsAsync(CancellationToken ct = default)
    {
        // Alerts are derived from AgreementRateMetric rows where rate < 98% threshold.
        // Only rows that meet the minimum threshold count are included — below-threshold
        // days are reported as "not enough data", not as alert-worthy degradation.
        var belowThreshold = await _db.AgreementRateMetrics
            .AsNoTracking()
            .Where(a => a.MeetsMinimumThreshold && a.DailyAgreementRate < TargetRateFraction)
            .OrderByDescending(a => a.CalculationDate)
            .ToListAsync(ct);

        if (belowThreshold.Count == 0)
            return [];

        // Load discrepancies for all alert dates in one query
        var alertDates    = belowThreshold.Select(a => a.CalculationDate).ToList();
        var allDiscrepancies = await _db.CodingDiscrepancies
            .AsNoTracking()
            .ToListAsync(ct);

        var alerts = new List<AlertResult>(belowThreshold.Count);

        foreach (var row in belowThreshold)
        {
            var dayStart = row.CalculationDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd   = row.CalculationDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            // Top discrepancy patterns for this day
            var patterns = allDiscrepancies
                .Where(d => d.DetectedAt >= dayStart && d.DetectedAt <= dayEnd)
                .GroupBy(d => d.DiscrepancyType.ToString())
                .OrderByDescending(g => g.Count())
                .Take(MaxAlertPatterns)
                .Select(g => $"{g.Key} ({g.Count()} occurrence{(g.Count() == 1 ? "" : "s")})")
                .ToList();

            alerts.Add(new AlertResult
            {
                AlertDate            = row.CalculationDate,
                CurrentRate          = row.DailyAgreementRate * 100m,   // convert fraction to %
                DisagreementPatterns = patterns,
            });
        }

        return alerts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the rolling 30-day weighted-average agreement rate as a percentage [0.0, 100.0].
    /// Returns <c>null</c> when fewer than <see cref="MinRollingDaysRequired"/> days of history
    /// are available (including today's in-progress calculation).
    /// </summary>
    private async Task<decimal?> ComputeRolling30DayAsync(DateOnly date, CancellationToken ct)
    {
        var windowStart = date.AddDays(-(RollingWindowDays - 1));

        var window = await _db.AgreementRateMetrics
            .AsNoTracking()
            .Where(a => a.CalculationDate >= windowStart && a.CalculationDate <= date)
            .ToListAsync(ct);

        if (window.Count < MinRollingDaysRequired)
            return null;

        long sumApproved = window.Sum(a => (long)a.CodesApprovedWithoutOverride);
        long sumTotal    = window.Sum(a => (long)a.TotalCodesVerified);

        if (sumTotal == 0)
            return null;

        return (decimal)sumApproved / sumTotal * 100m;
    }

    /// <summary>
    /// Classifies whether an override is a partial (same 3-char prefix) or full override.
    /// </summary>
    private static DataAccess.Enums.DiscrepancyType ClassifyDiscrepancy(
        string originalCode, string finalCode)
    {
        if (originalCode.Length >= PartialOverridePrefixLength
            && finalCode.Length >= PartialOverridePrefixLength
            && string.Equals(
                originalCode[..PartialOverridePrefixLength],
                finalCode[..PartialOverridePrefixLength],
                StringComparison.OrdinalIgnoreCase))
        {
            return DataAccess.Enums.DiscrepancyType.PartialOverride;
        }

        return DataAccess.Enums.DiscrepancyType.FullOverride;
    }

    /// <summary>Maps an <see cref="AgreementRateMetric"/> row to the service result record.</summary>
    private static AgreementRateResult MapToResult(AgreementRateMetric row, decimal? rolling30DayPct) =>
        new()
        {
            CalculationDate             = row.CalculationDate,
            DailyAgreementRate          = row.DailyAgreementRate * 100m,   // fraction → %
            Rolling30DayRate            = rolling30DayPct,
            TotalCodesVerified          = row.TotalCodesVerified,
            CodesApprovedWithoutOverride = row.CodesApprovedWithoutOverride,
            CodesOverridden             = row.CodesOverridden,
            CodesPartiallyOverridden    = row.CodesPartiallyOverridden,
            MeetsMinimumThreshold       = row.MeetsMinimumThreshold,
        };

    /// <summary>Converts stored fraction (or null) to a display percentage.</summary>
    private static decimal? RollingToPercentage(decimal? storedFraction) =>
        storedFraction.HasValue ? storedFraction.Value * 100m : null;
}
