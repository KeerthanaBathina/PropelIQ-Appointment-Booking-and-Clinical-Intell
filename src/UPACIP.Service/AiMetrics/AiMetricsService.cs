using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AiMetrics.Dtos;

namespace UPACIP.Service.AiMetrics;

/// <summary>
/// Implements AI performance metrics aggregation, retrieval, and alert management
/// for the AI monitoring dashboard (US_072, AC-1 through AC-4).
///
/// <para>Accuracy calculation rules:</para>
/// <list type="bullet">
///   <item><b>Coding agreement</b>: AI-suggested codes (<c>SuggestedByAi = true</c>)
///   approved by staff (<c>ApprovedByUserId != null</c>).  Agreement = no original-code
///   override recorded (<c>OriginalCodeValue == null</c>).
///   Rate = (agreements / total approved) × 100. Target: 98 % (AC-1).</item>
///   <item><b>Extraction precision</b>: Verified extracted rows (<c>VerifiedByUserId != null</c>)
///   where <c>VerificationStatus == Verified</c> (accepted without correction).
///   Precision = (true positives / total verified) × 100. Target: 95 % (AC-2).</item>
///   <item><b>Extraction recall</b>: Ratio of extracted rows verified against total rows
///   created (flagged + non-flagged) in the period.
///   Recall = (total verified / total created) × 100. Target: 95 % (AC-2).</item>
///   <item>Minimum sample size: 30.  Below this, the service returns SampleSize &lt; MinSampleSize
///   and the dashboard shows "Insufficient data" (edge case).</item>
/// </list>
///
/// <para>Trend direction: delta between today's and yesterday's value.
/// &gt; +1 % → <see cref="TrendDirection.Up"/>,
/// &lt; -1 % → <see cref="TrendDirection.Down"/>, otherwise <see cref="TrendDirection.Stable"/>.</para>
///
/// <para>Latency: The P50/P95 percentiles are read from <c>AiLatencyMetric</c> rows
/// that are persisted by the aggregation job.  Raw timing data requires a
/// <c>DurationMilliseconds</c> field on <c>AiRequestLog</c> populated by the AI Gateway
/// (US_067 dependency).  Until that data is available, the aggregation stores
/// SampleSize = 0 and the dashboard displays "No data".</para>
/// </summary>
public sealed class AiMetricsService : IAiMetricsService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    internal const int    MinSampleSize     = 30;
    internal const double TrendThresholdPct = 1.0;    // ± 1 % triggers Up/Down

    // Default accuracy targets (percentages)
    private const double CodingAgreementTargetPct  = 98.0;
    private const double ExtractionTargetPct        = 95.0;

    // Default latency targets (milliseconds) per AC-3
    private const double IntakeP95TargetMs         = 1_000.0;
    private const double DocumentParsingP95TargetMs = 30_000.0;
    private const double MedicalCodingP95TargetMs   = 5_000.0;

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext        _db;
    private readonly ILogger<AiMetricsService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiMetricsService(
        ApplicationDbContext      db,
        ILogger<AiMetricsService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetCurrentSummaryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AiMetricsSummaryDto> GetCurrentSummaryAsync(CancellationToken ct = default)
    {
        // Load most recent accuracy metric for each type
        var accuracyRows = await _db.AiAccuracyMetrics
            .AsNoTracking()
            .OrderByDescending(a => a.MetricDate)
            .Take(6) // 2 days × 3 metric types — sufficient for current + trend
            .ToListAsync(ct);

        // Load most recent latency metrics (one per operation type)
        var latencyRows = await _db.AiLatencyMetrics
            .AsNoTracking()
            .OrderByDescending(l => l.MetricDate)
            .Take(6)
            .ToListAsync(ct);

        // Load active alert count
        int activeAlerts = await _db.AiMetricAlerts
            .CountAsync(a => !a.IsAcknowledged, ct);

        DateTime? lastCalculatedAt = accuracyRows.Count > 0
            ? accuracyRows.Max(a => a.MetricDate)
            : null;

        // Build accuracy values + trend
        var codingCurrent  = accuracyRows.FirstOrDefault(a => a.MetricType == AiMetricType.CodingAgreement);
        var codingPrevious = accuracyRows
            .Where(a => a.MetricType == AiMetricType.CodingAgreement && codingCurrent != null && a.MetricDate < codingCurrent.MetricDate)
            .OrderByDescending(a => a.MetricDate)
            .FirstOrDefault();

        var precisionCurrent  = accuracyRows.FirstOrDefault(a => a.MetricType == AiMetricType.ExtractionPrecision);
        var precisionPrevious = accuracyRows
            .Where(a => a.MetricType == AiMetricType.ExtractionPrecision && precisionCurrent != null && a.MetricDate < precisionCurrent.MetricDate)
            .OrderByDescending(a => a.MetricDate)
            .FirstOrDefault();

        var recallCurrent  = accuracyRows.FirstOrDefault(a => a.MetricType == AiMetricType.ExtractionRecall);
        var recallPrevious = accuracyRows
            .Where(a => a.MetricType == AiMetricType.ExtractionRecall && recallCurrent != null && a.MetricDate < recallCurrent.MetricDate)
            .OrderByDescending(a => a.MetricDate)
            .FirstOrDefault();

        // Build latency sub-objects
        var latencyDtos = BuildLatencyDtos(latencyRows);

        return new AiMetricsSummaryDto
        {
            CodingAgreementRate    = codingCurrent?.Value ?? 0.0,
            CodingAgreementTarget  = codingCurrent?.TargetValue ?? CodingAgreementTargetPct,
            CodingSampleSize       = codingCurrent?.SampleSize ?? 0,
            ExtractionPrecision    = precisionCurrent?.Value ?? 0.0,
            ExtractionRecall       = recallCurrent?.Value ?? 0.0,
            ExtractionTarget       = precisionCurrent?.TargetValue ?? ExtractionTargetPct,
            ExtractionSampleSize   = precisionCurrent?.SampleSize ?? 0,
            MinSampleSize          = MinSampleSize,
            CodingAgreementTrend   = ComputeTrend(codingCurrent?.Value, codingPrevious?.Value).ToString(),
            ExtractionPrecisionTrend = ComputeTrend(precisionCurrent?.Value, precisionPrevious?.Value).ToString(),
            ExtractionRecallTrend  = ComputeTrend(recallCurrent?.Value, recallPrevious?.Value).ToString(),
            Latencies              = latencyDtos,
            ActiveAlertCount       = activeAlerts,
            LastCalculatedAt       = lastCalculatedAt,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTimeSeriesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AiMetricsTimeSeriesDto> GetTimeSeriesAsync(
        string            metricType,
        DateTime          startDate,
        DateTime          endDate,
        string            granularity,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<AiMetricType>(metricType, ignoreCase: true, out var parsedType))
        {
            _logger.LogWarning("GetTimeSeriesAsync: unknown metricType '{MetricType}'.", metricType);
            return new AiMetricsTimeSeriesDto { MetricType = metricType, Granularity = granularity };
        }

        var rows = await _db.AiAccuracyMetrics
            .AsNoTracking()
            .Where(a => a.MetricType == parsedType
                     && a.MetricDate >= startDate
                     && a.MetricDate <= endDate)
            .OrderBy(a => a.MetricDate)
            .ToListAsync(ct);

        double target = rows.Count > 0 ? rows[^1].TargetValue : ExtractionTargetPct;

        var points = granularity.ToLowerInvariant() switch
        {
            "weekly"  => AggregateWeekly(rows),
            "monthly" => AggregateMonthly(rows),
            _         => rows.Select(r => new TimeSeriesPointDto
                         {
                             Date       = r.MetricDate,
                             Value      = r.Value,
                             SampleSize = r.SampleSize,
                         }).ToList(),
        };

        return new AiMetricsTimeSeriesDto
        {
            MetricType  = parsedType.ToString(),
            Granularity = granularity,
            TargetValue = target,
            DataPoints  = points,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetActiveAlertsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AiMetricAlertDto>> GetActiveAlertsAsync(CancellationToken ct = default)
    {
        var alerts = await _db.AiMetricAlerts
            .AsNoTracking()
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.GeneratedAt)
            .ToListAsync(ct);

        return alerts.Select(a => new AiMetricAlertDto
        {
            AlertId              = a.AlertId,
            GeneratedAt          = a.GeneratedAt,
            MetricName           = a.MetricName,
            CurrentValue         = a.CurrentValue,
            TargetValue          = a.TargetValue,
            TrendDirection       = a.TrendDirection.ToString(),
            IsAcknowledged       = a.IsAcknowledged,
            AcknowledgedByUserId = a.AcknowledgedByUserId,
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AcknowledgeAlertAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> AcknowledgeAlertAsync(
        Guid              alertId,
        Guid              acknowledgedByUserId,
        CancellationToken ct = default)
    {
        var alert = await _db.AiMetricAlerts
            .FirstOrDefaultAsync(a => a.AlertId == alertId, ct);

        if (alert is null)
        {
            return false;
        }

        alert.IsAcknowledged       = true;
        alert.AcknowledgedByUserId = acknowledgedByUserId;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AiMetricsService: alert {AlertId} acknowledged by user {UserId}.",
            alertId, acknowledgedByUserId);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RunDailyAggregationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RunDailyAggregationAsync(DateTime targetDate, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "AiMetricsService: starting daily aggregation for {Date:yyyy-MM-dd}.", targetDate);

        var dayStart = targetDate.Date.ToUniversalTime();
        var dayEnd   = dayStart.AddDays(1).AddTicks(-1);

        // Load thresholds from the database; fall back to defaults for missing entries
        var thresholds = await _db.AiMetricThresholds
            .AsNoTracking()
            .Where(t => t.IsEnabled)
            .ToListAsync(ct);

        // ── 1. Accuracy metrics ───────────────────────────────────────────
        await AggregateAccuracyAsync(dayStart, dayEnd, thresholds, ct);

        // ── 2. Latency metrics ────────────────────────────────────────────
        await AggregateLatencyAsync(dayStart, dayEnd, ct);

        // ── 3. Threshold evaluation → alert generation ────────────────────
        await EvaluateThresholdsAndGenerateAlertsAsync(dayStart, thresholds, ct);

        _logger.LogInformation(
            "AiMetricsService: daily aggregation complete for {Date:yyyy-MM-dd}.", targetDate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — accuracy aggregation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task AggregateAccuracyAsync(
        DateTime              dayStart,
        DateTime              dayEnd,
        List<AiMetricThreshold> thresholds,
        CancellationToken     ct)
    {
        // ── Coding agreement rate ─────────────────────────────────────────────
        var approvedCodes = await _db.MedicalCodes
            .AsNoTracking()
            .Where(m => m.SuggestedByAi
                     && m.ApprovedByUserId != null
                     && m.UpdatedAt >= dayStart
                     && m.UpdatedAt <= dayEnd)
            .Select(m => new { m.Id, HasOverride = m.OriginalCodeValue != null })
            .ToListAsync(ct);

        int codingSample    = approvedCodes.Count;
        int codingAgreements = approvedCodes.Count(m => !m.HasOverride);
        double codingRate    = codingSample > 0
            ? (double)codingAgreements / codingSample * 100.0
            : 0.0;

        double codingTarget = GetThresholdTarget(
            thresholds, AiMetricType.CodingAgreement.ToString(), CodingAgreementTargetPct);

        await UpsertAccuracyMetricAsync(
            dayStart, AiMetricType.CodingAgreement, codingRate, codingSample, codingTarget, ct);

        // ── Extraction precision and recall ───────────────────────────────────
        // Precision: how many AI extractions were accepted unchanged (Verified, not Corrected)
        // Recall:    how many total extractable items the AI found (verified / total in period)
        var extractionRows = await _db.ExtractedData
            .AsNoTracking()
            .Where(e => e.CreatedAt >= dayStart && e.CreatedAt <= dayEnd)
            .Select(e => new
            {
                IsVerified  = e.VerifiedByUserId != null,
                IsAccepted  = e.VerificationStatus == VerificationStatus.Verified,
            })
            .ToListAsync(ct);

        int totalCreated   = extractionRows.Count;
        int totalVerified  = extractionRows.Count(e => e.IsVerified);
        int truePositives  = extractionRows.Count(e => e.IsAccepted);

        double precision = totalVerified > 0
            ? (double)truePositives / totalVerified * 100.0
            : 0.0;

        double recall = totalCreated > 0
            ? (double)totalVerified / totalCreated * 100.0
            : 0.0;

        double extractionTarget = GetThresholdTarget(
            thresholds, AiMetricType.ExtractionPrecision.ToString(), ExtractionTargetPct);

        await UpsertAccuracyMetricAsync(
            dayStart, AiMetricType.ExtractionPrecision, precision, totalVerified, extractionTarget, ct);

        await UpsertAccuracyMetricAsync(
            dayStart, AiMetricType.ExtractionRecall, recall, totalCreated, extractionTarget, ct);
    }

    private async Task UpsertAccuracyMetricAsync(
        DateTime          metricDate,
        AiMetricType      metricType,
        double            value,
        int               sampleSize,
        double            targetValue,
        CancellationToken ct)
    {
        var existing = await _db.AiAccuracyMetrics
            .FirstOrDefaultAsync(a => a.MetricDate == metricDate && a.MetricType == metricType, ct);

        if (existing is null)
        {
            _db.AiAccuracyMetrics.Add(new AiAccuracyMetric
            {
                MetricDate  = metricDate,
                MetricType  = metricType,
                Value       = value,
                SampleSize  = sampleSize,
                TargetValue = targetValue,
            });
        }
        else
        {
            existing.Value       = value;
            existing.SampleSize  = sampleSize;
            existing.TargetValue = targetValue;
            existing.UpdatedAt   = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — latency aggregation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task AggregateLatencyAsync(
        DateTime          dayStart,
        DateTime          dayEnd,
        CancellationToken ct)
    {
        // AiRequestLog does not yet carry a DurationMilliseconds column (US_067 dependency).
        // We write a SampleSize-only row so the dashboard can show "No data" gracefully
        // rather than crashing. When US_067 adds duration tracking, replace this with real
        // percentile calculations over the DurationMilliseconds values.

        var typeMapping = new Dictionary<AiRequestType, (AiOperationType OpType, double TargetMs)>
        {
            [AiRequestType.ConversationalIntake] = (AiOperationType.Intake,         IntakeP95TargetMs),
            [AiRequestType.DocumentParsing]      = (AiOperationType.DocumentParsing, DocumentParsingP95TargetMs),
            [AiRequestType.MedicalCoding]        = (AiOperationType.MedicalCoding,   MedicalCodingP95TargetMs),
        };

        foreach (var (requestType, (opType, targetMs)) in typeMapping)
        {
            int sampleSize = await _db.AiRequestLogs
                .CountAsync(l => l.RequestType == requestType
                              && l.CreatedAt >= dayStart
                              && l.CreatedAt <= dayEnd, ct);

            await UpsertLatencyMetricAsync(dayStart, opType, 0.0, 0.0, targetMs, sampleSize, ct);
        }
    }

    private async Task UpsertLatencyMetricAsync(
        DateTime          metricDate,
        AiOperationType   operationType,
        double            p50Ms,
        double            p95Ms,
        double            targetP95Ms,
        int               sampleSize,
        CancellationToken ct)
    {
        var existing = await _db.AiLatencyMetrics
            .FirstOrDefaultAsync(l => l.MetricDate == metricDate && l.OperationType == operationType, ct);

        if (existing is null)
        {
            _db.AiLatencyMetrics.Add(new AiLatencyMetric
            {
                MetricDate            = metricDate,
                OperationType         = operationType,
                P50Milliseconds       = p50Ms,
                P95Milliseconds       = p95Ms,
                TargetP95Milliseconds = targetP95Ms,
                SampleSize            = sampleSize,
            });
        }
        else
        {
            existing.P50Milliseconds       = p50Ms;
            existing.P95Milliseconds       = p95Ms;
            existing.TargetP95Milliseconds = targetP95Ms;
            existing.SampleSize            = sampleSize;
            existing.UpdatedAt             = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — threshold evaluation → alert generation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task EvaluateThresholdsAndGenerateAlertsAsync(
        DateTime              dayStart,
        List<AiMetricThreshold> thresholds,
        CancellationToken     ct)
    {
        // Evaluate each accuracy metric type
        foreach (AiMetricType metricType in Enum.GetValues<AiMetricType>())
        {
            var current = await _db.AiAccuracyMetrics
                .AsNoTracking()
                .Where(a => a.MetricDate == dayStart && a.MetricType == metricType)
                .FirstOrDefaultAsync(ct);

            if (current is null || current.SampleSize < MinSampleSize)
                continue;

            double targetValue = GetThresholdTarget(
                thresholds, metricType.ToString(),
                metricType == AiMetricType.CodingAgreement ? CodingAgreementTargetPct : ExtractionTargetPct);

            if (current.Value < targetValue)
            {
                // Determine trend — compare with previous day's value
                var previous = await _db.AiAccuracyMetrics
                    .AsNoTracking()
                    .Where(a => a.MetricType == metricType && a.MetricDate < dayStart)
                    .OrderByDescending(a => a.MetricDate)
                    .FirstOrDefaultAsync(ct);

                TrendDirection trend = ComputeTrend(current.Value, previous?.Value);

                var alert = new AiMetricAlert
                {
                    GeneratedAt    = DateTime.UtcNow,
                    MetricName     = metricType.ToString(),
                    CurrentValue   = current.Value,
                    TargetValue    = targetValue,
                    TrendDirection = trend,
                    IsAcknowledged = false,
                };
                _db.AiMetricAlerts.Add(alert);

                _logger.LogWarning(
                    "AiMetricsService: threshold breach — metric={Metric}, value={Value:F2}, target={Target:F2}, trend={Trend}.",
                    metricType, current.Value, targetValue, trend);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static TrendDirection ComputeTrend(double? current, double? previous)
    {
        if (current is null || previous is null)
            return TrendDirection.Stable;

        double delta = current.Value - previous.Value;
        if (delta > TrendThresholdPct)  return TrendDirection.Up;
        if (delta < -TrendThresholdPct) return TrendDirection.Down;
        return TrendDirection.Stable;
    }

    private static double GetThresholdTarget(
        IEnumerable<AiMetricThreshold> thresholds,
        string                         metricName,
        double                         defaultValue)
    {
        return thresholds
            .FirstOrDefault(t => t.MetricName.Equals(metricName, StringComparison.OrdinalIgnoreCase))
            ?.TargetValue ?? defaultValue;
    }

    private static IReadOnlyList<OperationLatencyDto> BuildLatencyDtos(
        IReadOnlyList<AiLatencyMetric> latencyRows)
    {
        var result = new List<OperationLatencyDto>();

        foreach (AiOperationType opType in Enum.GetValues<AiOperationType>())
        {
            var row = latencyRows.FirstOrDefault(l => l.OperationType == opType);

            result.Add(new OperationLatencyDto
            {
                OperationType         = opType.ToString(),
                P50Milliseconds       = row?.P50Milliseconds ?? 0.0,
                P95Milliseconds       = row?.P95Milliseconds ?? 0.0,
                TargetP95Milliseconds = row?.TargetP95Milliseconds ?? GetDefaultLatencyTarget(opType),
                SampleSize            = row?.SampleSize ?? 0,
                MeetsTarget           = row is not null
                                        && row.SampleSize > 0
                                        && row.P95Milliseconds <= row.TargetP95Milliseconds,
            });
        }

        return result;
    }

    private static double GetDefaultLatencyTarget(AiOperationType opType) => opType switch
    {
        AiOperationType.Intake          => IntakeP95TargetMs,
        AiOperationType.DocumentParsing => DocumentParsingP95TargetMs,
        AiOperationType.MedicalCoding   => MedicalCodingP95TargetMs,
        _                               => 0.0,
    };

    private static List<TimeSeriesPointDto> AggregateWeekly(List<AiAccuracyMetric> rows)
    {
        return rows
            .GroupBy(r => IsoWeekStart(r.MetricDate))
            .OrderBy(g => g.Key)
            .Select(g => new TimeSeriesPointDto
            {
                Date       = g.Key,
                Value      = g.Sum(r => r.SampleSize) > 0
                             ? g.Sum(r => r.Value * r.SampleSize) / g.Sum(r => r.SampleSize)
                             : 0.0,
                SampleSize = g.Sum(r => r.SampleSize),
            })
            .ToList();
    }

    private static List<TimeSeriesPointDto> AggregateMonthly(List<AiAccuracyMetric> rows)
    {
        return rows
            .GroupBy(r => new DateTime(r.MetricDate.Year, r.MetricDate.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new TimeSeriesPointDto
            {
                Date       = g.Key,
                Value      = g.Sum(r => r.SampleSize) > 0
                             ? g.Sum(r => r.Value * r.SampleSize) / g.Sum(r => r.SampleSize)
                             : 0.0,
                SampleSize = g.Sum(r => r.SampleSize),
            })
            .ToList();
    }

    /// <summary>Returns the Monday of the ISO week containing <paramref name="date"/>.</summary>
    private static DateTime IsoWeekStart(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date.ToUniversalTime();
    }
}
