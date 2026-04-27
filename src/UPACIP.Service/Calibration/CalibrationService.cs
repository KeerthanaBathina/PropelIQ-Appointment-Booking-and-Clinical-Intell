using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
namespace UPACIP.Service.Calibration;

/// <summary>
/// Confidence score calibration service implementing Platt scaling, automatic
/// low-confidence flagging, per-category calibration, and drift detection (US_073 AC-1–AC-4).
///
/// <para>
/// Registered as Scoped — one instance per HTTP request or per
/// <c>IServiceScope</c> created by <see cref="CalibrationJob"/>.
/// </para>
/// </summary>
public sealed class CalibrationService : ICalibrationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calibrated score threshold below which records are flagged for manual review (AC-2, AIR-Q08).
    /// </summary>
    internal const float FlagThreshold = 0.80f;

    /// <summary>
    /// Maximum absolute gap between predicted and actual accuracy that does not trigger
    /// a <see cref="CalibrationDriftAlert"/> (AC-4).
    /// </summary>
    internal const double DriftThreshold = 0.05; // 5 %

    /// <summary>
    /// Number of days of verified data used for each calibration run.
    /// </summary>
    private const int LookbackDays = 90;

    /// <summary>
    /// Number of confidence bins (0.0–0.1, 0.1–0.2, …, 0.9–1.0).
    /// </summary>
    private const int BinCount = 10;

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext          _db;
    private readonly ILogger<CalibrationService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CalibrationService(
        ApplicationDbContext        db,
        ILogger<CalibrationService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICalibrationService implementation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<float> CalibrateScoreAsync(
        float             rawScore,
        DataType          dataType,
        Guid              extractedDataId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.ExtractedData
            .FirstOrDefaultAsync(e => e.Id == extractedDataId, cancellationToken);

        if (record is null)
        {
            _logger.LogWarning(
                "CalibrateScoreAsync: ExtractedData {Id} not found; returning raw score.", extractedDataId);
            return rawScore;
        }

        // Look up active calibration parameters for this data type.
        var param = await _db.CalibrationParameters
            .Where(p => p.DataType == dataType && p.IsActive)
            .OrderByDescending(p => p.LastCalibratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (param is null)
        {
            // Insufficient verification data edge case — use raw score, mark pending.
            record.CalibratedConfidenceScore = null;
            record.CalibrationStatus         = CalibrationStatus.CalibrationPending;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "CalibrateScoreAsync: No active CalibrationParameter for {DataType}; " +
                "ExtractedData {Id} marked CalibrationPending.",
                dataType, extractedDataId);

            return rawScore;
        }

        double calibrated = PlattScaler.CalibrateScore(rawScore, param.Slope, param.Intercept);
        float  calibratedF = (float)Math.Clamp(calibrated, 0.0, 1.0);

        record.CalibratedConfidenceScore = calibratedF;
        record.CalibrationStatus         = CalibrationStatus.Calibrated;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "CalibrateScoreAsync: ExtractedData {Id} raw={Raw:F4} calibrated={Cal:F4} DataType={DataType}.",
            extractedDataId, rawScore, calibratedF, dataType);

        return calibratedF;
    }

    /// <inheritdoc/>
    public async Task FlagLowConfidenceAsync(
        Guid              extractedDataId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.ExtractedData
            .FirstOrDefaultAsync(e => e.Id == extractedDataId, cancellationToken);

        if (record is null)
        {
            _logger.LogWarning(
                "FlagLowConfidenceAsync: ExtractedData {Id} not found; skipping flag.", extractedDataId);
            return;
        }

        // Prefer calibrated score; fall back to raw score for uncalibrated records.
        float scoreToEvaluate = record.CalibratedConfidenceScore ?? record.ConfidenceScore;

        if (scoreToEvaluate < FlagThreshold)
        {
            record.FlaggedForReview = true;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "FlagLowConfidenceAsync: ExtractedData {Id} flagged for review. " +
                "Score={Score:F4} Threshold={Threshold} CalibrationStatus={Status}.",
                extractedDataId, scoreToEvaluate, FlagThreshold, record.CalibrationStatus);
        }
    }

    /// <inheritdoc/>
    public async Task RunWeeklyCalibrationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RunWeeklyCalibrationAsync: starting weekly calibration run.");

        foreach (DataType dataType in Enum.GetValues<DataType>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CalibrateDataTypeAsync(dataType, cancellationToken);
        }

        _logger.LogInformation("RunWeeklyCalibrationAsync: completed all data type calibrations.");
    }

    /// <inheritdoc/>
    public async Task<bool> HasSufficientDataAsync(
        DataType          dataType,
        CancellationToken cancellationToken = default)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-LookbackDays);

        int count = await _db.ExtractedData
            .CountAsync(
                e => e.DataType == dataType
                  && e.VerifiedByUserId != null
                  && e.CreatedAt >= cutoff,
                cancellationToken);

        return count >= PlattScaler.MinimumSampleSize;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private — per-category calibration logic
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs calibration for a single <paramref name="dataType"/>:
    /// bins verified records, computes predicted-vs-actual accuracy, fits Platt parameters,
    /// persists <see cref="CalibrationRecord"/> rows, rotates active
    /// <see cref="CalibrationParameter"/>, and triggers drift detection.
    /// </summary>
    private async Task CalibrateDataTypeAsync(DataType dataType, CancellationToken ct)
    {
        bool sufficient = await HasSufficientDataAsync(dataType, ct);

        if (!sufficient)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-LookbackDays);
            int actualCount = await _db.ExtractedData
                .CountAsync(
                    e => e.DataType == dataType
                      && e.VerifiedByUserId != null
                      && e.CreatedAt >= cutoff,
                    ct);

            _logger.LogWarning(
                "RunWeeklyCalibration: Insufficient data for {DataType}. " +
                "Current={Current} Required={Required}. Using uncalibrated model.",
                dataType, actualCount, PlattScaler.MinimumSampleSize);

            return;
        }

        // ── 1. Load verified records for the past 90 days ─────────────────────
        DateTime lookbackCutoff = DateTime.UtcNow.AddDays(-LookbackDays);

        var verifiedRecords = await _db.ExtractedData
            .Where(e => e.DataType         == dataType
                     && e.VerifiedByUserId != null
                     && e.CreatedAt        >= lookbackCutoff)
            .Select(e => new
            {
                e.ConfidenceScore,
                IsCorrect = e.VerificationStatus == VerificationStatus.Verified,
            })
            .ToListAsync(ct);

        int totalSampleSize = verifiedRecords.Count;
        DateTime runDate    = DateTime.UtcNow;

        // ── 2. Build per-bin statistics ───────────────────────────────────────
        double  totalWeightedDrift = 0.0;
        int     totalWeightedN     = 0;
        double  maxDrift           = 0.0;
        double  maxDriftPredicted  = 0.0;
        double  maxDriftActual     = 0.0;
        bool    driftDetected      = false;

        for (int bin = 0; bin < BinCount; bin++)
        {
            double binLow  = bin       * (1.0 / BinCount);
            double binHigh = (bin + 1) * (1.0 / BinCount);

            int binStart = bin       * 10;
            int binEnd   = (bin + 1) * 10;

            var binItems = verifiedRecords
                .Where(r => r.ConfidenceScore >= binLow &&
                            (bin == BinCount - 1
                                ? r.ConfidenceScore <= binHigh
                                : r.ConfidenceScore <  binHigh))
                .ToList();

            if (binItems.Count == 0) continue;

            double predictedAcc = binItems.Average(r => (double)r.ConfidenceScore) * 100.0;
            double actualAcc    = binItems.Count(r => r.IsCorrect) * 100.0 / binItems.Count;
            double driftPct     = Math.Abs(predictedAcc - actualAcc);
            bool   binDrift     = driftPct > DriftThreshold * 100.0; // convert to % scale

            // Track weighted average drift
            totalWeightedDrift += driftPct  * binItems.Count;
            totalWeightedN     += binItems.Count;

            if (driftPct > maxDrift)
            {
                maxDrift          = driftPct;
                maxDriftPredicted = predictedAcc;
                maxDriftActual    = actualAcc;
            }

            if (binDrift) driftDetected = true;

            // Persist the calibration record for this bin.
            _db.CalibrationRecords.Add(new CalibrationRecord
            {
                CalibrationRunDate = runDate,
                DataType           = dataType,
                PredictedAccuracy  = predictedAcc,
                ActualAccuracy     = actualAcc,
                DriftPercentage    = driftPct,
                BinStart           = binStart,
                BinEnd             = binEnd,
                SampleSize         = binItems.Count,
                DriftDetected      = binDrift,
            });
        }

        // ── 3. Fit Platt scaling parameters ────────────────────────────────────
        var samples = verifiedRecords
            .Select(r => ((double)r.ConfidenceScore, r.IsCorrect))
            .ToList();

        var (slope, intercept) = PlattScaler.FitParameters(samples);

        // ── 4. Rotate active CalibrationParameter ─────────────────────────────
        var oldParams = await _db.CalibrationParameters
            .Where(p => p.DataType == dataType && p.IsActive)
            .ToListAsync(ct);

        foreach (var old in oldParams)
        {
            old.IsActive  = false;
            old.UpdatedAt = DateTime.UtcNow;
        }

        _db.CalibrationParameters.Add(new CalibrationParameter
        {
            DataType               = dataType,
            Slope                  = slope,
            Intercept              = intercept,
            LastCalibratedAt       = runDate,
            VerificationSampleSize = totalSampleSize,
            IsActive               = true,
        });

        // ── 5. Drift detection — generate alert if drift > 5% ─────────────────
        if (driftDetected && totalWeightedN > 0)
        {
            double weightedAvgDrift = totalWeightedDrift / totalWeightedN;

            _db.CalibrationDriftAlerts.Add(new CalibrationDriftAlert
            {
                GeneratedAt       = runDate,
                DataType          = dataType,
                PredictedAccuracy = maxDriftPredicted,
                ActualAccuracy    = maxDriftActual,
                DriftPercentage   = maxDrift,
                IsAcknowledged    = false,
            });

            _logger.LogWarning(
                "CalibrationDrift: Drift detected for {DataType}. " +
                "MaxDrift={MaxDrift:F2}% WeightedAvgDrift={AvgDrift:F2}% " +
                "Predicted={Predicted:F2}% Actual={Actual:F2}%.",
                dataType, maxDrift, weightedAvgDrift, maxDriftPredicted, maxDriftActual);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CalibrateDataTypeAsync: {DataType} complete. " +
            "Samples={Samples} Slope={Slope:F4} Intercept={Intercept:F4} DriftDetected={Drift}.",
            dataType, totalSampleSize, slope, intercept, driftDetected);
    }
}
